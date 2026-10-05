namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void GeneratedArrayLengthsMaterializeAnExactNativeUnsignedPair() => WarpPortableSourceArrayLengthCases.GeneratedArrayLengthsMaterializeAnExactNativeUnsignedPair();

    [TestMethod]
    public void GeneratedArrayLengthRejectsNullStaleAndUnboundShapesBeforePublishing() => WarpPortableSourceArrayLengthCases.GeneratedArrayLengthRejectsNullStaleAndUnboundShapesBeforePublishing();
}
