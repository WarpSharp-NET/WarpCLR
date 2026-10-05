using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    public void CompiledConstructorProtocolRetainsOwnersThroughFilterDecisionAndFinallyThenClearsTheFailedOperation()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            foreach (bool accept in new[] { false, true })
            {
                Assert.AreEqual(accept ? 2 : 3, WarpPortableExceptionConstructorSources.FailedClass(new InvalidOperationException("constructor-reference"), accept));
                var driver = ConstructorDriver(nameof(WarpPortableExceptionConstructorSources.FailedClass), quantum);
                AssertConstructorOwners(driver, driver.TemporaryOwner);
                RaiseConstructor(driver);
                uint record = driver.Active;
                Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
                Assert.AreEqual(WarpPortableExceptionLayout.RunFilter, driver.Arena[record + WarpPortableExceptionLayout.Action]);
                AssertConstructorOwners(driver, driver.TemporaryOwner);
                // The generated phase protocol consumes this explicit decision.
                // General newobj/filter frontend admission still needs factories.
                Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.CompleteFilter), driver.Raise, accept ? 1u : 0u));
                if (!accept) { Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise)); }
                AssertConstructorOwners(driver, driver.TemporaryOwner);
                FinishConstructorFinally(driver, record);
                AssertConstructorOwners(driver, [0, 0, 0]);
                Assert.AreEqual(1u, driver.Arena[record + WarpPortableExceptionLayout.TemporaryOwnersRetired]);
                Assert.AreEqual(1u, driver.Arena[record + WarpPortableExceptionLayout.TemporaryRetirementBoundary]);
            }
        }
    }

    [TestMethod]
    public void AHandledConstructorCalleeExceptionPreservesTheLiveCallerNewobjTemporary()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            Assert.AreEqual(4, WarpPortableExceptionConstructorSources.ContinueClass(new InvalidOperationException("constructor-callee-reference")));
            var driver = ConstructorDriver(nameof(WarpPortableExceptionConstructorSources.ContinueClass), quantum);
            RaiseConstructor(driver);
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
            Assert.AreEqual(2u, driver.Arena[driver.Active + WarpPortableExceptionLayout.SelectedFrame]);
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
            uint record = driver.Active;
            driver.Apply();
            AssertConstructorOwners(driver, driver.TemporaryOwner);
            Assert.AreEqual(WarpPortableExceptionLayout.Caught, driver.Arena[record + WarpPortableExceptionLayout.Phase]);
            Assert.AreEqual(2u, driver.Arena[record + WarpPortableExceptionLayout.TemporaryRetirementBoundary]);
            Assert.AreEqual(driver.TemporaryOwner[2], driver.Arena[driver.Arena[WarpPortableHeapLayout.SlotStart] +
                (driver.TemporaryOwner[1] - 1) * WarpPortableHeapLayout.SlotWords + WarpPortableHeapLayout.SlotGeneration]);
        }
    }

    [TestMethod]
    public void ConstructorRetirementClearsEveryEmbeddedOwnerButOnlyTheAbandonedOriginalSite()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var value = ConstructorDriver(nameof(WarpPortableExceptionConstructorSources.FailedValue), quantum);
            Assert.HasCount(2, value.Program.Bindings[0].PrivateTemporaries[0].Owners);
            ReadyConstructorCatch(value); value.Apply();
            AssertConstructorOwners(value, [0, 0, 0]);
            CollectionAssert.AreEqual(value.Owner, value.State.AsSpan(WarpLogicalMachineLayout.HeaderWords + value.Program.Layout.PrivateOffset, 3).ToArray());
            var two = ConstructorDriver(nameof(WarpPortableExceptionConstructorSources.FailedTwoClass), quantum);
            Assert.HasCount(2, two.Program.Bindings[0].PrivateTemporaries);
            ReadyConstructorCatch(two); two.Apply();
            AssertConstructorOwners(two, [0, 0, 0], temporaryIndex: 0);
            AssertConstructorOwners(two, two.TemporaryOwner, temporaryIndex: 1);
        }
    }

    [TestMethod]
    public void AStaleCapturedSourceActivationAndMalformedLaterOwnerRejectBeforeAnyConstructorOwnerIsCleared()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var malformed = ConstructorDriver(nameof(WarpPortableExceptionConstructorSources.FailedValue), quantum);
            ReadyConstructorCatch(malformed);
            uint row = malformed.Descriptor + malformed.Arena[malformed.Descriptor + WarpPortableExceptionLayout.TemporaryOwnerStart] + WarpPortableExceptionLayout.TemporaryOwnerWords;
            malformed.Arena[row + WarpPortableExceptionLayout.TemporaryPrivateOffset] = uint.MaxValue;
            AssertRejectedConstructor(malformed);
            var stale = ConstructorDriver(nameof(WarpPortableExceptionConstructorSources.FailedValue), quantum);
            ReadyConstructorCatch(stale);
            uint index = stale.Arena[stale.Worker + WarpPortableExceptionLayout.ActiveRecord];
            stale.Arena[stale.Captured(index, 1) + WarpPortableExceptionLayout.FrameActivation]++;
            AssertRejectedConstructor(stale);
        }
    }

    [TestMethod]
    public void ConstructorTableAdmissionRejectsForgedOffsetsFieldPathsAndOriginalNewobjSites()
    {
        WarpPortableExceptionTestProgram program = WarpPortableExceptionTestBuilder.Create(typeof(WarpPortableExceptionConstructorSources)
            .GetMethod(nameof(WarpPortableExceptionConstructorSources.FailedValue))!);
        WarpPortableExceptionBodyBinding original = program.Bindings[0];
        WarpPortableWordPrivateTemporary temporary = original.PrivateTemporaries[0];
        WarpPortableWordTemporaryOwner owner = temporary.Owners[0];
        foreach (WarpPortableWordPrivateTemporary forged in new[]
        {
            temporary with { SourceOffset = temporary.SourceOffset + 1 },
            temporary with { Owners = temporary.Owners.SetItem(0, owner with { PrivateWordOffset = 0 }) },
            temporary with { Owners = temporary.Owners.SetItem(0, owner with { FieldPath = [] }) },
        })
        {
            WarpPortableExceptionBodyBinding body = original with { PrivateTemporaries = original.PrivateTemporaries.SetItem(0, forged) };
            WarpVerificationException fault = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableExceptionPlan.Create(
                program.Graph, program.Typed, program.Schema, program.Layout, program.Bindings.SetItem(0, body)));
            Assert.AreEqual("WRPCLR2500", fault.Code, StringComparer.Ordinal);
        }
    }

    private static WarpPortableExceptionTestDriver ConstructorDriver(string method, int quantum) =>
        new(method, quantum: quantum, source: typeof(WarpPortableExceptionConstructorSources));

    private static void RaiseConstructor(WarpPortableExceptionTestDriver driver)
    {
        uint index = driver.Arena[driver.Worker + WarpPortableExceptionLayout.PreparedRecord];
        uint captured = driver.Captured(index, driver.Arena[driver.Prepared + WarpPortableExceptionLayout.FrameCount]);
        Assert.AreEqual(0u, driver.RaiseOwner((int)driver.Arena[captured + WarpPortableExceptionLayout.FrameFunction]));
    }

    private static void ReadyConstructorCatch(WarpPortableExceptionTestDriver driver)
    {
        RaiseConstructor(driver);
        Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
        Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
        Assert.AreEqual(WarpPortableExceptionLayout.EnterCatch, driver.Arena[driver.Active + WarpPortableExceptionLayout.Action]);
    }

    private static void FinishConstructorFinally(WarpPortableExceptionTestDriver driver, uint record)
    {
        Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
        Assert.AreEqual(WarpPortableExceptionLayout.RunFinally, driver.Arena[record + WarpPortableExceptionLayout.Action]);
        uint clause = driver.Arena[record + WarpPortableExceptionLayout.CleanupClause];
        driver.Apply(); driver.CaptureInitial();
        AssertConstructorOwners(driver, driver.TemporaryOwner);
        Assert.AreEqual(0u, driver.Arena[record + WarpPortableExceptionLayout.TemporaryOwnersRetired]);
        Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.EndCleanup), driver.Raise, clause));
        Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
        Assert.AreEqual(WarpPortableExceptionLayout.EnterCatch, driver.Arena[record + WarpPortableExceptionLayout.Action]);
        driver.Apply();
    }

    private static void AssertRejectedConstructor(WarpPortableExceptionTestDriver driver)
    {
        uint record = driver.Active;
        uint[] original = driver.Arena.AsSpan((int)record, (int)WarpPortableExceptionLayout.RecordWords).ToArray();
        driver.Apply(terminal: true);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, driver.State[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(3u, driver.State[WarpLogicalMachineLayout.FaultKindOffset]);
        AssertConstructorOwners(driver, driver.TemporaryOwner);
        CollectionAssert.AreEqual(original, driver.Arena.AsSpan((int)record, (int)WarpPortableExceptionLayout.RecordWords).ToArray());
    }

    private static void AssertConstructorOwners(WarpPortableExceptionTestDriver driver, uint[] expected, int? temporaryIndex = null)
    {
        foreach (WarpPortableWordPrivateTemporary temporary in driver.Program.Bindings[0].PrivateTemporaries)
        {
            if (temporaryIndex is not null && temporary.Index != temporaryIndex) { continue; }
            foreach (WarpPortableWordTemporaryOwner owner in temporary.Owners)
            {
                int offset = WarpLogicalMachineLayout.HeaderWords + driver.Program.Layout.PrivateOffset + owner.PrivateWordOffset;
                CollectionAssert.AreEqual(expected, driver.State.AsSpan(offset, 3).ToArray());
            }
        }
    }
}
