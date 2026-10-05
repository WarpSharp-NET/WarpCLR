using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private static void RequireInvocationPrelude(WarpPortableMethodGraph graph, WarpPortableWordLoweredProgram program,
        WarpPortableWordBody body, WarpPortableWordBody planned, HashSet<int> seen)
    {
        if (body.InvocationPrelude is not { } prelude) { return; }
        WarpPortableTypedInitializerTrigger? trigger = program.VerifiedProgram.EntryInitializerTrigger;
        if (trigger is null || body.AliasOwnerFunction != -1 || !string.Equals(body.MethodIdentity, graph.EntryIdentity, StringComparison.Ordinal) ||
            !string.Equals(prelude.MethodIdentity, body.MethodIdentity, StringComparison.Ordinal) || !Same(prelude.Trigger, trigger) ||
            prelude.GeneratedBlocks.Length != 6 || prelude.GeneratedBlocks[0] != 0 ||
            !Same(prelude.Roots, planned.SourceBlocks[0].Roots))
        {
            throw WarpPortableWordProgramIdentity.Invalid("The root initializer invocation has fabricated source/alias/type/argument root metadata.");
        }
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, program.VerifiedProgram);
        WarpPortableSourceInitializerPlan expected = WarpPortableSourceInitializerPlan.Capture(graph, program.VerifiedProgram, schema);
        if (prelude.DeclaringType != schema.TypeId(trigger.DeclaringType) ||
            !string.Equals(prelude.InitializerPlanHash, expected.PlanHash, StringComparison.Ordinal) ||
            program.ExecutionBindingHash is null || !program.RequiredServices.Contains(WarpPortableWordInvocationPrelude.Semantics, StringComparer.Ordinal))
        {
            throw WarpPortableWordProgramIdentity.Invalid("The root invocation lacks its exact captured initializer closure and compiled service binding.");
        }
        WarpLogicalBodyMetadata metadata = program.Kernel.Execution!.Bodies[body.Function];
        int count = program.Kernel.Functions[body.Function - 1].Blocks.Count;
        var distinct = new HashSet<int>();
        foreach (int block in prelude.GeneratedBlocks)
        {
            if (block < 0 || block >= count || !distinct.Add(block) || metadata.SourceBlockCosts[block] != 0 || block != 0 && !seen.Add(block))
            {
                throw WarpPortableWordProgramIdentity.Invalid("A root invocation block is duplicated, unbound or charged as original guest CIL.");
            }
        }
    }
}
