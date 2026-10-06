namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void InitializerOperationOriginsSeparateInvocationFromOriginalProtectedCil() => WarpPortableSourceInitializerFailureCases.InitializerOperationOriginsSeparateInvocationFromOriginalProtectedCil();
    [TestMethod]
    public void InitializerOriginsRejectFabricatedOpcodesEffectsAndOtherClosures() => WarpPortableSourceInitializerFailureCases.InitializerOriginsRejectFabricatedOpcodesEffectsAndOtherClosures();
    [TestMethod]
    public void InitializerMessagesRetainEveryRawUtf16UnitAndExactCapturedCorelibTemplate() => WarpPortableSourceInitializerFailureCases.InitializerMessagesRetainEveryRawUtf16UnitAndExactCapturedCorelibTemplate();
    [TestMethod]
    public void GeneratedInitializerFailureConstructionCachesExactWrapperAndOrdinaryInnerTypes() => WarpPortableSourceInitializerFailureCases.GeneratedInitializerFailureConstructionCachesExactWrapperAndOrdinaryInnerTypes();
    [TestMethod]
    public void CachedInitializerWrapperResetsOnlyItsOwnTraceAndKeepsMutationAndOriginalInner() => WarpPortableSourceInitializerFailureCases.CachedInitializerWrapperResetsOnlyItsOwnTraceAndKeepsMutationAndOriginalInner();
    [TestMethod]
    public void InitializerFailureRootsRetainWrapperNameMessageAndInnerDuringCollection() => WarpPortableSourceInitializerFailureCases.InitializerFailureRootsRetainWrapperNameMessageAndInnerDuringCollection();
    [TestMethod]
    public void InitializerFailureRejectsInvalidOwnersOriginsAndDataBeforeCacheWrites() => WarpPortableSourceInitializerFailureCases.InitializerFailureRejectsInvalidOwnersOriginsAndDataBeforeCacheWrites();
    [TestMethod]
    public void InitializerResourceExhaustionRemainsItsOriginalFaultAndCannotPublishACachedDefault() => WarpPortableSourceInitializerFailureCases.InitializerResourceExhaustionRemainsItsOriginalFaultAndCannotPublishACachedDefault();
    [TestMethod]
    public void CapturedInitializerFailureDataNeverGrantsActualSourceFailureExecution() => WarpPortableSourceInitializerFailureCases.CapturedInitializerFailureDataNeverGrantsActualSourceFailureExecution();
    [TestMethod]
    public void ActualCoreClrOrdinaryCctorFailuresMatchCapturedDataAndAlwaysNestExplicitTies() => WarpPortableSourceInitializerFailureCases.ActualCoreClrOrdinaryCctorFailuresMatchCapturedDataAndAlwaysNestExplicitTies();
}
