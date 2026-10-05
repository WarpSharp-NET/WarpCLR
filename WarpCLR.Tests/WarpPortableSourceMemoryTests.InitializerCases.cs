namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void OriginalCilTriggersBindExactTypesInitializersEffectsAndEntryInvocation() => WarpPortableSourceInitializerCases.OriginalCilTriggersBindExactTypesInitializersEffectsAndEntryInvocation();
    [TestMethod]
    public void CallsWithoutCapturedInitializersAcquireNoFictitiousTypeInitializeEffect() => WarpPortableSourceInitializerCases.CallsWithoutCapturedInitializersAcquireNoFictitiousTypeInitializeEffect();
    [TestMethod]
    public void BeforeFieldInitMethodAndOrdinaryClassInstanceCallsDoNotInventTriggers() => WarpPortableSourceInitializerCases.BeforeFieldInitMethodAndOrdinaryClassInstanceCallsDoNotInventTriggers();
    [TestMethod]
    public void ActualCoreClrStrictValueAndInterfaceInvocationTimingMatchesCapturedTriggers() => WarpPortableSourceInitializerCases.ActualCoreClrStrictValueAndInterfaceInvocationTimingMatchesCapturedTriggers();
    [TestMethod]
    public void ActualCoreClrInitializerFailureCachesWrapperAndOriginalInnerIdentity() => WarpPortableSourceInitializerCases.ActualCoreClrInitializerFailureCachesWrapperAndOriginalInnerIdentity();
    [TestMethod]
    public void UnboundSourceInitializerStillFailsClosedAtTheRealCompilerBoundary() => WarpPortableSourceInitializerCases.UnboundSourceInitializerStillFailsClosedAtTheRealCompilerBoundary();
    [TestMethod]
    public void GeneratedBeforeFieldInitMethodExecutesWithoutInventingACctorTrigger() => WarpPortableSourceInitializerCases.GeneratedBeforeFieldInitMethodExecutesWithoutInventingACctorTrigger();
    [TestMethod]
    public void ActualCoreClrTopLevelAndNestedInitializerNamesUseOwnMetadataNamespace() => WarpPortableSourceInitializerNameCases.ActualCoreClrTopLevelAndNestedInitializerNamesUseOwnMetadataNamespace();
    [TestMethod]
    public void ActualCoreClrClosedGenericInitializerNamesExcludeDisplayArguments() => WarpPortableSourceInitializerNameCases.ActualCoreClrClosedGenericInitializerNamesExcludeDisplayArguments();
    [TestMethod]
    public void ActualCoreClrSavedRawMetadataInitializerNamesMatchCapturedCodeUnits() => WarpPortableSourceInitializerNameCases.ActualCoreClrSavedRawMetadataInitializerNamesMatchCapturedCodeUnits();
    [TestMethod]
    public void ActualCoreClrInMemoryRawInitializerNamesMatchCapturedCodeUnits() => WarpPortableSourceInitializerNameCases.ActualCoreClrInMemoryRawInitializerNamesMatchCapturedCodeUnits();
    [TestMethod]
    public void InMemoryNestedInitializerNameRequiresProvedOwnMetadataNamespace() => WarpPortableSourceInitializerNameCases.InMemoryNestedInitializerNameRequiresProvedOwnMetadataNamespace();
    [TestMethod]
    public void ActualCoreClrCachedInitializerWrapperResetsItsPropagationTrace() => WarpPortableSourceInitializerNameCases.ActualCoreClrCachedInitializerWrapperResetsItsPropagationTrace();
}
