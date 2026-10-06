using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    [DataRow(WarpCoreCLRControllerOperation.RequestCollection, 33, 3, false, false)]
    [DataRow(WarpCoreCLRControllerOperation.RequestCollection, 33, 3, true, false)]
    [DataRow(WarpCoreCLRControllerOperation.RequestCollection, 131, 7, false, true)]
    [DataRow(WarpCoreCLRControllerOperation.RequestCollection, 131, 7, true, true)]
    [DataRow(WarpCoreCLRControllerOperation.BeginDispatch, 33, 3, false, false)]
    [DataRow(WarpCoreCLRControllerOperation.BeginDispatch, 33, 3, true, false)]
    [DataRow(WarpCoreCLRControllerOperation.BeginDispatch, 131, 7, false, true)]
    [DataRow(WarpCoreCLRControllerOperation.BeginDispatch, 131, 7, true, true)]
    public async Task StoppedControllerAuthenticatesIndependentCensusAndDrainsPartialGeneration(WarpCoreCLRControllerOperation operation,
        int workers, int residents, bool largeQuantum, bool afterGeneration)
    {
        ControllerModules modules = await ControllerModules.CreateAsync(operation, TestContext).ConfigureAwait(false);
        await using var moduleOwner = modules.ConfigureAwait(false);
        try
        {
            ControllerFixture fixture = await CreateControllerFixtureAsync(modules, operation, workers, residents, largeQuantum).ConfigureAwait(false);
            await AssertControllerSourceExcludedAsync(modules, fixture).ConfigureAwait(false);
            if (afterGeneration) { modules.FailReturnedGeneration(); } else { modules.FailNextServiceResponse(); }
            WarpHostException failure = await FailControllerAsync(modules, fixture).ConfigureAwait(false);
            Assert.IsNotNull(modules.FailedReturnedArena); Assert.IsNotNull(modules.FailedReturnedState);
            if (afterGeneration)
            {
                uint offset = operation == WarpCoreCLRControllerOperation.RequestCollection ? WarpPortableSchedulerLayout.GCEpoch : WarpPortableSchedulerLayout.DispatchGeneration;
                Assert.AreEqual(operation == WarpCoreCLRControllerOperation.RequestCollection ? 1u : 2u, modules.FailedReturnedArena[fixture.Scheduler + offset]);
                Assert.AreEqual(operation == WarpCoreCLRControllerOperation.RequestCollection ? 0u : 1u, fixture.Arena[fixture.Scheduler + offset]);
            }
            await RecoverControllerAsync(modules, fixture, failure).ConfigureAwait(false);
            TestContext.WriteLine($"Stopped independent controller census: workers={workers}, residents={residents}, count={workers + 1}, afterReturnedGeneration={afterGeneration}, quantum={fixture.Quantum}.");
        }
        catch (Exception failure)
        {
            modules.RecordBodyFailure(failure);
            throw;
        }
    }

    private static async Task<WarpHostException> FailControllerAsync(ControllerModules modules, ControllerFixture fixture)
    {
        int operations = 0;
        while (++operations < 100000)
        {
            uint[] arena = (uint[])fixture.Arena.Clone(), state = (uint[])fixture.Controller.Preparation.State.Clone();
            try { await modules.Service.ExecuteOwnedControllerQuantumAsync(fixture.Controller, fixture.Quantum).ConfigureAwait(false); }
            catch (WarpHostException failure)
            {
                modules.RecordReturnedServiceFailure(failure);
                CollectionAssert.AreEqual(arena, fixture.Arena); CollectionAssert.AreEqual(state, fixture.Controller.Preparation.State);
                AssertTerminated(modules.Service.ProcessId); return failure;
            }
            Assert.AreEqual(WarpLogicalMachineLayout.Runnable, fixture.Controller.Preparation.State[0]);
        }
        throw new InvalidOperationException("The actual child did not reach the armed controller response boundary.");
    }

    private static async Task RecoverControllerAsync(ControllerModules modules, ControllerFixture fixture, WarpHostException failure)
    {
        WarpCoreCLRStoppedCommands.Command command = WarpCoreCLRStoppedCommands.FromFailure(failure)!;
        Assert.IsNotNull(command); Assert.AreSame(fixture.Controller, command.Controller);
        Assert.Throws<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.Mint(command, new object()));
        WarpCoreCLRQuarantineRecovery recovery = WarpCoreCLRStoppedCommands.Mint(command, fixture.Controller);
        Assert.AreEqual(fixture.Owners.Length + 1, recovery.Census.Count);
        Assert.AreSame(fixture.Controller, recovery.Census.Controller);
        Assert.IsTrue(recovery.Census.Admissions.All(owner => fixture.Owners.Contains(owner)));
        Assert.HasCount(2, recovery.Cleanup);
        foreach (WarpCoreCLRPreparedCleanup binding in recovery.Cleanup)
        { await RunRecoveryServiceAsync(binding, fixture.Arena, recovery, fixture.Quantum).ConfigureAwait(false); }
        await recovery.AcknowledgeGeneratedAbortAsync(fixture.Controller, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(WarpPortableSchedulerLayout.DisposedContext, fixture.Arena[fixture.Scheduler + WarpPortableSchedulerLayout.ContextState]);
        Assert.AreEqual(1u, fixture.Arena[fixture.Scheduler + WarpPortableSchedulerLayout.OutputQuarantined]);
        Assert.AreEqual(0u, fixture.Arena[fixture.Scheduler + WarpPortableSchedulerLayout.ControllerOwner]);
        Assert.AreEqual(0u, fixture.Arena[fixture.Scheduler + WarpPortableSchedulerLayout.RunningCount]);
        if (fixture.Scheduler != 0)
        { Assert.AreEqual(0u, fixture.Arena[WarpPortableHeapLayout.LeaseState]); Assert.AreEqual(0u, fixture.Arena[WarpPortableHeapLayout.PendingResult]); }
        foreach (WarpCoreCLRCommandAdmission owner in fixture.Owners)
        { await Assert.ThrowsAsync<WarpHostException>(() => WarpCoreCLRWordTransaction.AcquireAsync(owner.State, [], CancellationToken.None)).ConfigureAwait(false); }
        await Assert.ThrowsAsync<WarpHostException>(() => WarpCoreCLRWordTransaction.AcquireAsync(new uint[1], fixture.Arena, CancellationToken.None)).ConfigureAwait(false);
        Assert.Throws<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.Mint(command, fixture.Controller));
        Assert.IsFalse(modules.Emergency.IsFaulted); Assert.IsFalse(modules.Stopped.IsFaulted);
    }
}
