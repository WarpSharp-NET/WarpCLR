using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    [DataRow(WarpCoreCLRControllerOperation.RequestCollection, false)]
    [DataRow(WarpCoreCLRControllerOperation.RequestCollection, true)]
    [DataRow(WarpCoreCLRControllerOperation.BeginDispatch, false)]
    [DataRow(WarpCoreCLRControllerOperation.BeginDispatch, true)]
    public async Task FailedNormalPublicationUsesDistinctAlreadyPreparedEmergencyCas(WarpCoreCLRControllerOperation operation, bool largeQuantum)
    {
        ControllerModules modules = await ControllerModules.CreateAsync(operation).ConfigureAwait(false);
        await using var moduleOwner = modules.ConfigureAwait(false);
        ControllerFixture fixture = await CreateControllerFixtureAsync(modules, operation, 33, 3, largeQuantum).ConfigureAwait(false);
        WarpCoreCLRGenerationTransition receipt = await CompleteControllerAsync(modules, fixture).ConfigureAwait(false);
        WarpCoreCLRCommandAdmission[] successors = fixture.Owners.Select(owner => new WarpCoreCLRCommandAdmission(owner.ExactTicket,
            modules.Source.Layout.CreateInitialState(4, long.MaxValue), fixture.Arena, owner.SchemaHash, owner.PlanHash, owner.SourceIrHash,
            receipt.ToDispatch, receipt.ToCollection, RegistryController, fixture.SuccessorCleanup)).ToArray();
        await WarpCoreCLRStoppedCommands.ApplyCommittedGenerationTransitionAsync(receipt, fixture.Owners, successors).ConfigureAwait(false);
        fixture = fixture with { Owners = successors };
        await AssertControllerSourceExcludedAsync(modules, fixture).ConfigureAwait(false);
        uint[] arena = (uint[])fixture.Arena.Clone();
        modules.FailNextReleaseResponse();
        WarpHostException failure = await Assert.ThrowsAsync<WarpHostException>(() =>
            WarpCoreCLRStoppedCommands.ReleaseOwnedControllerAsync(fixture.Controller)).ConfigureAwait(false);
        CollectionAssert.AreEqual(arena, fixture.Arena);
        Assert.AreEqual(RegistryController, fixture.Arena[fixture.Scheduler + WarpPortableSchedulerLayout.ControllerOwner]);
        Assert.IsNotNull(modules.FailedReturnedArena);
        Assert.AreEqual(0u, modules.FailedReturnedArena[fixture.Scheduler + WarpPortableSchedulerLayout.ControllerOwner]);
        Assert.IsTrue(modules.Publication.IsFaulted); Assert.IsFalse(modules.Emergency.IsFaulted); Assert.IsFalse(modules.Service.IsFaulted);
        Assert.AreNotEqual(modules.Publication.ProcessId, modules.Emergency.ProcessId);
        Assert.AreNotEqual(modules.Publication.CompiledModule, modules.Emergency.CompiledModule);
        AssertTerminated(modules.Publication.ProcessId);
        await RecoverControllerAsync(modules, fixture, failure).ConfigureAwait(false);
        Assert.IsTrue(fixture.Controller.Receipt!.Applied); Assert.IsFalse(fixture.Controller.ReleaseCompleted);
        TestContext.WriteLine($"Failed normal CAS {modules.Publication.ProcessId}/{modules.Publication.CompiledModule} was not retried; prepared emergency {modules.Emergency.ProcessId}/{modules.Emergency.CompiledModule} confirmed captured-owner release.");
    }

    [TestMethod]
    public async Task CommittedPartialWorkerResetCanOnlyDrainThroughBoundStoppedController()
    {
        ControllerModules modules = await ControllerModules.CreateAsync(WarpCoreCLRControllerOperation.BeginDispatch).ConfigureAwait(false);
        await using var moduleOwner = modules.ConfigureAwait(false);
        ControllerFixture fixture = await CreateControllerFixtureAsync(modules, WarpCoreCLRControllerOperation.BeginDispatch, 33, 3, false).ConfigureAwait(false);
        uint start = fixture.Arena[WarpPortableSchedulerLayout.LogicalStateStart];
        int operations = 0;
        while (fixture.Arena[start] != 0 && ++operations < 10000)
        { await modules.Service.ExecuteOwnedControllerQuantumAsync(fixture.Controller, fixture.Quantum).ConfigureAwait(false); }
        Assert.AreEqual(0u, fixture.Arena[start]); Assert.AreEqual(0xA195F03Du, fixture.Arena[start + 63]);
        Assert.AreEqual(1u, fixture.Arena[WarpPortableSchedulerLayout.DispatchGeneration]);
        Assert.AreEqual(1u, fixture.Controller.Checkpoint.Dispatch);
        Assert.AreNotEqual(0ul, fixture.Controller.Checkpoint.Ordinal);
        Assert.Throws<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.GetCommittedGenerationTransition(fixture.Controller.Preparation.State));
        modules.FailNextServiceResponse();
        WarpHostException failure = await FailControllerAsync(modules, fixture).ConfigureAwait(false);
        await RecoverControllerAsync(modules, fixture, failure).ConfigureAwait(false);
    }

    private static async Task<WarpCoreCLRGenerationTransition> CompleteControllerAsync(ControllerModules modules, ControllerFixture fixture)
    {
        int operations = 0;
        while (fixture.Controller.Preparation.State[0] == WarpLogicalMachineLayout.Runnable && ++operations < 100000)
        { await modules.Service.ExecuteOwnedControllerQuantumAsync(fixture.Controller, fixture.Quantum).ConfigureAwait(false); }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, fixture.Controller.Preparation.State[0]);
        return WarpCoreCLRStoppedCommands.GetCommittedGenerationTransition(fixture.Controller.Preparation.State);
    }
}
