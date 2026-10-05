namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void RawSourceUtf16IdentityDistinguishesTheActualJsonReplacementCollision() => WarpPortableSourceUtf16Cases.RawSourceUtf16IdentityDistinguishesTheActualJsonReplacementCollision();
    [TestMethod]
    public void ResourceAndFactoryDataHashesKeepExactUnpairedMessageAndParameterUnits() => WarpPortableSourceUtf16Cases.ResourceAndFactoryDataHashesKeepExactUnpairedMessageAndParameterUnits();
    [TestMethod]
    public void OperationDescriptorUsesTheSameVersionedRawUtf16IdentityAsItsCapturedRow() => WarpPortableSourceUtf16Cases.OperationDescriptorUsesTheSameVersionedRawUtf16IdentityAsItsCapturedRow();
    [TestMethod]
    public void ActualGeneratedResourceAndExceptionPreparationPreservesEveryRawUtf16Unit() => WarpPortableSourceUtf16Cases.ActualGeneratedResourceAndExceptionPreparationPreservesEveryRawUtf16Unit();
}
