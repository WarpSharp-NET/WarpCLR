using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture.")]
internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    public void EveryClosedEhServiceCompilesWithExplicitBanksAndRejectsAnInvalidArena()
    {
        MethodInfo[] services = typeof(WarpPortableExceptionServices).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.IsGreaterThan(15, services.Length);
        foreach (MethodInfo method in services)
        {
            uint[] arguments = new uint[method.GetParameters().Count(parameter => parameter.ParameterType != typeof(uint[]))];
            uint[] arena = [0, 0, 0]; uint[] before = (uint[])arena.Clone();
            Assert.AreEqual(WarpPortableExceptionLayout.InvalidDescriptor,
                WarpPortableExceptionTestService.Run(typeof(WarpPortableExceptionServices), method.Name, arena, arguments, 31), method.Name);
            CollectionAssert.AreEqual(before, arena, method.Name);
        }
    }

    [TestMethod]
    public void GeneratedCapturePreservesExactObjectAndSelectsTheFirstAssignableCatch()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.Catch), quantum: quantum, stopHandlers: false);
            Assert.AreEqual(1u, driver.Arena[driver.Prepared + WarpPortableExceptionLayout.FrameCount]);
            Assert.AreEqual(0u, driver.State[WarpLogicalMachineLayout.FaultKindOffset]);
            Assert.AreEqual(0u, driver.RaiseOwner()); uint record = driver.Active;
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
            Assert.AreEqual(WarpPortableExceptionLayout.Unwinding, driver.Arena[record + WarpPortableExceptionLayout.Phase]);
            uint clause = driver.Descriptor + driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.ClauseStart] +
                (driver.Arena[record + WarpPortableExceptionLayout.SelectedClause] - 1) * WarpPortableExceptionLayout.ClauseWords;
            Assert.AreEqual(driver.ExceptionType, driver.Arena[clause + WarpPortableExceptionLayout.CatchType]);
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
            Assert.AreEqual(WarpPortableExceptionLayout.EnterCatch, driver.Arena[record + WarpPortableExceptionLayout.Action]);
            driver.Apply();
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(101u, driver.State[driver.Program.Layout.GetResultWordOffset(0, 128)]);
            int destination = WarpLogicalMachineLayout.HeaderWords + driver.Program.Layout.PrivateOffset + 12;
            CollectionAssert.AreEqual(driver.Owner, driver.State.AsSpan(destination, 3).ToArray());
            Assert.AreEqual(WarpPortableExceptionLayout.Caught, driver.Arena[record + WarpPortableExceptionLayout.Phase]);
        }
    }

    [TestMethod]
    public void GeneratedEscapedTerminalPublishesTheValidatedTripleAndSchedulerQuarantinesIt()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.Escape), quantum: quantum);
            Assert.AreEqual(0u, driver.RaiseOwner());
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
            uint record = driver.Active; uint raise = driver.Raise;
            Assert.AreEqual(WarpPortableExceptionLayout.Escaped, driver.Arena[record + WarpPortableExceptionLayout.Action]);
            driver.Apply(terminal: true);
            Assert.AreEqual(WarpLogicalMachineLayout.Faulted, driver.State[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(WarpLogicalMachineLayout.ManagedExceptionFault, driver.State[WarpLogicalMachineLayout.FaultKindOffset]);
            CollectionAssert.AreEqual(driver.Owner, driver.State.AsSpan(WarpLogicalMachineLayout.EscapedExceptionContextOffset, 3).ToArray());
            Assert.AreEqual(1u, driver.Arena[record + WarpPortableExceptionLayout.TerminalCommitted]);
            Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, driver.Service(nameof(WarpPortableExceptionServices.PublishEscapedFault), raise));
            Assert.AreEqual(WarpPortableSchedulerLayout.Quarantined, driver.Arena[driver.Scheduler + WarpPortableSchedulerLayout.ContextState]);
            Assert.AreEqual(0u, driver.Arena[WarpPortableHeapLayout.LeaseState]);
            Assert.AreEqual(1u, driver.Arena[driver.Scheduler + WarpPortableSchedulerLayout.OutputQuarantined]);
        }
    }
}
