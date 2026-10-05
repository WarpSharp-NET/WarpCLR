using System.Diagnostics;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    public async Task DeadlineBeforeNativeJitTerminatesTheActualAdmittedChild()
    {
        bool admitted = false;
        int pid = 0;
        var hooks = new WarpCoreCLRWorkerTestHooks(1) { Started = id => pid = id, PlanAdmitted = () => admitted = true, Killed = RecordKill };
        var options = new WarpCoreCLRWorkerOptions { CompilationTimeout = TimeSpan.FromSeconds(10) };
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<WarpHostException>(() => WarpCoreCLRWorkerKernel.CompileAsync(new(RichKernel()), options,
            CancellationToken.None, hooks)).ConfigureAwait(false);
        Assert.IsTrue(admitted, "The actual child must have admitted the full plan before the deadline.");
        Assert.IsTrue(watch.Elapsed < options.CompilationTimeout + options.CleanupTimeout + TimeSpan.FromSeconds(2));
        AssertTerminated(pid);
    }

    [TestMethod]
    public async Task DeadlineInsideActualNativeJitCallbackTerminatesPrepareMethod()
    {
        string? profiler = Environment.GetEnvironmentVariable("WARP_JIT_TEST_PROFILER");
        Assert.IsNotNull(profiler, "This gate requires the recorded native profiler probe artifact.");
        string marker = Path.Combine(Path.GetTempPath(), "warp-jit-block-" + Guid.NewGuid().ToString("N") + ".txt");
        int pid = 0; bool jit = false;
        var hooks = new WarpCoreCLRWorkerTestHooks
        {
            Started = id => pid = id, JitEntering = _ => jit = true,
            Killed = RecordKill,
            Environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CORECLR_ENABLE_PROFILING"] = "1", ["CORECLR_PROFILER"] = "{64450E59-928C-4F80-BEF0-9A173E685FA1}",
                ["CORECLR_PROFILER_PATH"] = profiler, ["WARP_JIT_PROBE_MARKER"] = marker,
            },
        };
        var options = new WarpCoreCLRWorkerOptions { CompilationTimeout = TimeSpan.FromSeconds(15) };
        try
        {
            await Assert.ThrowsAsync<WarpHostException>(() => WarpCoreCLRWorkerKernel.CompileAsync(new(RichKernel()), options,
                CancellationToken.None, hooks)).ConfigureAwait(false);
            Assert.IsTrue(jit); Assert.IsTrue(File.Exists(marker), "Native JITCompilationStarted must actually have blocked for the admitted collectible module.");
            string receipt = await File.ReadAllTextAsync(marker).ConfigureAwait(false);
            StringAssert.Contains(receipt, "JITCompilationStarted", StringComparison.Ordinal);
            TestContext.WriteLine(receipt);
            AssertTerminated(pid);
        }
        finally { File.Delete(marker); }
    }

    [TestMethod]
    public async Task DeadlineKillsInheritedPipeHolderWithoutAwaitingPipeEof()
    {
        int parent = 0, child = 0;
        var hooks = new WarpCoreCLRWorkerTestHooks(2)
        {
            Started = id => parent = id,
            InheritedChild = id =>
            {
                child = id;
                if (OperatingSystem.IsLinux())
                { Assert.AreEqual(parent, WarpCoreCLRWorkerContainment.GetProcessGroup(child)); }
            },
            Killed = RecordKill,
        };
        var options = new WarpCoreCLRWorkerOptions { CompilationTimeout = TimeSpan.FromSeconds(10) };
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<WarpHostException>(() => WarpCoreCLRWorkerKernel.CompileAsync(new(RichKernel()), options,
            CancellationToken.None, hooks)).ConfigureAwait(false);
        Assert.IsGreaterThan(0, child); AssertTerminated(parent); AssertTerminated(child);
        Assert.IsTrue(watch.Elapsed < options.CompilationTimeout + options.CleanupTimeout + TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task NativeExecutionDeadlineQuarantinesArenaAndCommitsNoPartialWords()
    {
        var layout = new WarpLogicalMachineLayout(InfiniteArenaKernel());
        var options = new WarpCoreCLRWorkerOptions { QuantumTimeout = TimeSpan.FromSeconds(1) };
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, options, CancellationToken.None).ConfigureAwait(false);
        await using var owner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        uint[] state = layout.CreateInitialState(1, long.MaxValue), arena = [42];
        uint[] original = (uint[])state.Clone();
        await Assert.ThrowsAsync<WarpHostException>(() => lease.ExecuteManagedQuantumAsync([[0]], [], 0, state, 1,
            int.MaxValue, arena, CancellationToken.None)).ConfigureAwait(false);
        CollectionAssert.AreEqual(original, state); CollectionAssert.AreEqual(new uint[] { 42 }, arena);
        Assert.IsTrue(lease.IsFaulted); AssertTerminated(lease.ProcessId);
        await Assert.ThrowsAsync<WarpHostException>(() => WarpCoreCLRWordTransaction.AcquireAsync(new uint[1], arena, CancellationToken.None)).ConfigureAwait(false);
    }

    private static WarpControlFlowKernel InfiniteArenaKernel() => new("worker-infinite-arena", 1, 0,
    [new WarpBasicBlock(0, [], [new(0, WarpIrOpCode.Constant), new(1, WarpIrOpCode.Constant, immediate: 123),
        new(2, WarpManagedMemoryOpCode.StoreWord, 0, 1)], new WarpBranchTerminator(new(1, []))),
     new WarpBasicBlock(1, [], [new(3, WarpIrOpCode.Constant, immediate: 1)],
        new WarpConditionalBranchTerminator(3, new(1, []), new(2, [3]))),
     new WarpBasicBlock(2, [new WarpBlockParameter(4)], [], new WarpReturnTerminator(4))]);

    private static void AssertTerminated(int pid)
    {
        Assert.IsGreaterThan(0, pid);
        try
        {
            using Process process = Process.GetProcessById(pid);
            if (OperatingSystem.IsLinux())
            {
                string[] status = File.ReadAllLines($"/proc/{pid}/status");
                if (status.Any(line => line.StartsWith("State:", StringComparison.Ordinal) && line.Contains("Z (zombie)", StringComparison.Ordinal))) { return; }
            }
            Assert.IsTrue(process.HasExited, "Contained child must have terminated and released inherited pipes.");
        }
        catch (ArgumentException) { }
        catch (DirectoryNotFoundException) { }
        catch (FileNotFoundException) { }
    }

    private void RecordKill(WarpCoreCLRContainmentEvidence evidence)
    {
        TestContext.WriteLine(evidence.ToString());
        Assert.AreEqual(evidence.ChildProcess, evidence.RegisteredPrivateGroup);
        Assert.AreEqual(evidence.ChildProcess, evidence.ChildGroup);
        Assert.AreNotEqual(evidence.ParentGroup, evidence.ChildGroup);
        Assert.AreNotEqual(evidence.ParentProcess, evidence.ChildProcess);
        Assert.IsTrue(evidence.GroupSignalled); Assert.AreEqual(9, evidence.Signal);
        Assert.AreEqual(0, evidence.SignalResult);
    }
}
