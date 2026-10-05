namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void ActualValueConstructorAndHandledCalleeKeepExactReferenceResults() =>
        WarpPortableClosedFrameExceptionCases.NormalActualConstructorAndHandledCalleeRetainBothReferenceFields();

    [TestMethod]
    public void ActualValueConstructorThrowUsesOrderedCatchFinallyAndFilters() =>
        WarpPortableClosedFrameExceptionCases.ActualFailingConstructorUsesOrderedCatchFinallyAndFilterExecution();

    [TestMethod]
    public void ComposedValueConstructorDomainStillDeniesAliasOwnersAndImplicitFaults() =>
        WarpPortableClosedFrameExceptionCases.AliasTailAddressesAndMissingImplicitFactoriesRemainDenied();

    [TestMethod]
    public void ActualConstructorOwnersSurviveRealFilterSearchAndRetireAfterUnwind() =>
        WarpPortableClosedFrameExceptionCases.ActualConstructorOwnersSurviveRealFilterSearchAndRetireAfterUnwind();

    [TestMethod]
    public void RealHandledConstructorCalleeKeepsItsSuspendedOriginalNewobjOwners() =>
        WarpPortableClosedFrameExceptionCases.ARealHandledConstructorCalleeKeepsItsSuspendedOriginalNewobjOwners();

    [TestMethod]
    public void ActualEscapingConstructorPublishesTheOriginalTypedExceptionReference() =>
        WarpPortableClosedFrameExceptionCases.AnActualEscapingConstructorPublishesTheOriginalTypedExceptionReference();

    [TestMethod]
    public void ASecondLegitimateFrameViewCannotReplaceTheCompiledRootElementType() =>
        WarpPortableClosedFrameExceptionCases.ASecondLegitimateFrameViewCannotReplaceTheCompiledRootElementType();

    [TestMethod]
    public void CompositeIdentityAttachesTheActualPlanAndRejectsCloneOrHashReplacement() =>
        WarpPortableClosedFrameExceptionCases.CompositeIdentityAttachesTheActualPlanAndRejectsCloneOrHashReplacement();

    [TestMethod]
    public void CompletionCannotAttachASecondPlanOrAPlanFromAnotherExactSource() =>
        WarpPortableClosedFrameExceptionCases.CompletionCannotAttachASecondPlanOrAPlanFromAnotherExactSource();
}
