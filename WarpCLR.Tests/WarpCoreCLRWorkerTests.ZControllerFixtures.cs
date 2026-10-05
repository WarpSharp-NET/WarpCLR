using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    private const uint RegistryController = 119;

    private sealed record ControllerFixture(uint[] Arena, uint Scheduler, WarpCoreCLRCommandAdmission[] Owners,
        WarpCoreCLRControllerAdmission Controller, int Quantum, WarpCoreCLRPreparedCleanup[] SuccessorCleanup);

    private static async Task<ControllerFixture> CreateControllerFixtureAsync(ControllerModules modules,
        WarpCoreCLRControllerOperation operation, int workers, int residents, bool largeQuantum)
    {
        uint[] arena = operation == WarpCoreCLRControllerOperation.RequestCollection ?
            SeedRecoveryArena((uint)workers, (uint)residents, true, RegistryController) : CompletedControllerArena(workers, residents);
        uint scheduler = arena[0] == WarpPortableHeapLayout.Magic ? arena[WarpPortableSchedulerLayout.HeapDescriptor] : 0;
        WarpCoreCLRPreparedCleanup[] ownerCleanup = RecoveryBindings(arena, scheduler, RegistryController, true, modules.Emergency, modules.Census);
        WarpCoreCLRCommandAdmission[] owners = await RegisterRecoveryCensusAsync(workers, arena, scheduler,
            modules.Source.Layout, ownerCleanup, RegistryController).ConfigureAwait(false);
        uint[] helper = modules.Service.Layout.CreateInitialState(32, 100_000_000);
        WarpCoreCLRPreparedCleanup[] cleanup = ControllerBindings(arena, scheduler, RegistryController, operation,
            modules.Emergency, modules.Publication, modules.Stopped);
        var preparation = new WarpCoreCLRControllerPreparation(operation, modules.Service, helper, arena, 32,
            owners[0].SchemaHash, owners[0].PlanHash, scheduler, RegistryController, cleanup);
        WarpCoreCLRControllerAdmission controller = await WarpCoreCLRStoppedCommands.RegisterPreparedControllerAsync(preparation).ConfigureAwait(false);
        uint dispatch = operation == WarpCoreCLRControllerOperation.BeginDispatch ? 2u : 1u;
        uint epoch = operation == WarpCoreCLRControllerOperation.RequestCollection ? 1u : 0u;
        WarpCoreCLRPreparedCleanup[] successorCleanup = [new(modules.Census, WarpCoreCLRCleanupPurpose.StoppedFault, 0,
            WordBanks(scheduler, RegistryController, dispatch, epoch, 0, 1, 0, 17, 19, 1), []),
            new(modules.Emergency, WarpCoreCLRCleanupPurpose.ReleaseCapturedController, RegistryController,
                WordBanks(scheduler + WarpPortableSchedulerLayout.ControllerOwner, RegistryController, 0), [])];
        return new(arena, scheduler, owners, controller, largeQuantum ? 4096 : modules.Service.Layout.MaximumBlockCost, successorCleanup);
    }

    private static uint[] CompletedControllerArena(int workers, int residents)
    {
        uint[] members = Enumerable.Range(0, workers).Select(worker => (uint)worker).ToArray();
        WarpPortableSchedulerSchema schema = new((uint)workers, (uint)residents, 4096, 4, 100000, 0, 64,
            [new(0, 0, [])], [new(17, WarpPortableSchedulerLayout.GridScope, members)]);
        uint[] arena = schema.CreateArena(716);
        Assert.AreEqual(0u, Interlocked.CompareExchange(ref arena[WarpPortableSchedulerLayout.ControllerOwner], RegistryController, 0));
        for (int worker = 0; worker < workers; worker++)
        {
            Assert.AreEqual(0u, WarpPortableSchedulerServices.TryAcquireWorker(arena, 0, RegistryController, 0));
            Assert.AreEqual(0u, WarpPortableSchedulerServices.CompleteWorker(arena, 0, RegistryController,
                arena[WarpPortableSchedulerLayout.Result], arena[WarpPortableSchedulerLayout.Result + 1]));
        }
        arena.AsSpan((int)arena[WarpPortableSchedulerLayout.LogicalStateStart]).Fill(0xA195F03Du);
        Assert.AreEqual(WarpPortableSchedulerLayout.CompletedContext, arena[WarpPortableSchedulerLayout.ContextState]);
        return arena;
    }

    private static async Task AssertControllerSourceExcludedAsync(ControllerModules modules, ControllerFixture fixture)
    {
        uint[] before = (uint[])fixture.Owners[0].State.Clone();
        await Assert.ThrowsAsync<WarpHostException>(() => modules.Source.ExecuteOwnedManagedQuantumAsync([[7]], [], 0,
            fixture.Owners[0].State, 4, 4096, fixture.Arena, fixture.Owners[0], CancellationToken.None)).ConfigureAwait(false);
        WarpCoreCLRInputBinding binding = await modules.Source.BindInputsAsync([[7]], [], CancellationToken.None).ConfigureAwait(false);
        await using var bindingOwner = binding.ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => modules.Source.ExecuteBatchAsync(binding, 0,
            [fixture.Owners[0].State], 4, 4096, CancellationToken.None)).ConfigureAwait(false);
        CollectionAssert.AreEqual(before, fixture.Owners[0].State); Assert.IsFalse(modules.Source.IsFaulted);
    }
}
