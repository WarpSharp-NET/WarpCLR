namespace WarpCLR.Compiler;

internal static class WarpPortableSourceInitializerFailureLayout
{
    internal const string Semantics = "warp.source-initializer-failure-table/header58-tagged-origin-captured-method-index-runtime-owned-name-message-wrapper-roots-per-type-cache/0.1";
    public const uint Descriptor = 58;
    public const uint Magic = 0x57524946;
    public const uint Version = 1;
    public const uint HeaderWords = 64;
    public const uint DescriptorWords = 2;
    public const uint Context = 3;
    public const uint TypeCount = 4;
    public const uint TypeStart = 5;
    public const uint RowCount = 6;
    public const uint RowStart = 7;
    public const uint TextStart = 8;
    public const uint TextUnits = 9;
    public const uint StringType = 10;
    public const uint RootFirst = 12;
    public const uint RootCount = 13;
    public const uint PlanHash = 16;
    public const uint SchemaHash = 24;
    public const uint InitializerPlanHash = 32;
    public const uint ResourceHash = 40;
    // Zero or copied hashes provide no source/factory authority.
    public const uint AdmittedProgramHash = 48;
    public const uint AdmittedExceptionPlanHash = 56;
    public const uint TypeWords = 32;
    public const uint TypeId = 0;
    public const uint WrapperType = 1;
    public const uint DefaultHResult = 2;
    public const uint NameText = 3;
    public const uint NameUnits = 4;
    public const uint MessageText = 5;
    public const uint MessageUnits = 6;
    public const uint State = 7;
    public const uint WrapperReference = 8;
    public const uint NameReference = 11;
    public const uint MessageReference = 14;
    public const uint FirstRoot = 17;
    public const uint RootGeneration = 18;
    public const uint InnerReference = 19;
    public const uint DataHash = 24;
    public const uint RowWords = 24;
    public const uint RowId = 0;
    public const uint RowTypeRecord = 1;
    public const uint RowOriginKind = 2;
    public const uint RowCapturedMethodIndex = 3;
    public const uint RowOffset = 4;
    public const uint RowOpcode = 5;
    public const uint RowEffect = 6;
    public const uint RowSourceHash = 8;
    public const uint RowOriginHash = 16;
    public const uint Empty = 0;
    public const uint Preparing = 1;
    public const uint Ready = 2;
    public const uint Cached = 3;
    public const uint Failed = 4;
}
