using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    internal static void ValidateSourceMaps(WarpPortableMethodGraph graph, WarpPortableWordLoweredProgram program)
    {
        if (program.Kernel.Execution is null) { throw WarpPortableWordProgramIdentity.Invalid("The source maps require immutable logical frame metadata."); }
        var planner = new Builder(graph, program.VerifiedProgram);
        foreach (WarpPortableWordBody body in program.Bodies)
        {
            WarpPortableTypedMethod? typed = program.VerifiedProgram.Methods.FirstOrDefault(method => string.Equals(method.Identity, body.MethodIdentity, StringComparison.Ordinal));
            WarpPortableMethodGraphMethod? captured = graph.Methods.FirstOrDefault(method => string.Equals(method.Identity, body.MethodIdentity, StringComparison.Ordinal));
            if (typed is null || captured is null || body.Function <= 0 || body.Function > program.Kernel.Functions.Count)
            {
                throw WarpPortableWordProgramIdentity.Invalid("A source body is outside its exact captured function/method closure.");
            }
            var planned = new MethodLowerer(planner, captured, typed, body.Function - 1);
            RequireStorageMaps(body, planned.Body);
            if (body.AliasOwnerFunction != -1)
            {
                WarpPortableWordBody? original = program.Bodies.FirstOrDefault(candidate => candidate.Function == body.AliasOwnerFunction);
                if (original is null || original.AliasOwnerFunction != -1 || body.AliasPrefixWords != body.StoragePrefixWords ||
                    body.AliasPrefixWords != original.StoragePrefixWords || !string.Equals(original.MethodIdentity, body.MethodIdentity, StringComparison.Ordinal))
                {
                    throw WarpPortableWordProgramIdentity.Invalid("A filter alias has no exact original argument/local prefix owner.");
                }
            }
            RequireSourceBlocks(graph, program, body, planned.Body);
        }
        WarpPortableWordEntryProjection expected = planner.IdentityEntryProjection(program.Bodies);
        if (!Same(expected, program.EntryProjection)) { throw WarpPortableWordProgramIdentity.Invalid("The wrapper/result root maps differ from the exact source entry signature."); }
    }

    private static void RequireStorageMaps(WarpPortableWordBody body, WarpPortableWordBody planned)
    {
        if (!Same(body.Arguments, planned.Arguments) || !Same(body.Locals, planned.Locals) || body.EvaluationWordOffset != planned.EvaluationWordOffset ||
            body.MaximumStackWords != planned.MaximumStackWords || body.PrivateWordCount != planned.PrivateWordCount ||
            !Same(body.PrivateTemporaries, planned.PrivateTemporaries))
        {
            throw WarpPortableWordProgramIdentity.Invalid("A source body's argument/local/evaluation/private word layout differs from its exact typed storage.");
        }
    }

    private static void RequireSourceBlocks(WarpPortableMethodGraph graph, WarpPortableWordLoweredProgram program,
        WarpPortableWordBody body, WarpPortableWordBody planned)
    {
        WarpControlFlowKernel kernel = program.Kernel;
        WarpLogicalBodyMetadata metadata = kernel.Execution!.Bodies[body.Function];
        WarpControlFlowFunction function = kernel.Functions[body.Function - 1];
        if (metadata.RuntimeHelper || metadata.PrivateWordCount != body.PrivateWordCount || metadata.AliasOwnerFunction != body.AliasOwnerFunction ||
            metadata.AliasPrefixWords != body.AliasPrefixWords || metadata.CountsSourceDepth != (body.AliasOwnerFunction == -1) ||
            body.AliasOwnerFunction == -1 && !string.Equals(function.Name, body.MethodIdentity, StringComparison.Ordinal))
        {
            throw WarpPortableWordProgramIdentity.Invalid("The source body and immutable logical-frame/alias metadata differ.");
        }
        if (body.AliasOwnerFunction == -1 && body.SourceBlocks.Length != planned.SourceBlocks.Length)
        {
            throw WarpPortableWordProgramIdentity.Invalid("An original source body has omitted captured reachable instructions.");
        }
        var membership = new HashSet<int>();
        foreach (WarpPortableWordSourceBlock source in body.SourceBlocks)
        {
            WarpPortableWordSourceBlock? expected = planned.SourceBlocks.FirstOrDefault(block => block.Instruction.Offset == source.Instruction.Offset);
            if (expected is null || !Same(source.Instruction, expected.Instruction) || !Same(source.Roots, expected.Roots) || !Same(source.Operation, expected.Operation))
            {
                throw WarpPortableWordProgramIdentity.Invalid("A source instruction, typed root or ordered operation map differs from its original verified offset.", source.Instruction.Offset);
            }
            RequireBlockMembership(source, metadata, function.Blocks.Count, membership);
            RequireReturnedRootRanges(source, function);
        }
        RequireInvocationPrelude(graph, program, body, planned, membership);
        if (membership.Count != function.Blocks.Count - 1 || metadata.SourceBlockCosts[0] != 0)
        {
            throw WarpPortableWordProgramIdentity.Invalid("A source body has unbound generated blocks or an incorrect prologue charge.");
        }
    }

    private static void RequireBlockMembership(WarpPortableWordSourceBlock source, WarpLogicalBodyMetadata metadata, int blockCount, HashSet<int> seen)
    {
        if (source.GeneratedBlocks.IsEmpty || source.GeneratedBlocks[0] != source.Block)
        {
            throw WarpPortableWordProgramIdentity.Invalid("A source instruction has no distinct original entry block.", source.Instruction.Offset);
        }
        foreach (int block in source.GeneratedBlocks)
        {
            if (block <= 0 || block >= blockCount || !seen.Add(block) || metadata.SourceBlockCosts[block] != (block == source.Block ? 1 : 0))
            {
                throw WarpPortableWordProgramIdentity.Invalid("Source and generated continuation block membership/charges differ.", source.Instruction.Offset);
            }
        }
    }

    private static void RequireReturnedRootRanges(WarpPortableWordSourceBlock source, WarpControlFlowFunction function)
    {
        Dictionary<int, WarpIrValueType> definitions = WarpControlFlowKernel.CollectBodyDefinitions(function.Blocks);
        foreach (WarpPortableWordTransientRoot root in source.ReturnedRoots)
        {
            for (int word = 0; word < 3; word++)
            {
                if (!definitions.TryGetValue(root.SsaWordOffset + word, out WarpIrValueType type) || type != WarpIrValueType.UInt32)
                {
                    throw WarpPortableWordProgramIdentity.Invalid("A pending returned owner exceeds its exact generated SSA word tuple.", source.Instruction.Offset);
                }
            }
        }
    }

    private static bool Same<T>(T first, T second) => WarpPortableSnapshotIdentity.Serialize(first).AsSpan().SequenceEqual(WarpPortableSnapshotIdentity.Serialize(second));

    private sealed partial class Builder
    {
        internal WarpPortableWordEntryProjection IdentityEntryProjection(IEnumerable<WarpPortableWordBody> mapped)
        {
            bodies.AddRange(mapped); return EntryProjection();
        }
    }
}
