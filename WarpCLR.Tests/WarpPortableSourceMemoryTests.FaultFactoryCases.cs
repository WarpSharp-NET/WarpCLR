namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void CapturedFaultResourcesBindExactCorelibCultureAndUtf16Data() => WarpPortableSourceFaultFactoryCases.CapturedFaultResourcesBindExactCorelibCultureAndUtf16Data();
    [TestMethod]
    public void MissingResourcesAndOtherTypedClosuresCannotGrantAFaultContract() => WarpPortableSourceFaultFactoryCases.MissingResourcesAndOtherTypedClosuresCannotGrantAFaultContract();
    [TestMethod]
    public void FiniteIntegerFaultRowsPreserveOriginalOpcodeEffectAndExceptionType() => WarpPortableSourceFaultFactoryCases.FiniteIntegerFaultRowsPreserveOriginalOpcodeEffectAndExceptionType();
    [TestMethod]
    public void MathFactoryRowsKeepOperationSpecificDescriptorsAndDeclaredParameters() => WarpPortableSourceFaultFactoryCases.MathFactoryRowsKeepOperationSpecificDescriptorsAndDeclaredParameters();
    [TestMethod]
    public void ConstructorPlanningRetainsExactPrivateOwnerFieldsWithoutSourceExecution() => WarpPortableSourceFaultFactoryCases.ConstructorPlanningRetainsExactPrivateOwnerFieldsWithoutSourceExecution();
    [TestMethod]
    public void EmbeddedValueConstructorOwnersBindLeafTypesPathsAndMapIdentity() => WarpPortableSourceFaultFactoryCases.EmbeddedValueConstructorOwnersBindLeafTypesPathsAndMapIdentity();
    [TestMethod]
    public void RegisteredFlattenedTupleConstructionDoesNotReserveUnusedObjectStorage() => WarpPortableSourceFaultFactoryCases.RegisteredFlattenedTupleConstructionDoesNotReserveUnusedObjectStorage();
}
