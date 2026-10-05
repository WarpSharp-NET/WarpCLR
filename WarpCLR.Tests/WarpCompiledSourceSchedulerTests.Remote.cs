using System.Security.Cryptography;
using System.Text;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    [TestMethod]
    public async Task ActualRemoteCompiledSourceUsesBoundariesWithoutParentSourceJit()
    {
        uint[] counts = Enumerable.Range(1, 13).Select(value => (uint)value).ToArray();
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 13, 2, [counts]);
        RemoteModules modules = await RemoteModules.CreateAsync(context, new()).ConfigureAwait(false);
        await using var ownedModules = modules.ConfigureAwait(false);
        var adapter = new WarpCompiledRemoteSourceAdapter(context, modules.Source, modules.CompareExchange, modules.Census);
        for (int attempt = 0; context.State == WarpPortableSchedulerLayout.Active && attempt < 10000; attempt++)
        {
            WarpCompiledWorkerTicket? ticket = context.Claim((uint)attempt % 2);
            if (ticket is null) { continue; }
            Assert.ThrowsExactly<InvalidOperationException>(() => context.CommitRetainedSource(ticket));
            WarpCompiledRemotePreparedRun run = await adapter.PrepareAsync(ticket).ConfigureAwait(false);
            Assert.IsNull(context.Controller.TryAcquire(), "An owned remote command suspends ordinary controller acquisition.");
            Assert.ThrowsExactly<InvalidOperationException>(() => context.Execute(ticket));
            Assert.IsNull(await adapter.ExecuteAsync(run).ConfigureAwait(false));
            context.Commit(ticket);
        }
        Assert.AreEqual(WarpPortableSchedulerLayout.CompletedContext, context.State);
        Assert.IsFalse(context.Plan.HasLocalSourceKernel);
        for (uint worker = 0; worker < 13; worker++)
        {
            Assert.AreEqual(counts[worker] * (counts[worker] + 1) / 2, context.Result(worker, context.Dispatch)[0]);
            Assert.IsGreaterThan(0u, context.WorkerQuanta(worker));
            Assert.AreEqual(context.MachineState(worker)[WarpLogicalMachineLayout.RemainingStepsLowOffset],
                context.Arena[Worker(context, worker) + WarpPortableSchedulerLayout.StepRemainingLow]);
        }
    }

    [TestMethod]
    [DataRow(33, 3, false)]
    [DataRow(131, 7, true)]
    public async Task AuthenticStoppedChildCensusDisposesEveryLogicalParticipantAndPendingResult(int participants, int residency, bool held)
    {
        uint workers = checked((uint)participants), residents = checked((uint)residency);
        var source = CaptureSource(nameof(Kernels.OwnerStopLoop));
        var plan = new WarpCompiledSourcePlan(source.Graph, source.Schema, source.Program, workers, residents,
            4, long.MaxValue, int.MaxValue, Services.Value);
        uint[] delays = new uint[workers]; delays[0] = uint.MaxValue;
        WarpCompiledSourceContext context = Bind(plan, [new uint[workers], new uint[workers], new uint[workers], delays]);
        RemoteModules modules = await RemoteModules.CreateAsync(context,
            new WarpCoreCLRWorkerOptions { QuantumTimeout = TimeSpan.FromSeconds(1) }).ConfigureAwait(false);
        await using var ownedModules = modules.ConfigureAwait(false);
        var adapter = new WarpCompiledRemoteSourceAdapter(context, modules.Source, modules.CompareExchange, modules.Census);
        context.QueueEntryAllocation(1, ObjectType(context));
        WarpCompiledWorkerTicket first = context.Claim(0)!;
        Assert.AreEqual(0u, first.Worker);
        WarpCompiledWorkerTicket allocation = context.Claim(1)!;
        for (uint physical = 2; physical < residents; physical++) { Assert.IsNotNull(context.Claim(physical)); }
        WarpCompiledWorkerTicket pending = await LeavePendingResultAsync(context, allocation, adapter).ConfigureAwait(false);
        Assert.AreEqual(1u, context.Arena[WarpPortableHeapLayout.PendingResult]);
        Assert.IsNull(await adapter.ExecuteAsync(await adapter.PrepareAsync(first).ConfigureAwait(false)).ConfigureAwait(false));
        context.Commit(first);
        WarpCompiledWorkerTicket failed = await FindRemoteWorkerZeroAsync(context, adapter).ConfigureAwait(false);
        WarpCompiledControllerGrant? grant = held ? context.Controller.TryAcquire() : null;
        WarpCompiledRemotePreparedRun run = await adapter.PrepareAsync(failed, grant).ConfigureAwait(false);
        Assert.HasCount(participants, run.Admissions);
        Assert.IsLessThan(32, run.Cleanup.Length);
        Assert.HasCount(held ? 2 : 3, run.Cleanup);
        uint[] before = context.MachineState(failed.Worker).ToArray();
        before[WarpCompiledSourceBoundary.StateOffset] = WarpCompiledSourceBoundary.Acknowledged;
        // An original CIL boundary yields after every source instruction, even
        // inside a large loop. Stop only this admitted child after its complete
        // census is prepared to exercise the actual RPC deadline and recovery.
        await StopOwnedNativeWorkerAsync(modules.Source).ConfigureAwait(false);
        WarpCoreCLRQuarantineRecovery recovery = (await adapter.ExecuteAsync(run).ConfigureAwait(false))!;
        Assert.HasCount(participants, recovery.Census.Admissions);
        Assert.IsGreaterThan(0UL, recovery.StoppedOrdinal);
        CollectionAssert.AreEqual(before, context.MachineState(failed.Worker).ToArray());
        AssertRemoteDisposed(context, pending);
        Assert.IsFalse(plan.HasLocalSourceKernel);
        Assert.IsTrue(modules.Source.IsFaulted);
        await Assert.ThrowsAsync<WarpHostException>(() => modules.CompareExchange.ExecuteManagedQuantumAsync(
            [[context.Scheduler + WarpPortableSchedulerLayout.ControllerOwner], [0], [1]], [], 0,
            modules.CompareExchange.Layout.CreateInitialState(32, 1000), 32, 4096, context.Arena, CancellationToken.None)).ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.AcknowledgeGeneratedAbortAsync(failed, CancellationToken.None)).ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ExecuteAsync(run)).ConfigureAwait(false);
    }

    private static async Task<WarpCompiledWorkerTicket> LeavePendingResultAsync(WarpCompiledSourceContext context,
        WarpCompiledWorkerTicket initial, WarpCompiledRemoteSourceAdapter adapter)
    {
        WarpCompiledWorkerTicket? current = initial;
        for (int attempt = 0; attempt < 100000; attempt++)
        {
            WarpCompiledWorkerTicket? ticket = current ?? context.Claim(1);
            current = null;
            if (ticket is null) { continue; }
            if (context.HasRuntimeHelper(ticket)) { context.Execute(ticket); }
            else { Assert.IsNull(await adapter.ExecuteAsync(await adapter.PrepareAsync(ticket).ConfigureAwait(false)).ConfigureAwait(false)); }
            if (context.Arena[WarpPortableHeapLayout.PendingResult] != 0) { return ticket; }
            context.Commit(ticket);
        }
        throw new AssertFailedException("The actual compiled allocator did not leave a pending reference result.");
    }

    private static async Task<WarpCompiledWorkerTicket> FindRemoteWorkerZeroAsync(WarpCompiledSourceContext context, WarpCompiledRemoteSourceAdapter adapter)
    {
        for (int attempt = 0; attempt < context.Plan.Workers * 2; attempt++)
        {
            WarpCompiledWorkerTicket? ticket = context.Claim(0);
            if (ticket is null) { continue; }
            if (ticket.Worker == 0) { return ticket; }
            Assert.IsNull(await adapter.ExecuteAsync(await adapter.PrepareAsync(ticket).ConfigureAwait(false)).ConfigureAwait(false));
            context.Commit(ticket);
        }
        throw new AssertFailedException("Fair generated scheduler did not revisit the admitted primary worker.");
    }

    private static void AssertRemoteDisposed(WarpCompiledSourceContext context, WarpCompiledWorkerTicket pending)
    {
        Assert.AreEqual(WarpPortableSchedulerLayout.DisposedContext, context.State);
        Assert.AreEqual(0u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.ControllerOwner]);
        Assert.AreEqual(0u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.RunningCount]);
        Assert.AreEqual(1u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.OutputQuarantined]);
        Assert.AreEqual(0u, context.Arena[WarpPortableHeapLayout.LeaseState]);
        Assert.AreEqual(0u, context.Arena[WarpPortableHeapLayout.PendingResult]);
        for (uint worker = 0; worker < context.Plan.Workers; worker++) { Assert.AreEqual(WarpPortableSchedulerLayout.Disposed, context.WorkerState(worker)); }
        Assert.IsNull(context.Claim(0));
        Assert.IsNull(context.Controller.TryAcquire());
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Execute(pending));
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Result(0, context.Dispatch));
    }

    // Retained historical instrumentation is an explicit canonical rejection witness.
    // Positive timeout execution uses the captured uint-count OwnerStopLoop CIL.
    private static WarpPortableWordLoweredProgram DelaySourceEntry(WarpPortableWordLoweredProgram program)
    {
        WarpPortableWordBody body = program.Bodies[0];
        WarpControlFlowKernel original = program.Kernel;
        WarpControlFlowFunction function = original.Functions[body.Function - 1];
        WarpBasicBlock target = function.Blocks[body.SourceBlocks[0].Block];
        WarpIrInstruction[] instructions = [
            new(function.ValueCount, WarpManagedFrameOpCode.LoadPrivateWord, immediate: checked((uint)body.Arguments[^1].WordOffset)),
            new(function.ValueCount + 1, original.Functions.Count, [function.ValueCount], 1), .. target.Instructions];
        WarpBasicBlock changed = new(target.Id, target.Parameters, instructions, target.Terminator);
        WarpControlFlowFunction[] functions = original.Functions.Select(item => item.Id == function.Id ?
            new WarpControlFlowFunction(item.Id, item.Name, item.ParameterCount, item.Blocks.Select(block => block.Id == target.Id ? changed : block)) : item).ToArray();
        WarpControlFlowFunction delay = DelayFunction(functions.Length);
        WarpLogicalExecutionMetadata execution = original.Execution!;
        var metadata = new WarpLogicalExecutionMetadata([.. execution.Bodies, new(0, true, Enumerable.Repeat(0, delay.Blocks.Count))],
            execution.RecursiveCalls, execution.FrameOwners, execution.RuntimeStateAccess);
        var kernel = new WarpControlFlowKernel(original.Name + "/native-rpc-delay-witness", original.InputBufferCount,
            original.ScalarArgumentCount, original.Blocks, original.Reduction, [.. functions, delay], metadata);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WarpPortableWordLowerer.Version + "\n" + program.VerifiedHash + "\n" + WarpIrHash.Compute(kernel))));
        return program with { Kernel = kernel, LoweredHash = hash };
    }
}
