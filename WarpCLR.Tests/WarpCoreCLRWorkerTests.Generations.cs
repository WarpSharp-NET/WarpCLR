using System.Security.Cryptography;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    [DataRow(nameof(WarpPortableSchedulerServices.RequestCollection), false)]
    [DataRow(nameof(WarpPortableSchedulerServices.RequestCollection), true)]
    [DataRow(nameof(WarpPortableSchedulerServices.BeginDispatch), false)]
    [DataRow(nameof(WarpPortableSchedulerServices.BeginDispatch), true)]
    public async Task ExactCommittedNativeGenerationReceiptsReplaceAllPausedOwnersAndRejectReplay(string operation, bool largeQuantum)
    {
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableSchedulerServices).GetMethod(operation)!);
        Assert.AreEqual(string.Equals(operation, nameof(WarpPortableSchedulerServices.RequestCollection), StringComparison.Ordinal) ? WarpCoreCLRRecoveryCatalog.RequestCollection :
            WarpCoreCLRRecoveryCatalog.BeginDispatch, WarpIrHash.Compute(layout.Kernel), StringComparer.Ordinal);
        WarpCoreCLRWorkerKernel service = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var serviceOwner = service.ConfigureAwait(false);
        WarpCoreCLRWorkerLease serviceLease = service.TryAcquireLease()!;
        await using var serviceLeaseOwner = serviceLease.ConfigureAwait(false);
        WarpCoreCLRWorkerKernel cas = await WarpCoreCLRWorkerKernel.CompileAsync(WarpManagedAtomicKernels.Create32()[2], new(), CancellationToken.None).ConfigureAwait(false);
        await using var casOwner = cas.ConfigureAwait(false);
        WarpCoreCLRWorkerLease casLease = cas.TryAcquireLease()!;
        await using var casLeaseOwner = casLease.ConfigureAwait(false);
        WarpLogicalMachineLayout censusLayout = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableSchedulerServices).GetMethod(nameof(WarpPortableSchedulerServices.DisposeStoppedCensus))!);
        WarpCoreCLRWorkerKernel census = await WarpCoreCLRWorkerKernel.CompileAsync(censusLayout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var censusOwner = census.ConfigureAwait(false);
        WarpCoreCLRWorkerLease censusLease = census.TryAcquireLease()!;
        await using var censusLeaseOwner = censusLease.ConfigureAwait(false);
        WarpCoreCLRWorkerKernel publication = await WarpCoreCLRWorkerKernel.CompileAsync(cas.Layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var publicationOwner = publication.ConfigureAwait(false);
        WarpCoreCLRWorkerLease publicationLease = publication.TryAcquireLease()!;
        await using var publicationLeaseOwner = publicationLease.ConfigureAwait(false);
        WarpLogicalMachineLayout stoppedLayout = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableSchedulerServices).GetMethod(nameof(WarpPortableSchedulerServices.DisposeStoppedController))!);
        WarpCoreCLRWorkerKernel stopped = await WarpCoreCLRWorkerKernel.CompileAsync(stoppedLayout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var stoppedOwner = stopped.ConfigureAwait(false);
        WarpCoreCLRWorkerLease stoppedLease = stopped.TryAcquireLease()!;
        await using var stoppedLeaseOwner = stoppedLease.ConfigureAwait(false);
        try { await RunGenerationReceiptAsync(operation, largeQuantum, serviceLease, casLease, censusLease, publicationLease, stoppedLease).ConfigureAwait(false); }
        catch (Exception failure) { TestContext.WriteLine(failure.ToString()); throw; }
    }

    private static async Task RunGenerationReceiptAsync(string operation, bool largeQuantum, WarpCoreCLRWorkerLease service,
        WarpCoreCLRWorkerLease cas, WarpCoreCLRWorkerLease census, WarpCoreCLRWorkerLease publication, WarpCoreCLRWorkerLease stopped)
    {
        const uint controller = 119;
        WarpPortableSchedulerSchema schema = new(3, 1, 4096, 4, 100000, 0, 16, [new(0, 0, [])],
            [new(17, WarpPortableSchedulerLayout.GridScope, [0, 1, 2])]);
        uint[] arena = schema.CreateArena(619);
        Assert.AreEqual(0u, Interlocked.CompareExchange(ref arena[WarpPortableSchedulerLayout.ControllerOwner], controller, 0));
        if (string.Equals(operation, nameof(WarpPortableSchedulerServices.BeginDispatch), StringComparison.Ordinal)) { CompleteGenerationFixture(arena, controller); }
        WarpCoreCLRCommandAdmission[] owners = GenerationAdmissions(arena, service.Layout, cas, census, controller, 1, 0);
        foreach (WarpCoreCLRCommandAdmission owner in owners)
        { await WarpCoreCLRStoppedCommands.RegisterPausedAdmissionAsync(owner).ConfigureAwait(false); }
        int quantum = largeQuantum ? 4096 : service.Layout.MaximumBlockCost;
        WarpCoreCLRGenerationTransition receipt = await ExecuteGenerationAsync(service, owners[0], quantum, controller, cas, publication, stopped).ConfigureAwait(false);
        Assert.AreEqual(string.Equals(operation, nameof(WarpPortableSchedulerServices.BeginDispatch), StringComparison.Ordinal) ? 2u : 1u, receipt.ToDispatch);
        Assert.AreEqual(string.Equals(operation, nameof(WarpPortableSchedulerServices.RequestCollection), StringComparison.Ordinal) ? 1u : 0u, receipt.ToCollection);
        WarpCoreCLRCommandAdmission[] successors = GenerationAdmissions(arena, service.Layout, cas, census, controller, receipt.ToDispatch, receipt.ToCollection);
        await ValidateAndApplyGenerationAsync(receipt, owners, successors).ConfigureAwait(false);
        await AssertControllerHeldAndReleaseAsync(receipt, service, successors[0], controller).ConfigureAwait(false);
        if (string.Equals(operation, nameof(WarpPortableSchedulerServices.RequestCollection), StringComparison.Ordinal))
        {
            Assert.AreEqual(0u, Interlocked.CompareExchange(ref arena[WarpPortableSchedulerLayout.ControllerOwner], controller, 0));
            receipt = await ExecuteGenerationAsync(service, successors[0], quantum, controller, cas, publication, stopped).ConfigureAwait(false);
            Assert.AreEqual(receipt.FromDispatch, receipt.ToDispatch); Assert.AreEqual(receipt.FromCollection, receipt.ToCollection);
            owners = successors;
            successors = GenerationAdmissions(arena, service.Layout, cas, census, controller, receipt.ToDispatch, receipt.ToCollection);
            await ValidateAndApplyGenerationAsync(receipt, owners, successors).ConfigureAwait(false);
            await AssertControllerHeldAndReleaseAsync(receipt, service, successors[0], controller).ConfigureAwait(false);
        }
        else
        {
            uint barrier = arena[WarpPortableSchedulerLayout.BarrierStart];
            Assert.AreEqual(2u, arena[barrier + WarpPortableSchedulerLayout.BarrierGeneration]);
            Assert.AreEqual(WarpPortableSchedulerLayout.Active, arena[WarpPortableSchedulerLayout.ContextState]);
        }
        foreach (WarpCoreCLRCommandAdmission successor in successors)
        { await WarpCoreCLRStoppedCommands.RetirePausedAdmissionAsync(successor).ConfigureAwait(false); }
    }

    private static WarpCoreCLRCommandAdmission[] GenerationAdmissions(uint[] arena, WarpLogicalMachineLayout layout,
        WarpCoreCLRWorkerLease cas, WarpCoreCLRWorkerLease census, uint controller, uint dispatch, uint collection)
    {
        WarpCoreCLRPreparedCleanup[] cleanup = [new(census, WarpCoreCLRCleanupPurpose.StoppedFault, 0,
            WordBanks(0, controller, dispatch, collection, uint.MaxValue, 0, 0, 0, 0, 0), []),
            new(cas, WarpCoreCLRCleanupPurpose.ReleaseCapturedController, controller, WordBanks(16, controller, 0), [])];
        string schema = Convert.ToHexString(SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(
            arena.AsSpan((int)WarpPortableSchedulerLayout.SchemaHash, 8))));
        string ir = WarpIrHash.Compute(layout.Kernel);
        return Enumerable.Range(0, 3).Select(_ => new WarpCoreCLRCommandAdmission(new object(), layout.CreateInitialState(32, 100_000_000),
            arena, schema, ir, ir, dispatch, collection, controller, cleanup)).ToArray();
    }

    private static async Task<WarpCoreCLRGenerationTransition> ExecuteGenerationAsync(WarpCoreCLRWorkerLease service,
        WarpCoreCLRCommandAdmission admission, int quantum, uint controller, WarpCoreCLRWorkerLease cas,
        WarpCoreCLRWorkerLease publication, WarpCoreCLRWorkerLease stopped)
    {
        uint[] state = service.Layout.CreateInitialState(32, 100_000_000);
        WarpCoreCLRControllerOperation operation = string.Equals(WarpIrHash.Compute(service.Layout.Kernel), WarpCoreCLRRecoveryCatalog.RequestCollection, StringComparison.Ordinal) ?
            WarpCoreCLRControllerOperation.RequestCollection : WarpCoreCLRControllerOperation.BeginDispatch;
        WarpCoreCLRPreparedCleanup[] cleanup = ControllerBindings(admission.Arena, 0, controller, operation, cas, publication, stopped);
        var preparation = new WarpCoreCLRControllerPreparation(operation, service, state, admission.Arena, 32,
            admission.SchemaHash, admission.PlanHash, 0, controller, cleanup);
        WarpCoreCLRControllerAdmission controllerAdmission = await WarpCoreCLRStoppedCommands.RegisterPreparedControllerAsync(preparation).ConfigureAwait(false);
        Assert.Throws<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.GetCommittedGenerationTransition(state));
        int calls = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && calls++ < 100000)
        {
            await service.ExecuteOwnedControllerQuantumAsync(controllerAdmission, quantum).ConfigureAwait(false);
            if (state[0] == WarpLogicalMachineLayout.Runnable)
            { Assert.Throws<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.GetCommittedGenerationTransition(state)); }
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]);
        return WarpCoreCLRStoppedCommands.GetCommittedGenerationTransition(state);
    }

    private static async Task AssertControllerHeldAndReleaseAsync(WarpCoreCLRGenerationTransition receipt, WarpCoreCLRWorkerLease service,
        WarpCoreCLRCommandAdmission successor, uint controller)
    {
        Assert.IsNotNull(receipt.Controller);
        uint[] original = (uint[])successor.State.Clone();
        await Assert.ThrowsAsync<WarpHostException>(() => service.ExecuteOwnedManagedQuantumAsync(WordBanks(0, controller), [], 0,
            successor.State, 32, service.Layout.MaximumBlockCost, successor.Arena, successor, CancellationToken.None)).ConfigureAwait(false);
        CollectionAssert.AreEqual(original, successor.State); Assert.IsFalse(service.IsFaulted);
        await WarpCoreCLRStoppedCommands.ReleaseOwnedControllerAsync(receipt.Controller).ConfigureAwait(false);
        Assert.AreEqual(0u, successor.Arena[WarpPortableSchedulerLayout.ControllerOwner]);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => WarpCoreCLRStoppedCommands.ReleaseOwnedControllerAsync(receipt.Controller)).ConfigureAwait(false);
    }

    private static async Task ValidateAndApplyGenerationAsync(WarpCoreCLRGenerationTransition receipt,
        WarpCoreCLRCommandAdmission[] predecessors, WarpCoreCLRCommandAdmission[] successors)
    {
        var forged = new WarpCoreCLRGenerationTransition(receipt.Ordinal, receipt.Arena, receipt.Operation, receipt.Scheduler,
            receipt.FromDispatch, receipt.ToDispatch, receipt.FromCollection, receipt.ToCollection, predecessors);
        await Assert.ThrowsAsync<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.ApplyCommittedGenerationTransitionAsync(
            forged, predecessors, successors)).ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.ApplyCommittedGenerationTransitionAsync(
            receipt, predecessors[..^1], successors[..^1])).ConfigureAwait(false);
        await WarpCoreCLRStoppedCommands.ApplyCommittedGenerationTransitionAsync(receipt, predecessors, successors).ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.ApplyCommittedGenerationTransitionAsync(
            receipt, predecessors, successors)).ConfigureAwait(false);
        foreach (WarpCoreCLRCommandAdmission predecessor in predecessors)
        { await Assert.ThrowsAsync<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.RegisterPausedAdmissionAsync(predecessor)).ConfigureAwait(false); }
    }

    private static void CompleteGenerationFixture(uint[] arena, uint controller)
    {
        for (uint worker = 0; worker < 3; worker++)
        {
            Assert.AreEqual(0u, WarpPortableSchedulerServices.TryAcquireWorker(arena, 0, controller, 0));
            uint selected = arena[WarpPortableSchedulerLayout.Result], generation = arena[WarpPortableSchedulerLayout.Result + 1];
            Assert.AreEqual(0u, WarpPortableSchedulerServices.CompleteWorker(arena, 0, controller, selected, generation));
        }
        Assert.AreEqual(WarpPortableSchedulerLayout.CompletedContext, arena[WarpPortableSchedulerLayout.ContextState]);
    }
}
