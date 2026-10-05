using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    public void ActualSourceCatchRetirementValidatesTheStillActiveOuterFilterAliasCapture()
    {
        var driver = new WarpPortableExceptionSourceDriver(nameof(WarpPortableExceptionFixtureSources.CalledFilter), 31,
            replacementType: typeof(StackOverflowException), flag: 0);
        driver.ExecuteUntil(() => CapturedOuterAlias(driver) != 0);
        uint record = driver.ActiveRecord; uint captured = CapturedOuterAlias(driver);
        Assert.AreNotEqual(0u, captured);
        uint[] owners = driver.Arena.AsSpan((int)(record + WarpPortableExceptionLayout.ExceptionReference), 3).ToArray();
        driver.Arena[captured + WarpPortableExceptionLayout.FrameAliasOwnerPhysical]++;
        uint[] before = driver.Arena.AsSpan((int)record, (int)WarpPortableExceptionLayout.RecordWords).ToArray();
        driver.Execute();
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, driver.State[WarpLogicalMachineLayout.StatusOffset], driver.Diagnostic());
        Assert.AreEqual(3u, driver.State[WarpLogicalMachineLayout.FaultKindOffset]);
        Assert.AreEqual(0u, driver.Arena[record + WarpPortableExceptionLayout.TemporaryOwnersRetired]);
        CollectionAssert.AreEqual(before, driver.Arena.AsSpan((int)record, (int)WarpPortableExceptionLayout.RecordWords).ToArray());
        CollectionAssert.AreEqual(owners, driver.Arena.AsSpan((int)(record + WarpPortableExceptionLayout.ExceptionReference), 3).ToArray());
    }

    private static uint CapturedOuterAlias(WarpPortableExceptionSourceDriver driver)
    {
        uint index = driver.Arena[driver.Worker + WarpPortableExceptionLayout.ActiveRecord];
        if (index == 0 || driver.Arena[driver.ActiveRecord + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.CatchPending ||
            driver.Arena[driver.ActiveRecord + WarpPortableExceptionLayout.Action] != WarpPortableExceptionLayout.EnterCatch) { return 0; }
        uint count = driver.Arena[driver.ActiveRecord + WarpPortableExceptionLayout.FrameCount];
        for (uint ordinal = 0; ordinal < count; ordinal++)
        {
            uint captured = driver.Descriptor + driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.FrameStart] +
                ((index - 1) * driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.MaximumFrames] + ordinal) * WarpPortableExceptionLayout.FrameWords;
            if (driver.Arena[captured + WarpPortableExceptionLayout.FrameAliasOwnerPhysical] != 0) { return captured; }
        }
        return 0;
    }
}
