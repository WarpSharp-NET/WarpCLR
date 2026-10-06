using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    public async Task ControllerAdmissionRejectsUnboundIdentityAndMutationBeforeAnyChildEffect()
    {
        ControllerModules modules = await ControllerModules.CreateAsync(WarpCoreCLRControllerOperation.RequestCollection, TestContext).ConfigureAwait(false);
        await using var moduleOwner = modules.ConfigureAwait(false);
        uint[] arena = SeedRecoveryArena(33, 3, true, RegistryController);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        WarpCoreCLRCommandAdmission[] owners = await RegisterRecoveryCensusAsync(33, arena, scheduler, modules.Source.Layout,
            RecoveryBindings(arena, scheduler, RegistryController, true, modules.Emergency, modules.Census), RegistryController).ConfigureAwait(false);
        WarpCoreCLRPreparedCleanup[] cleanup = ControllerBindings(arena, scheduler, RegistryController,
            WarpCoreCLRControllerOperation.RequestCollection, modules.Emergency, modules.Publication, modules.Stopped);
        uint[] original = (uint[])arena.Clone();
        string wrongHash = new('0', 64);
        await RejectPreparationAsync<InvalidOperationException>(modules, owners[0], scheduler, wrongHash, owners[0].PlanHash, RegistryController, cleanup).ConfigureAwait(false);
        await RejectPreparationAsync<InvalidOperationException>(modules, owners[0], scheduler, owners[0].SchemaHash, wrongHash, RegistryController, cleanup).ConfigureAwait(false);
        await RejectPreparationAsync<InvalidDataException>(modules, owners[0], scheduler, owners[0].SchemaHash, owners[0].PlanHash, RegistryController + 1, cleanup).ConfigureAwait(false);
        await RejectPreparationAsync<InvalidOperationException>(modules, owners[0], scheduler, owners[0].SchemaHash, owners[0].PlanHash, RegistryController, cleanup[..^1]).ConfigureAwait(false);
        WarpCoreCLRPreparedCleanup[] reused = (WarpCoreCLRPreparedCleanup[])cleanup.Clone();
        reused[2] = new(modules.Emergency, WarpCoreCLRCleanupPurpose.PublishControllerRelease, RegistryController,
            WordBanks(scheduler + WarpPortableSchedulerLayout.ControllerOwner, RegistryController, 0), []);
        await RejectPreparationAsync<InvalidOperationException>(modules, owners[0], scheduler, owners[0].SchemaHash, owners[0].PlanHash, RegistryController, reused).ConfigureAwait(false);
        uint[] state = modules.Service.Layout.CreateInitialState(32, 100_000_000); state[^1] = 17;
        var forged = new WarpCoreCLRControllerPreparation(WarpCoreCLRControllerOperation.RequestCollection, modules.Service, state,
            arena, 32, owners[0].SchemaHash, owners[0].PlanHash, scheduler, RegistryController, cleanup);
        await Assert.ThrowsAsync<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.RegisterPreparedControllerAsync(forged)).ConfigureAwait(false);
        CollectionAssert.AreEqual(original, arena); state[^1] = 0;
        WarpCoreCLRControllerAdmission admission = await WarpCoreCLRStoppedCommands.RegisterPreparedControllerAsync(forged).ConfigureAwait(false);
        owners[0].State[^1] = 29;
        await Assert.ThrowsAsync<WarpHostException>(() => modules.Service.ExecuteOwnedControllerQuantumAsync(admission, 4096)).ConfigureAwait(false);
        owners[0].State[^1] = 0; state[^1] = 37;
        await Assert.ThrowsAsync<WarpHostException>(() => modules.Service.ExecuteOwnedControllerQuantumAsync(admission, 4096)).ConfigureAwait(false);
        state[^1] = 0; arena[scheduler + WarpPortableSchedulerLayout.ControllerOwner] = 173;
        await Assert.ThrowsAsync<WarpHostException>(() => modules.Service.ExecuteOwnedControllerQuantumAsync(admission, 4096)).ConfigureAwait(false);
        arena[scheduler + WarpPortableSchedulerLayout.ControllerOwner] = RegistryController;
        Assert.IsFalse(modules.Service.IsFaulted); CollectionAssert.AreEqual(original, arena);
        var fixture = new ControllerFixture(arena, scheduler, owners, admission, 4096, []);
        modules.FailNextServiceResponse();
        await RecoverControllerAsync(modules, fixture, await FailControllerAsync(modules, fixture).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static Task<T> RejectPreparationAsync<T>(ControllerModules modules, WarpCoreCLRCommandAdmission owner, uint scheduler,
        string schema, string plan, uint controller, WarpCoreCLRPreparedCleanup[] cleanup) where T : Exception => Assert.ThrowsAsync<T>(async () =>
    {
        var preparation = new WarpCoreCLRControllerPreparation(WarpCoreCLRControllerOperation.RequestCollection, modules.Service,
            modules.Service.Layout.CreateInitialState(32, 100_000_000), owner.Arena, 32, schema, plan, scheduler, controller, cleanup);
        await WarpCoreCLRStoppedCommands.RegisterPreparedControllerAsync(preparation).ConfigureAwait(false);
    });
}
