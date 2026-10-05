using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableExceptionSourceBinding
{
    private WarpConditionalBranchTerminator EndFilter(WarpPortableWordInstructionContext context)
    {
        if (filtering < 0 || context.Instruction.EntryStack.Length != 1 || context.Instruction.EntryStack[0].Category != WarpPortableStackCategory.I4 ||
            !context.Instruction.ExceptionMemberships.Any(member => member.Role == WarpPortableExceptionRole.Filter))
        { throw new WarpVerificationException("WRPCLR2300", "An endfilter requires one exact captured filter decision.", context.Instruction.Offset); }
        int status = Call(context, nameof(WarpPortableExceptionServices.RefreshContinuation), Ticket(context));
        int refreshed = context.ReserveGeneratedBlock();
        context.EmitGeneratedBlock(refreshed, PrepareFilterEnd);
        return ContinueOrReject(context, status, refreshed);
    }

    private WarpConditionalBranchTerminator PrepareFilterEnd(WarpPortableWordInstructionContext context)
    {
        int record = ActiveRecord(context);
        int status = Call(context, nameof(WarpPortableExceptionServices.PrepareFilterEnd), [.. Ticket(context),
            ArenaLoad(context, Add(context, record, WarpPortableExceptionLayout.RaiseGeneration)), context.LoadStackValue(0)[0],
            CurrentFunction(context), context.Constant((uint)context.Instruction.Offset), context.Constant(unchecked((ushort)context.Instruction.OpCode))]);
        int commit = context.ReserveGeneratedBlock();
        context.EmitGeneratedBlock(commit, stage =>
        {
            stage.Call(filtering, [stage.Emit(WarpManagedInvocationOpCode.LoadLogicalWorker), stage.Constant(0),
                ArenaLoad(stage, Add(stage, ActiveRecord(stage), WarpPortableExceptionLayout.RaiseGeneration))]);
            return Reject(stage);
        });
        return ContinueOrReject(context, status, commit);
    }

    private WarpConditionalBranchTerminator IsInstance(WarpPortableWordInstructionContext context)
    {
        if (context.SourceInstruction.Type is not { } type || context.Instruction.EntryStack.IsEmpty ||
            context.Instruction.EntryStack[^1].Category != WarpPortableStackCategory.Reference)
        { throw new WarpVerificationException("WRPCLR2300", "The filter type test requires its exact captured reference and closed TypeID.", context.Instruction.Offset); }
        int[] owner = context.LoadStackValue(context.Instruction.EntryStack.Length - 1).ToArray();
        int status = Call(context, nameof(WarpPortableHeapServices.IsInstance), [.. owner, context.Constant(schema.TypeId(type))]);
        int complete = context.ReserveGeneratedBlock();
        context.EmitGeneratedBlock(complete, stage =>
        {
            int accepted = ArenaLoad(stage, stage.Constant(WarpPortableHeapLayout.Result));
            int[] value = stage.LoadStackValue(stage.Instruction.EntryStack.Length - 1).ToArray();
            int offset = stage.StackWordOffset(stage.Instruction.EntryStack.Length - 1);
            for (int index = 0; index < 3; index++)
            {
                int selected = stage.Emit(WarpIrOpCode.Select, accepted, value[index], third: stage.Constant(0));
                stage.StorePrivateWord(offset + index, selected);
            }
            return stage.Next();
        });
        return ContinueOrReject(context, status, complete);
    }

    private static ImmutableArray<WarpPortableWordBody> InstallAliases(WarpPortableWordBindingPreparation context)
    {
        var result = ImmutableArray.CreateBuilder<WarpPortableWordBody>();
        foreach (WarpPortableWordBody body in context.SourceBodies)
        {
            WarpPortableMethodGraphMethod method = context.Graph.Methods.First(method => string.Equals(method.Identity, body.MethodIdentity, StringComparison.Ordinal));
            for (int region = 0; region < method.ExceptionRegions.Length; region++)
            {
                if (method.ExceptionRegions[region].Kind != 1) { continue; }
                WarpPortableWordSourceBlock[] filter = body.SourceBlocks.Where(block => block.Instruction.ExceptionMemberships.Any(member =>
                    member.Region == region && member.Role == WarpPortableExceptionRole.Filter)).ToArray();
                string identity = SourceSemantics + "/filter/" + body.Function + "/" + region;
                int function = context.ReserveFunction(identity);
                WarpControlFlowFunction original = context.CompiledSourceFunctions.First(candidate => candidate.Id == body.Function - 1);
                Dictionary<int, int> mapping = filter.SelectMany(block => block.GeneratedBlocks).Distinct().Order().Select((block, index) =>
                    (block, target: index + 1)).ToDictionary(pair => pair.block, pair => pair.target);
                Dictionary<int, int> values = WarpPortableExceptionFilterClone.Values(original, mapping);
                ImmutableArray<WarpPortableWordSourceBlock> sources = filter.Select(block => block with
                { Block = mapping[block.Block], GeneratedBlocks = block.GeneratedBlocks.Select(block => mapping[block]).ToImmutableArray(),
                    ReturnedRoots = block.ReturnedRoots.Select(root => root with { SsaWordOffset = values[root.SsaWordOffset] }).ToImmutableArray() }).ToImmutableArray();
                List<WarpBasicBlock> blocks = [new(0, [], [], new WarpBranchTerminator(new(mapping[filter[0].Block], [])))];
                foreach ((int source, int target) in mapping.OrderBy(pair => pair.Value))
                {
                    WarpBasicBlock old = original.Blocks[source];
                    blocks.Add(WarpPortableExceptionFilterClone.Block(old, target, mapping, values, body.Function, function + 1));
                }
                WarpPortableWordBody alias = body with { Function = function + 1, SourceBlocks = sources,
                    AliasOwnerFunction = body.Function, AliasPrefixWords = body.StoragePrefixWords };
                int[] costs = new int[blocks.Count]; foreach (WarpPortableWordSourceBlock block in sources) { costs[block.Block] = 1; }
                context.InstallFilterAlias(new(function, identity, 0, blocks), new(body.PrivateWordCount, false, costs,
                    countsSourceDepth: false, aliasOwnerFunction: body.Function, aliasPrefixWords: body.StoragePrefixWords), alias);
                result.Add(alias);
            }
        }
        return result.ToImmutable();
    }

}
