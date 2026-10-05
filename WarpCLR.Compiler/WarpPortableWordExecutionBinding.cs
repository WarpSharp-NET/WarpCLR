using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal abstract class WarpPortableWordExecutionBinding
{
    internal const string Version = "warp.source-binding/closed-typed-ir-hooks-layout-projection-final-ir/0.1";

    protected WarpPortableWordExecutionBinding(string graphHash, string verifiedHash, string typeSchemaHash,
        string semantics, string bindingHash, WarpPortableWordExecutionCapabilities capabilities,
        IEnumerable<string>? additionalSourceMethods = null, IEnumerable<string>? exceptionMethods = null)
    {
        RequireHash(graphHash); RequireHash(verifiedHash); RequireHash(typeSchemaHash); RequireHash(bindingHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(semantics); ArgumentNullException.ThrowIfNull(capabilities);
        GraphHash = graphHash; VerifiedHash = verifiedHash; TypeSchemaHash = typeSchemaHash;
        Semantics = semantics; BindingHash = bindingHash; Capabilities = capabilities;
        AdditionalSourceMethods = Snapshot(additionalSourceMethods);
        ExceptionMethods = Snapshot(exceptionMethods);
    }

    internal string GraphHash { get; }
    internal string VerifiedHash { get; }
    internal string TypeSchemaHash { get; }
    internal string Semantics { get; }
    internal string BindingHash { get; }
    internal WarpPortableWordExecutionCapabilities Capabilities { get; }
    internal ImmutableArray<string> AdditionalSourceMethods { get; }
    internal ImmutableArray<string> ExceptionMethods { get; }

    // These are compiler extension points. They emit fixed verified word IR; they never run source CIL.
    internal virtual void Prepare(WarpPortableWordBindingPreparation context) { }
    internal virtual void AfterSourceLowering(WarpPortableWordBindingPreparation context) { }
    internal abstract WarpBlockTerminator? LowerInstruction(WarpPortableWordInstructionContext context);
    internal abstract string Complete(WarpPortableWordBindingCompletion context);

    internal void RequireClosure(WarpPortableMethodGraph graph, WarpPortableTypedProgram program, WarpPortableSourceHeapSchema schema)
    {
        if (!string.Equals(GraphHash, graph.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(VerifiedHash, program.VerifiedHash, StringComparison.Ordinal) ||
            !string.Equals(TypeSchemaHash, schema.SchemaHash, StringComparison.Ordinal) ||
            !string.Equals(schema.GraphHash, graph.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(schema.VerifiedHash, program.VerifiedHash, StringComparison.Ordinal))
        {
            throw new WarpVerificationException("WRPCLR2300", "The source execution binding belongs to a different exact graph, verified program or heap schema.", 0);
        }
        foreach (string identity in AdditionalSourceMethods.Concat(ExceptionMethods))
        {
            if (!program.Methods.Any(method => string.Equals(method.Identity, identity, StringComparison.Ordinal)))
            {
                throw new WarpVerificationException("WRPCLR2300", "A source execution binding names a method outside its verified closure.", 0);
            }
        }
    }

    internal static void RequireHash(string hash)
    {
        ArgumentException.ThrowIfNullOrEmpty(hash);
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("A source execution binding requires an exact SHA256 identity.", nameof(hash));
        }
    }

    private static ImmutableArray<string> Snapshot(IEnumerable<string>? methods)
    {
        if (methods is null) { return []; }
        string[] values = WarpCompilationAdmission.Materialize(methods, "<source-binding-methods>",
            WarpCompilationResourceKind.Functions, WarpCompilationAdmission.MaximumFunctionsPerEntry);
        if (values.Any(string.IsNullOrWhiteSpace)) { throw new ArgumentException("Every bound method needs its exact captured identity.", nameof(methods)); }
        return values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
    }
}
