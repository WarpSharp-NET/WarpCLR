namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void NativeNumericConversionsRequireTheirOwnGraphBoundProfile() => WarpPortableCliNativeCases.NativeNumericConversionsRequireTheirOwnGraphBoundProfile();
    [TestMethod]
    public void NativeSignedAndUnsignedExpansionMatchActualCoreClr64() => WarpPortableCliNativeCases.NativeSignedAndUnsignedExpansionMatchActualCoreClr64();
    [TestMethod]
    public void NativeShiftsKeepSixtyFourBitValuesAndCounts() => WarpPortableCliNativeCases.NativeShiftsKeepSixtyFourBitValuesAndCounts();
    [TestMethod]
    public void NativeMixedI4ArithmeticPromotesWithSignedExtension() => WarpPortableCliNativeCases.NativeMixedI4ArithmeticPromotesWithSignedExtension();
    [TestMethod]
    public void NativeMixedI4ComparisonsKeepHighWordsAndSignedPromotion() => WarpPortableCliNativeCases.NativeMixedI4ComparisonsKeepHighWordsAndSignedPromotion();
    [TestMethod]
    public void NativeConditionalsConsumeBothWordsAndUseExactMixedComparisons() => WarpPortableCliNativeCases.NativeConditionalsConsumeBothWordsAndUseExactMixedComparisons();
    [TestMethod]
    public void NativePrivateStorageAndCallsCoerceOnlyAtDeclaredWidths() => WarpPortableCliNativeCases.NativePrivateStorageAndCallsCoerceOnlyAtDeclaredWidths();
    [TestMethod]
    public void LdlenAndArrayLengthRetainNativeCategoryBeforeExplicitI4Conversion() => WarpPortableCliNativeCases.LdlenAndArrayLengthRetainNativeCategoryBeforeExplicitI4Conversion();
    [TestMethod]
    public void NativeEvaluationCannotAdmitNativeStorageOrManagedAddressEscapes() => WarpPortableCliNativeCases.NativeEvaluationCannotAdmitNativeStorageOrManagedAddressEscapes();
    [TestMethod]
    public void NativeCheckedFaultsRemainUnboundAtTheirOriginalOffsets() => WarpPortableCliNativeCases.NativeCheckedFaultsRemainUnboundAtTheirOriginalOffsets();
}
