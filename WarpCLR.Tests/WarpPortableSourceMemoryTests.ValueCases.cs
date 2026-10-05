namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void GeneratedPackedValueCopiesRetainEveryByteAndLeaveAdjacentFields() => WarpPortableSourceValueCases.GeneratedPackedValueCopiesRetainEveryByteAndLeaveAdjacentFields();

    [TestMethod]
    public void BooleanMemoryKeepsTheLowByteAndReadZeroesScratchPadding() => WarpPortableSourceValueCases.BooleanMemoryKeepsTheLowByteAndReadZeroesScratchPadding();

    [TestMethod]
    public void ReferenceAggregateWritesPrevalidateEveryOwnerBeforeAnyMutation() => WarpPortableSourceValueCases.ReferenceAggregateWritesPrevalidateEveryOwnerBeforeAnyMutation();

    [TestMethod]
    public void ReferenceAggregateLeaseIsRequiredForReadWriteAndInitialization() => WarpPortableSourceValueCases.ReferenceAggregateLeaseIsRequiredForReadWriteAndInitialization();

    [TestMethod]
    public void EqualWidthRetypingAndScratchBoundsFailBeforePayloadWrites() => WarpPortableSourceValueCases.EqualWidthRetypingAndScratchBoundsFailBeforePayloadWrites();

    [TestMethod]
    public void StaticValueStorageUsesExactDeclaredTypeAndPackedBytes() => WarpPortableSourceValueCases.StaticValueStorageUsesExactDeclaredTypeAndPackedBytes();

    [TestMethod]
    public void ReferenceCopiesRemainTracedAfterTheirSourceOperationCompletes() => WarpPortableSourceValueCases.ReferenceCopiesRemainTracedAfterTheirSourceOperationCompletes();

    [TestMethod]
    public void ReadonlyCovariantReferenceViewKeepsTheActualOwnerAndType() => WarpPortableSourceValueCases.ReadonlyCovariantReferenceViewKeepsTheActualOwnerAndType();

    [TestMethod]
    public void ImportedArithmeticAndArenaServicesExecuteInsideTheCallerWordGraph() => WarpPortableGeneratedServiceImportCases.ImportedArithmeticAndArenaServicesExecuteInsideTheCallerWordGraph();

    [TestMethod]
    public void ExactSchemaSemanticVersionAndBankKindChangeImportedIdentity() => WarpPortableGeneratedServiceImportCases.ExactSchemaSemanticVersionAndBankKindChangeImportedIdentity();

    [TestMethod]
    public void RepeatedImportReusesExactHelpersAndRetainsImmutableBindingRows() => WarpPortableGeneratedServiceImportCases.RepeatedImportReusesExactHelpersAndRetainsImmutableBindingRows();

    [TestMethod]
    public void ArbitraryHostMethodsUnknownBanksAndMalformedSchemaAreRejected() => WarpPortableGeneratedServiceImportCases.ArbitraryHostMethodsUnknownBanksAndMalformedSchemaAreRejected();

    [TestMethod]
    public void MixedFrameServiceClosureRequiresEveryExactStateAndArenaBinding() => WarpPortableGeneratedServiceImportCases.MixedFrameServiceClosureRequiresEveryExactStateAndArenaBinding();

    [TestMethod]
    public void SourceAliasPrefixEndsBeforeItsEvaluationStorage() => WarpPortableGeneratedServiceImportCases.SourceAliasPrefixEndsBeforeItsEvaluationStorage();

    [TestMethod]
    public void GeneratedMixedReadUsesTheLiveSourceActivationAndExactWideType() => WarpPortableSourceFrameValueCases.GeneratedMixedReadUsesTheLiveSourceActivationAndExactWideType();

    [TestMethod]
    public void GeneratedMixedWriteChangesOnlyTheCapturedPrivateWideSlot() => WarpPortableSourceFrameValueCases.GeneratedMixedWriteChangesOnlyTheCapturedPrivateWideSlot();

    [TestMethod]
    public void StaleFrameOwnersAndEqualWidthRetypingCannotMutateSourceStorage() => WarpPortableSourceFrameValueCases.StaleFrameOwnersAndEqualWidthRetypingCannotMutateSourceStorage();

    [TestMethod]
    public void ReferenceOutWriteRetainsItsLeaseUntilTheWholeOwnerIsCopied() => WarpPortableSourceFrameValueCases.ReferenceOutWriteRetainsItsLeaseUntilTheWholeOwnerIsCopied();

    [TestMethod]
    public void InvalidReferenceScratchFailsBeforeAnOutOwnerBeginsChanging() => WarpPortableSourceFrameValueCases.InvalidReferenceScratchFailsBeforeAnOutOwnerBeginsChanging();

    [TestMethod]
    public void RuntimeFrameServicesRemainZeroSourceCostWithExactBankBindings() => WarpPortableSourceFrameValueCases.RuntimeFrameServicesRemainZeroSourceCostWithExactBankBindings();

}
