using System.Security.Cryptography;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    [DataRow(33, 3, false)]
    [DataRow(131, 7, true)]
    public async Task ActualStoppedChildRecoveryAuthenticatesTheWholeCensusAndKeepsPermanentQuarantine(int workers, int residents, bool held)
    {
        WarpLogicalMachineLayout casLayout = WarpManagedAtomicKernels.Create32()[2];
        WarpLogicalMachineLayout censusLayout = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableSchedulerServices)
            .GetMethod(nameof(WarpPortableSchedulerServices.DisposeStoppedCensus))!);
        Assert.AreEqual(WarpCoreCLRRecoveryCatalog.CompareExchange, WarpIrHash.Compute(casLayout.Kernel), StringComparer.Ordinal);
        Assert.AreEqual(WarpCoreCLRRecoveryCatalog.DisposeStoppedCensus, WarpIrHash.Compute(censusLayout.Kernel), StringComparer.Ordinal);
        WarpCoreCLRWorkerKernel cas = await WarpCoreCLRWorkerKernel.CompileAsync(casLayout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var casOwner = cas.ConfigureAwait(false);
        WarpCoreCLRWorkerLease casLease = cas.TryAcquireLease()!;
        await using var casLeaseOwner = casLease.ConfigureAwait(false);
        WarpCoreCLRWorkerKernel census = await WarpCoreCLRWorkerKernel.CompileAsync(censusLayout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var censusOwner = census.ConfigureAwait(false);
        WarpCoreCLRWorkerLease censusLease = census.TryAcquireLease()!;
        await using var censusLeaseOwner = censusLease.ConfigureAwait(false);
        foreach (int quantum in new[] { censusLayout.MaximumBlockCost, 4096 })
        { await RecoverStoppedCensusAsync(workers, residents, held, quantum, casLease, censusLease).ConfigureAwait(false); }
    }

    private async Task RecoverStoppedCensusAsync(int workers, int residents, bool held, int quantum,
        WarpCoreCLRWorkerLease cas, WarpCoreCLRWorkerLease census)
    {
        const uint controller = 119;
        uint[] arena = SeedRecoveryArena((uint)workers, (uint)residents, held, controller);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        var sourceLayout = new WarpLogicalMachineLayout(InfiniteArenaKernel());
        WarpCoreCLRWorkerKernel source = await WarpCoreCLRWorkerKernel.CompileAsync(sourceLayout,
            new() { QuantumTimeout = TimeSpan.FromSeconds(1) }, CancellationToken.None).ConfigureAwait(false);
        await using var sourceOwner = source.ConfigureAwait(false);
        WarpCoreCLRWorkerLease sourceLease = source.TryAcquireLease()!;
        await using var sourceLeaseOwner = sourceLease.ConfigureAwait(false);
        WarpCoreCLRPreparedCleanup[] cleanup = RecoveryBindings(arena, scheduler, controller, held, cas, census);
        WarpCoreCLRCommandAdmission[] admissions = await RegisterRecoveryCensusAsync(workers, arena, scheduler, sourceLayout, cleanup, held ? controller : 0).ConfigureAwait(false);
        uint[] original = (uint[])arena.Clone(), originalState = (uint[])admissions[0].State.Clone();
        WarpHostException failure = await Assert.ThrowsAsync<WarpHostException>(() => sourceLease.ExecuteOwnedManagedQuantumAsync(
            [[0]], [], 0, admissions[0].State, 4, int.MaxValue, arena, admissions[0], CancellationToken.None)).ConfigureAwait(false);
        CollectionAssert.AreEqual(original, arena); CollectionAssert.AreEqual(originalState, admissions[0].State);
        WarpCoreCLRStoppedCommands.Command command = WarpCoreCLRStoppedCommands.FromFailure(failure)!;
        Assert.IsNotNull(command); AssertTerminated(sourceLease.ProcessId);
        ValidateStoppedForgery(command, admissions[0].ExactTicket);
        WarpCoreCLRQuarantineRecovery recovery = WarpCoreCLRStoppedCommands.Mint(command, admissions[0].ExactTicket);
        Assert.HasCount(workers, recovery.Census.Admissions);
        Assert.IsTrue(recovery.Census.Admissions.All(admission => admissions.Contains(admission)));
        foreach (WarpCoreCLRPreparedCleanup binding in cleanup)
        { await RunRecoveryServiceAsync(binding, arena, recovery, quantum).ConfigureAwait(false); }
        await recovery.AcknowledgeGeneratedAbortAsync(admissions[0].ExactTicket, CancellationToken.None).ConfigureAwait(false);
        Assert.Throws<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.Mint(command, admissions[0].ExactTicket));
        await AssertRecoveryTerminalAsync(arena, scheduler, workers, admissions).ConfigureAwait(false);
        TestContext.WriteLine($"Authentic stopped command {command.Ordinal}, workers={workers}, residents={residents}, held={held}, quantum={quantum}, prepared-kinds={cleanup.Length}; CAS process={cas.ProcessId}, module={cas.CompiledModule} reused by both purposes.");
    }

    private static uint[] SeedRecoveryArena(uint workers, uint residents, bool held, uint controller)
    {
        WarpPortableHeapSchema heap = new([new(1, "worker-recovery:Object", WarpPortableHeapLayout.Class, 1, [1])]);
        uint[] template = heap.CreateArena(531, workers * 32 + 256, workers * 2 + 8, workers * 3 + 8, workers, workers * 32 + 256);
        WarpPortableSchedulerSchema schema = new(workers, residents, 4096, 4, 100000, 0, 16, [new(0, 0, [])], []);
        uint[] arena = schema.AttachToEmptyHeap(template);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        // This is a pre-source fixture setup oracle; the stopped cleanup itself runs only in actual native child modules.
        Assert.AreEqual(0u, Interlocked.CompareExchange(ref arena[scheduler + WarpPortableSchedulerLayout.ControllerOwner], controller, 0));
        for (uint physical = 0; physical < residents; physical++)
        { Assert.AreEqual(0u, WarpPortableSchedulerServices.TryAcquireWorker(arena, scheduler, controller, physical)); }
        Assert.AreEqual(0u, WarpPortableSchedulerServices.AcquireHeapService(arena, scheduler, controller, 0, 1));
        Assert.AreEqual(0u, WarpPortableHeapServices.AllocateObject(arena, 1));
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.PendingResult]);
        if (!held) { Assert.AreEqual(controller, Interlocked.CompareExchange(ref arena[scheduler + WarpPortableSchedulerLayout.ControllerOwner], 0, controller)); }
        return arena;
    }

    private static WarpCoreCLRPreparedCleanup[] RecoveryBindings(uint[] arena, uint scheduler, uint controller, bool held,
        WarpCoreCLRWorkerLease cas, WarpCoreCLRWorkerLease census)
    {
        var bindings = new List<WarpCoreCLRPreparedCleanup>();
        uint word = scheduler + WarpPortableSchedulerLayout.ControllerOwner;
        if (!held) { bindings.Add(new(cas, WarpCoreCLRCleanupPurpose.AcquireFreeController, 0, WordBanks(word, 0, controller), [])); }
        bindings.Add(new(census, WarpCoreCLRCleanupPurpose.StoppedFault, 0,
            WordBanks(scheduler, controller, arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration],
                arena[scheduler + WarpPortableSchedulerLayout.GCEpoch], 0, 1, 0, 17, 19, 1), []));
        bindings.Add(new(cas, WarpCoreCLRCleanupPurpose.ReleaseCapturedController, controller, WordBanks(word, controller, 0), []));
        return bindings.ToArray();
    }

    private static async Task<WarpCoreCLRCommandAdmission[]> RegisterRecoveryCensusAsync(int workers, uint[] arena, uint scheduler,
        WarpLogicalMachineLayout layout, WarpCoreCLRPreparedCleanup[] cleanup, uint owner)
    {
        string schema = Convert.ToHexString(SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(
            arena.AsSpan((int)(scheduler + WarpPortableSchedulerLayout.SchemaHash), 8))));
        string ir = WarpIrHash.Compute(layout.Kernel);
        WarpCoreCLRCommandAdmission[] admissions = new WarpCoreCLRCommandAdmission[workers];
        foreach (ref WarpCoreCLRCommandAdmission admission in admissions.AsSpan())
        {
            admission = new(new object(), layout.CreateInitialState(4, long.MaxValue), arena, schema, ir, ir,
                arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration], arena[scheduler + WarpPortableSchedulerLayout.GCEpoch], owner, cleanup);
        }
        foreach (WarpCoreCLRCommandAdmission admission in admissions)
        { await WarpCoreCLRStoppedCommands.RegisterPausedAdmissionAsync(admission).ConfigureAwait(false); }
        WarpCoreCLRCommandAdmission predecessor = admissions[1];
        WarpCoreCLRCommandAdmission successor = ReplacementAdmission(predecessor, layout.CreateInitialState(4, long.MaxValue));
        await WarpCoreCLRStoppedCommands.ReplacePausedAdmissionAsync(predecessor, successor).ConfigureAwait(false);
        admissions[1] = successor;
        await Assert.ThrowsAsync<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.RegisterPausedAdmissionAsync(predecessor)).ConfigureAwait(false);
        predecessor = admissions[2]; successor = ReplacementAdmission(predecessor, layout.CreateInitialState(4, long.MaxValue));
        await WarpCoreCLRStoppedCommands.RetirePausedAdmissionAsync(predecessor).ConfigureAwait(false);
        await WarpCoreCLRStoppedCommands.RegisterPausedAdmissionAsync(successor).ConfigureAwait(false);
        admissions[2] = successor;
        return admissions;
    }

    private static WarpCoreCLRCommandAdmission ReplacementAdmission(WarpCoreCLRCommandAdmission predecessor, uint[] state) =>
        new(predecessor.ExactTicket, state, predecessor.Arena, predecessor.SchemaHash, predecessor.PlanHash, predecessor.SourceIrHash,
            predecessor.DispatchGeneration, predecessor.CollectionGeneration, predecessor.CapturedControllerOwner, predecessor.Cleanup);

    private static void ValidateStoppedForgery(WarpCoreCLRStoppedCommands.Command command, object exactTicket)
    {
        Assert.Throws<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.Mint(command, new object()));
        var forged = new WarpCoreCLRStoppedCommands.Command(command.Ordinal, command.Process, command.Sequence, command.Digest,
            command.State, command.Arena, command.IrHash, command.Admission);
        Assert.Throws<InvalidOperationException>(() => forged.MarkStopped(new object()));
        Assert.Throws<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.Mint(forged, exactTicket));
    }

    private static async Task RunRecoveryServiceAsync(WarpCoreCLRPreparedCleanup binding, uint[] arena,
        WarpCoreCLRQuarantineRecovery recovery, int quantum)
    {
        uint[] state = binding.Lease.Layout.CreateInitialState(32, 100_000_000);
        uint[][] inputs = Enumerable.Range(0, binding.Lease.Layout.Kernel.InputBufferCount).Select(index => new[] { binding.InputWord(index) }).ToArray();
        int operations = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && operations++ < 100000)
        {
            await binding.Lease.ExecuteQuarantineQuantumAsync(inputs, [], 0, state, 32,
                Math.Max(quantum, binding.Lease.Layout.MaximumBlockCost), arena, recovery, CancellationToken.None).ConfigureAwait(false);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]);
        Assert.AreEqual(binding.ExpectedResult, state[WarpLogicalMachineLayout.ResultOffset]);
    }

    private static async Task AssertRecoveryTerminalAsync(uint[] arena, uint scheduler, int workers, WarpCoreCLRCommandAdmission[] admissions)
    {
        Assert.AreEqual(WarpPortableSchedulerLayout.DisposedContext, arena[scheduler + WarpPortableSchedulerLayout.ContextState]);
        Assert.AreEqual(0u, arena[scheduler + WarpPortableSchedulerLayout.RunningCount]);
        Assert.AreEqual(0u, arena[scheduler + WarpPortableSchedulerLayout.ControllerOwner]);
        Assert.AreEqual(1u, arena[scheduler + WarpPortableSchedulerLayout.OutputQuarantined]);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LeaseState]); Assert.AreEqual(0u, arena[WarpPortableHeapLayout.PendingResult]);
        for (uint worker = 0; worker < workers; worker++)
        {
            uint entry = scheduler + arena[scheduler + WarpPortableSchedulerLayout.WorkerStart] + worker * WarpPortableSchedulerLayout.WorkerWords;
            Assert.AreEqual(WarpPortableSchedulerLayout.Disposed, arena[entry]);
        }
        foreach (WarpCoreCLRCommandAdmission admission in admissions)
        {
            await Assert.ThrowsAsync<WarpHostException>(() => WarpCoreCLRWordTransaction.AcquireAsync(admission.State, [], CancellationToken.None)).ConfigureAwait(false);
        }
        await Assert.ThrowsAsync<WarpHostException>(() => WarpCoreCLRWordTransaction.AcquireAsync(new uint[1], arena, CancellationToken.None)).ConfigureAwait(false);
    }

    private static uint[][] WordBanks(params uint[] words) => words.Select(word => new[] { word }).ToArray();
}
