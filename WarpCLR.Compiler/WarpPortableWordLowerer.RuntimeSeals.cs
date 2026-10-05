using System.Runtime.CompilerServices;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private static readonly ConditionalWeakTable<WarpPortableWordLoweredProgram, RuntimeSourceSeal> RuntimeSourceSeals = new();

    private static WarpPortableWordLoweredProgram SealRuntimeProgram(WarpPortableMethodGraph graph,
        WarpPortableSourceHeapSchema schema, WarpPortableWordLoweredProgram program, ExceptionAttachment? attachment)
    {
        program.SealCompilerIdentity(graph, schema, attachment);
        RuntimeSourceSeals.Add(program, new(graph, schema.SchemaHash, program.CompilerIdentity!));
        return program;
    }

    internal static WarpPortableWordProgramIdentity RequireRuntimeCompilerSeal(WarpPortableMethodGraph graph,
        WarpPortableSourceHeapSchema schema, WarpPortableWordLoweredProgram program)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(program);
        if (!RuntimeSourceSeals.TryGetValue(program, out RuntimeSourceSeal? seal) || !ReferenceEquals(graph, seal.Graph))
        {
            throw WarpPortableWordProgramIdentity.Invalid("Runtime source admission requires the exact program and captured closure issued by the compiler.");
        }
        WarpPortableWordProgramIdentity identity = WarpPortableWordProgramIdentity.Validate(graph, schema, program);
        if (!ReferenceEquals(identity, seal.Identity) || !string.Equals(schema.SchemaHash, seal.SchemaHash, StringComparison.Ordinal))
        {
            throw WarpPortableWordProgramIdentity.Invalid("The compiler-issued source seal does not match this exact program identity and schema.");
        }
        return identity;
    }

    private sealed record RuntimeSourceSeal(WarpPortableMethodGraph Graph, string SchemaHash, WarpPortableWordProgramIdentity Identity);
}
