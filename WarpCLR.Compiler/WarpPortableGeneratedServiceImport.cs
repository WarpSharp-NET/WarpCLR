namespace WarpCLR.Compiler;

internal sealed record WarpPortableGeneratedServiceImport(int Function, int ParameterWords, string Identity,
    string Signature, string Semantics, string SemanticIrHash, string TypeSchemaHash, bool RequiresStateAccess);
