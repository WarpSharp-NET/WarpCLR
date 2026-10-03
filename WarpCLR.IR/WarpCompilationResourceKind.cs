namespace WarpCLR.IR;

public enum WarpCompilationResourceKind
{
    Unknown,
    AssemblyBytes,
    ManifestBytes,
    ManifestEntries,
    Functions,
    Blocks,
    Instructions,
    ValueSlots,
    OperandReferences,
    Parameters,
    Locals,
    EvaluationStack,
    CilBytes,
    VerifierWorkspaceSlots,
    SourceBytes,
    IdentityCharacters,
}
