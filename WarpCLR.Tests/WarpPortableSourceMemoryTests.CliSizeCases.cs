namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void ExplicitCliPrimitiveAndReferenceSizesRemainSeparateFromOwnerWords() => WarpPortableSourceCliSizeCases.ExplicitCliPrimitiveAndReferenceSizesRemainSeparateFromOwnerWords();
    [TestMethod]
    public void PackedAndNestedCliSizesIncludeTheirOwnClrPaddingAndOffsets() => WarpPortableSourceCliSizeCases.PackedAndNestedCliSizesIncludeTheirOwnClrPaddingAndOffsets();
    [TestMethod]
    public void ReferenceContainingCliSizeNeverUsesItsPortableTwelveByteOwners() => WarpPortableSourceCliSizeCases.ReferenceContainingCliSizeNeverUsesItsPortableTwelveByteOwners();
    [TestMethod]
    public void ExplicitAndClosedGenericCliLayoutsRetainExactMetadataSpecializations() => WarpPortableSourceCliSizeCases.ExplicitAndClosedGenericCliLayoutsRetainExactMetadataSpecializations();
    [TestMethod]
    public void CliLayoutCaptureAndGeneratedExecutionNeverRunUserTypeInitializers() => WarpPortableSourceCliSizeCases.CliLayoutCaptureAndGeneratedExecutionNeverRunUserTypeInitializers();
    [TestMethod]
    public void CliSizeBindingChangesVerifiedKernelIdentityAndRejectsAnotherClosure() => WarpPortableSourceCliSizeCases.CliSizeBindingChangesVerifiedKernelIdentityAndRejectsAnotherClosure();
    [TestMethod]
    public void UnboundReferenceAndCompoundSizeofRemainExplicitlyRejected() => WarpPortableSourceCliSizeCases.UnboundReferenceAndCompoundSizeofRemainExplicitlyRejected();
    [TestMethod]
    public void NativeArrayLengthShiftCannotBeAdmittedAsAnI4Operation() => WarpPortableSourceCliSizeCases.NativeArrayLengthShiftCannotBeAdmittedAsAnI4Operation();
    [TestMethod]
    public void CliCaptureRejectsOversizedValueLocalsBeforeFieldAddressCapture() => WarpPortableSourceCliSizeCases.CliCaptureRejectsOversizedValueLocalsBeforeFieldAddressCapture();
}
