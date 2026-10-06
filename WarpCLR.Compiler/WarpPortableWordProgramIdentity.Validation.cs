using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableWordProgramIdentity
{
    private static void RequireClosure(WarpPortableMethodGraph graph, WarpPortableSourceHeapSchema schema, WarpPortableWordLoweredProgram program)
    {
        if (!string.Equals(graph.GraphHash, program.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(graph.GraphHash, schema.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(program.VerifiedHash, schema.VerifiedHash, StringComparison.Ordinal))
        {
            throw Invalid("The graph, typed program and schema describe different closures.");
        }
        WarpPortableTypedProgram verified = WarpPortableTypedProgram.Verify(graph, program.VerifiedProgram.CliSizes);
        if (!string.Equals(verified.VerifiedHash, program.VerifiedHash, StringComparison.Ordinal) ||
            !string.Equals(SnapshotHash(new { verified.Types, verified.Methods }),
                SnapshotHash(new { program.VerifiedProgram.Types, program.VerifiedProgram.Methods }), StringComparison.Ordinal))
        {
            throw Invalid("The typed source snapshot differs from verification of the captured closure.");
        }
        WarpPortableSourceHeapSchema captured = WarpPortableSourceHeapSchema.Create(graph, verified);
        if (!string.Equals(captured.SchemaHash, schema.SchemaHash, StringComparison.Ordinal))
        {
            throw Invalid("The source type/field/fault/view schema differs from the verified closure.");
        }
        if (program.VerifiedProgram.Types.Any(type => type.Category == WarpPortableStackCategory.CliNativeInteger) &&
            program.VerifiedProgram.CliSizes?.NativeEvaluationBits != 64)
        {
            throw Invalid("A native evaluation value has no exact CLI64 semantic profile.");
        }
    }

    private static void RequireKernelIdentity(WarpPortableWordLoweredProgram program)
    {
        string maps = WarpPortableWordMapHash.Compute(program.Bodies, program.EntryProjection);
        if (!string.Equals(maps, program.MapsHash, StringComparison.Ordinal)) { throw Invalid("The source maps differ from their canonical identity."); }
        string[] services = program.RequiredServices.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!services.SequenceEqual(program.RequiredServices, StringComparer.Ordinal)) { throw Invalid("Required services are not the exact ordered immutable catalog."); }
        string serviceHash = WarpPortableSnapshotIdentity.Hash(services);
        string name = WarpPortableWordLowerer.Version + "/" + program.VerifiedHash + "/" + serviceHash + "/" + maps;
        string loweredHash = WarpPortableSnapshotIdentity.Hash(new { Version = WarpPortableWordLowerer.Version, program.VerifiedHash, IrHash = WarpIrHash.Compute(program.Kernel) });
        if (!string.Equals(name, program.Kernel.Name, StringComparison.Ordinal) || !string.Equals(loweredHash, program.LoweredHash, StringComparison.Ordinal))
        {
            throw Invalid("The final kernel or lowered hash differs from the exact compiler catalog and maps.");
        }
        RequireExecutionBindings(program, services);
    }

    private static void RequireExecutionBindings(WarpPortableWordLoweredProgram program, string[] services)
    {
        if (program.Kernel.Execution is null) { throw Invalid("The source program requires exact immutable logical frame metadata."); }
        bool privateController = program.Kernel.Execution.PrivateControllerProjection is not null;
        if (program.Kernel.ScalarArgumentCount != (privateController ? 1 : 0) || privateController &&
            (program.ExecutionBindingHash is null || !services.Contains(WarpPrivateControllerOpCode.Version, StringComparer.Ordinal)))
        {
            throw Invalid("A source program's physical controller scalar is separate from its original guest arguments and requires its sealed binding.");
        }
        if (program.Kernel.Execution.PrivateControllerProjection?.RequiresHelperBoundaries == true &&
            !services.Contains(WarpPrivateControllerProjection.HelperBoundarySemantics, StringComparer.Ordinal))
        { throw Invalid("Private helper boundaries require their exact compiler service semantics."); }
        if (program.Kernel.Execution.PrivateControllerProjection?.RequiresHelperReturnFences == true &&
            (!services.Contains(WarpPrivateControllerProjection.HelperReturnFenceSemantics, StringComparer.Ordinal) ||
            !services.Contains(WarpLogicalMachineLayout.PrivateHelperScopeVersion, StringComparer.Ordinal)))
        { throw Invalid("Private helper return fences require their exact compiler service semantics."); }
        if ((program.ExecutionBindingHash is null) != (program.ExecutionPlanHash is null)) { throw Invalid("The source binding and completed execution plan must appear together."); }
        if (program.ExecutionPlanHash is { } plan)
        {
            WarpPortableWordExecutionBinding.RequireHash(plan); WarpPortableWordExecutionBinding.RequireHash(program.ExecutionBindingHash!);
            if (!services.Contains(WarpPortableWordExecutionBinding.Version + "/plan/" + plan, StringComparer.Ordinal))
            {
                throw Invalid("The final execution plan is absent from the compiler's exact service identity.");
            }
        }
        if (program.VerifiedProgram.CliSizes is { } cli && (!services.Contains(WarpPortableCliSizeContract.Semantics + "/" + cli.ContractHash, StringComparer.Ordinal) ||
            !services.Contains(WarpPortableCliNativeInteger.Semantics + "/" + cli.ContractHash, StringComparer.Ordinal)))
        {
            throw Invalid("The exact CLI numeric width contract is absent from the required service catalog.");
        }
    }
}
