namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void CapturedExceptionDataBindsDenseKindsHResultsAndAppendOnlyRoots() => WarpPortableSourceExceptionDataCases.CapturedExceptionDataBindsDenseKindsHResultsAndAppendOnlyRoots();
    [TestMethod]
    public void GeneratedExceptionDataPreservesRawFieldsAndOriginalInnerIdentity() => WarpPortableSourceExceptionDataCases.GeneratedExceptionDataPreservesRawFieldsAndOriginalInnerIdentity();
    [TestMethod]
    public void GeneratedExceptionInitializationValidatesEveryReferenceBeforeWriting() => WarpPortableSourceExceptionDataCases.GeneratedExceptionInitializationValidatesEveryReferenceBeforeWriting();
    [TestMethod]
    public void InitializedFaultGenerationsCannotBeReusedByClearingHResult() => WarpPortableSourceExceptionDataCases.InitializedFaultGenerationsCannotBeReusedByClearingHResult();
    [TestMethod]
    public void ExceptionDataRequiresExactOwnerLeaseAndCapturedDescriptor() => WarpPortableSourceExceptionDataCases.ExceptionDataRequiresExactOwnerLeaseAndCapturedDescriptor();
    [TestMethod]
    public void ExceptionParameterActualAndInnerRootsSurvivePreciseCollection() => WarpPortableSourceExceptionDataCases.ExceptionParameterActualAndInnerRootsSurvivePreciseCollection();
    [TestMethod]
    public void RawExceptionMessageDataNeverPretendsToImplementVirtualFormatting() => WarpPortableSourceExceptionDataCases.RawExceptionMessageDataNeverPretendsToImplementVirtualFormatting();
}
