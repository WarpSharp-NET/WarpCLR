namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void MetadataValuesAndCharactersPreserveAllUtf16CodeUnits() => WarpPortableMetadataUtf16Cases.SnapshotValuesAndCharactersPreserveEveryUtf16Unit();

    [TestMethod]
    public void MetadataDictionaryKeysRemainInjectiveBeforeJsonEncoding() => WarpPortableMetadataUtf16Cases.SnapshotDictionaryKeysAreInjectiveBeforeJsonEncoding();

    [TestMethod]
    public void MetadataBinarySnapshotsUseExactLengthsAndLittleEndianUnits() => WarpPortableMetadataUtf16Cases.BinarySnapshotUsesExactLengthAndLittleEndianUnits();

    [TestMethod]
    public void RuntimeHeapIdentityDigestKeepsRawTypeNamesAndIdenticalStorage() => WarpPortableMetadataUtf16Cases.RuntimeHeapDigestDistinguishesRawTypeNamesWithoutChangingStorage();

    [TestMethod]
    public void ManifestEncodingRejectsRawReplacementAndKeepsActualUtf8Bytes() => WarpPortableMetadataUtf16Cases.ManifestUtf8RejectsReplacementAndPreservesActualTextBytes();

    [TestMethod]
    public void CapturedLiteralAndMemberIdentitySnapshotsAreExactBeforeAdmission() => WarpPortableMetadataUtf16Cases.CapturedLiteralAndMemberSnapshotsDistinguishRawValuesBeforeAdmission();

    [TestMethod]
    public void TypedIdentityAndRootProvenanceSnapshotsKeepRawUtf16Units() => WarpPortableMetadataUtf16Cases.TypedSnapshotAndRootProvenanceKeepExactRawIdentityUnits();
}
