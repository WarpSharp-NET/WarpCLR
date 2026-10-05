using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// Identity validation checks a compiler result. Production source admission is a
// separate, evidence-bound decision; possession of this descriptor grants none.
internal sealed partial class WarpPortableWordProgramIdentity
{
    internal const string Version = "warp.source-program-identity/closed-graph-typed-schema-cli-maps-services-layout-final-ir-compiler-seal/0.1";
    internal const string LayoutProjection = "warp.source-program.layout/full-common-ir-except-final-entry-label/0.1";

    private WarpPortableWordProgramIdentity(WarpPortableMethodGraph graph, WarpPortableSourceHeapSchema schema, WarpPortableWordLoweredProgram program)
    {
        GraphHash = graph.GraphHash; VerifiedHash = program.VerifiedHash; TypeSchemaHash = schema.SchemaHash;
        CliContractHash = program.VerifiedProgram.CliSizes?.ContractHash;
        NativeEvaluationBits = program.VerifiedProgram.CliSizes?.NativeEvaluationBits ?? 0;
        RuntimeProfile = program.VerifiedProgram.CliSizes?.RuntimeProfile;
        LoweredHash = program.LoweredHash; MapsHash = program.MapsHash;
        SourceMapsHash = SnapshotHash(new { program.Bodies, program.EntryProjection });
        KernelIrHash = WarpIrHash.Compute(program.Kernel); StructuralLayoutHash = ComputeLayoutProjection(program.Kernel);
        ExecutionBindingHash = program.ExecutionBindingHash; ExecutionPlanHash = program.ExecutionPlanHash;
        RequiredServices = program.RequiredServices; BankCatalogIdentity = WarpPortableSourceServiceBanks.Semantics;
        IdentityHash = SnapshotHash(new
        {
            Version, GraphHash, VerifiedHash, TypeSchemaHash, CliContractHash, NativeEvaluationBits, RuntimeProfile,
            LoweredHash, MapsHash, SourceMapsHash, KernelIrHash, StructuralLayoutHash,
            ExecutionBindingHash, ExecutionPlanHash, RequiredServices, BankCatalogIdentity,
            GraphVersion = WarpPortableMethodGraph.Version, TypedVersion = WarpPortableTypedProgram.Version,
            SchemaVersion = WarpPortableSourceHeapSchema.Version, MapVersion = WarpPortableWordMapHash.Version,
            CliVersion = WarpPortableCliSizeContract.Semantics, NativeVersion = WarpPortableCliNativeInteger.Semantics,
            MachineVersion = WarpLogicalMachineLayout.Version, FrameVersion = WarpPortableSourceFrameSchema.Semantics,
        });
    }

    internal string IdentityHash { get; }
    internal string GraphHash { get; }
    internal string VerifiedHash { get; }
    internal string TypeSchemaHash { get; }
    internal string? CliContractHash { get; }
    internal int NativeEvaluationBits { get; }
    internal string? RuntimeProfile { get; }
    internal string LoweredHash { get; }
    internal string MapsHash { get; }
    internal string SourceMapsHash { get; }
    internal string KernelIrHash { get; }
    internal string StructuralLayoutHash { get; }
    internal string? ExecutionBindingHash { get; }
    internal string? ExecutionPlanHash { get; }
    internal ImmutableArray<string> RequiredServices { get; }
    internal string BankCatalogIdentity { get; }

    internal static WarpPortableWordProgramIdentity Validate(WarpPortableMethodGraph graph,
        WarpPortableSourceHeapSchema schema, WarpPortableWordLoweredProgram program)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(schema); ArgumentNullException.ThrowIfNull(program);
        WarpPortableWordProgramIdentity sealedIdentity = program.CompilerIdentity ?? throw Invalid("The program has no compiler identity seal.");
        RequireClosure(graph, schema, program);
        WarpPortableWordLowerer.ValidateSourceMaps(graph, program);
        RequireKernelIdentity(program);
        var current = new WarpPortableWordProgramIdentity(graph, schema, program);
        if (!string.Equals(current.IdentityHash, sealedIdentity.IdentityHash, StringComparison.Ordinal))
        {
            throw Invalid("The program differs from its exact compiler-sealed source, maps, services, plan or kernel.");
        }
        return sealedIdentity;
    }

    internal static string ComputeLayoutProjection(WarpControlFlowKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        return WarpIrHash.Compute(new WarpControlFlowKernel(LayoutProjection, kernel.InputBufferCount,
            kernel.ScalarArgumentCount, kernel.Blocks, kernel.Reduction, kernel.Functions, kernel.Execution));
    }

    internal static WarpPortableWordProgramIdentity Capture(WarpPortableMethodGraph graph,
        WarpPortableSourceHeapSchema schema, WarpPortableWordLoweredProgram program)
    {
        RequireClosure(graph, schema, program); WarpPortableWordLowerer.ValidateSourceMaps(graph, program); RequireKernelIdentity(program);
        return new(graph, schema, program);
    }

    private static string SnapshotHash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    internal static WarpVerificationException Invalid(string message, int offset = 0) => new("WRPCLR2350", message, offset);
}
