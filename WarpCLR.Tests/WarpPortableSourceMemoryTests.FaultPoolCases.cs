namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void GeneratedFaultPreparationUsesExactMessagesTypesParametersAndHResults() => WarpPortableSourceFaultPoolCases.GeneratedFaultPreparationUsesExactMessagesTypesParametersAndHResults();
    [TestMethod]
    public void PreparedFaultGenerationsAndRuntimeRootsCannotBeReusedOrReleasedByAHostRootCall() => WarpPortableSourceFaultPoolCases.PreparedFaultGenerationsAndRuntimeRootsCannotBeReusedOrReleasedByAHostRootCall();
    [TestMethod]
    public void FaultPreparationRejectsMetadataTextLeaseAndOriginalSiteMismatchBeforeAllocation() => WarpPortableSourceFaultPoolCases.FaultPreparationRejectsMetadataTextLeaseAndOriginalSiteMismatchBeforeAllocation();
    [TestMethod]
    public void PreparedValidationPreservesReadyStateAndDoesNotMintSourceAuthority() => WarpPortableSourceFaultPoolCases.PreparedValidationPreservesReadyStateAndDoesNotMintSourceAuthority();
    [TestMethod]
    public void FaultPoolAttachmentIsBoundedAtomicAndCannotRepurposeReservedRoots() => WarpPortableSourceFaultPoolCases.FaultPoolAttachmentIsBoundedAtomicAndCannotRepurposeReservedRoots();
}
