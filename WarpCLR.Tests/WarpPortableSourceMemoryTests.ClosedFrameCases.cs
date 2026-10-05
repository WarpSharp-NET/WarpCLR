namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void ActualValueConstructorWritesEveryPackedByteAndPreservesRawFloatBits() => WarpPortableClosedFrameSourceCases.ActualValueConstructorWritesEveryPackedByteAndPreservesRawFloatBits();
    [TestMethod]
    public void ValueConstructorResultAndInstanceFieldReadsPreserveDeclaredWidths() => WarpPortableClosedFrameSourceCases.ValueConstructorResultAndInstanceFieldReadsPreserveDeclaredWidths();
    [TestMethod]
    public void CalleeWritesThroughCallerFrameByrefsWithoutOverwritingPackedNeighbours() => WarpPortableClosedFrameSourceCases.CalleeWritesThroughCallerFrameByrefsWithoutOverwritingPackedNeighbours();
    [TestMethod]
    public void ClosedRecursiveFrameCallsKeepDistinctPrivateOwnerActivations() => WarpPortableClosedFrameSourceCases.ClosedRecursiveFrameCallsKeepDistinctPrivateOwnerActivations();
    [TestMethod]
    public void ActualReferenceContainingValueConstructorCopiesAndClearsExactOwnerFields() => WarpPortableClosedFrameSourceCases.ActualReferenceContainingValueConstructorCopiesAndClearsExactOwnerFields();
    [TestMethod]
    public void ConstructorAndMemoryHelpersHaveExactOriginalSourceChargesAndRootMaps() => WarpPortableClosedFrameSourceCases.ConstructorAndMemoryHelpersHaveExactOriginalSourceChargesAndRootMaps();
    [TestMethod]
    public void FrameSourceBindingCannotGrantHeapInitializersExceptionsOrExternalByrefs() => WarpPortableClosedFrameSourceCases.FrameSourceBindingCannotGrantHeapInitializersExceptionsOrExternalByrefs();
    [TestMethod]
    public void ChangedFramePlansTemporaryOwnersAndClosuresCannotReuseCompilerIdentity() => WarpPortableClosedFrameSourceCases.ChangedFramePlansTemporaryOwnersAndClosuresCannotReuseCompilerIdentity();
    [TestMethod]
    public void CorruptPrivateSourceOwnersFailBeforeWritingAnyPackedPayloadByte() => WarpPortableClosedFrameSourceCases.CorruptPrivateSourceOwnersFailBeforeWritingAnyPackedPayloadByte();
    [TestMethod]
    public void ActualBooleanConstructorStoresTheDeclaredByteWithoutNormalizingTruth() => WarpPortableClosedFrameSourceCases.ActualBooleanConstructorStoresTheDeclaredByteWithoutNormalizingTruth();
    [TestMethod]
    public void OrdinaryInstanceAndClosedGenericByrefCallsExecuteCapturedSource() => WarpPortableClosedFrameSourceCases.OrdinaryInstanceAndClosedGenericByrefCallsExecuteCapturedSource();
    [TestMethod]
    public void CalleeCanInstallAReferenceInAnInitiallyNullCallerFieldUnderFixedRootMaps() => WarpPortableClosedFrameSourceCases.CalleeCanInstallAReferenceInAnInitiallyNullCallerFieldUnderFixedRootMaps();
}
