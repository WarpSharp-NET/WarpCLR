namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void CapturedOriginalCctorRunsInsideRootFrameAndMemoizesPerContext() => WarpPortableSourceInitializerExecutionCases.CapturedOriginalCctorRunsInsideRootFrameAndMemoizesPerContext();
    [TestMethod]
    public void ActualCompiledCctorCycleSeesReentrantZeroThenCompletesExactStatics() => WarpPortableSourceInitializerExecutionCases.ActualCompiledCctorCycleSeesReentrantZeroThenCompletesExactStatics();
    [TestMethod]
    public void OriginalBeforeFieldInitCilTriggersAtFieldRatherThanConstantMethod() => WarpPortableSourceInitializerExecutionCases.OriginalBeforeFieldInitCilTriggersAtFieldRatherThanConstantMethod();
    [TestMethod]
    public void AbandonedCctorCannotReenterAfterResetOrPublishDefaultSourceOutput() => WarpPortableSourceInitializerExecutionCases.AbandonedCctorCannotReenterAfterResetOrPublishDefaultSourceOutput();
    [TestMethod]
    public void OriginalCctorCannotRunWithHeldHeapControllerOrAcquireFactoryAuthority() => WarpPortableSourceInitializerExecutionCases.OriginalCctorCannotRunWithHeldHeapControllerOrAcquireFactoryAuthority();
    [TestMethod]
    public void ActualCompiledInitializerStaticsPreservePackedSignedWideAndRawFloatingBits() => WarpPortableSourceInitializerExecutionCases.ActualCompiledInitializerStaticsPreservePackedSignedWideAndRawFloatingBits();
}
