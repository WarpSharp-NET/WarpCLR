using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class Builder
    {
        private void StoreFilterAlias(WarpControlFlowFunction function, WarpLogicalBodyMetadata description, WarpPortableWordBody body)
        {
            RequireBindingMutation(); ArgumentNullException.ThrowIfNull(body);
            WarpPortableWordBody original = plannedBodies.FirstOrDefault(candidate => candidate.Function == description.AliasOwnerFunction) ??
                throw new ArgumentException("A filter alias must name one captured original source owner.", nameof(description));
            if (description.RuntimeHelper || description.CountsSourceDepth || body.Function != function.Id + 1 ||
                body.AliasOwnerFunction != description.AliasOwnerFunction || body.AliasPrefixWords != description.AliasPrefixWords ||
                !string.Equals(body.MethodIdentity, original.MethodIdentity, StringComparison.Ordinal) ||
                description.AliasPrefixWords != original.StoragePrefixWords || body.StoragePrefixWords != original.StoragePrefixWords ||
                description.PrivateWordCount != body.PrivateWordCount || body.PrivateWordCount < original.StoragePrefixWords ||
                !body.Arguments.SequenceEqual(original.Arguments) || !body.Locals.SequenceEqual(original.Locals) || body.SourceBlocks.IsEmpty)
            {
                throw new ArgumentException("A filter alias must preserve exact argument/local storage, original owner and separate evaluation tail.", nameof(body));
            }
            ValidateAliasBlocks(function, description, body, original);
            if (aliasBodies.Any(existing => existing.Function == body.Function))
            {
                throw new ArgumentException("A filter alias source map is already installed.", nameof(body));
            }
            StoreRuntimeFunction(function, description); aliasBodies.Add(body);
        }

        private static void ValidateAliasBlocks(WarpControlFlowFunction function, WarpLogicalBodyMetadata description,
            WarpPortableWordBody body, WarpPortableWordBody original)
        {
            if (description.SourceBlockCosts.Count != function.Blocks.Count)
            {
                throw new ArgumentException("Every filter alias block requires an exact source-charge entry.", nameof(description));
            }
            var claimed = new HashSet<int>();
            foreach (WarpPortableWordSourceBlock block in body.SourceBlocks)
            {
                WarpPortableWordSourceBlock captured = original.SourceBlocks.FirstOrDefault(candidate => candidate.Instruction.Offset == block.Instruction.Offset) ??
                    throw new ArgumentException("A filter alias instruction is outside its captured source method.", nameof(body));
                if (!block.Instruction.ExceptionMemberships.Any(membership => membership.Role == WarpPortableExceptionRole.Filter) ||
                    block.Instruction != captured.Instruction || !block.Roots.SequenceEqual(captured.Roots) || block.Operation != captured.Operation ||
                    block.GeneratedBlocks.IsEmpty || block.GeneratedBlocks[0] != block.Block)
                {
                    throw new ArgumentException("A filter alias requires the exact verified filter instruction/effect/root mapping.", nameof(body));
                }
                foreach (int generated in block.GeneratedBlocks)
                {
                    if ((uint)generated >= (uint)function.Blocks.Count || !claimed.Add(generated) ||
                        description.SourceBlockCosts[generated] != (generated == block.Block ? 1 : 0))
                    {
                        throw new ArgumentException("Each filter block needs one exact original instruction and source charge.", nameof(body));
                    }
                }
            }
            if (function.Blocks.Any(block => !claimed.Contains(block.Id) && (block.Id != 0 || description.SourceBlockCosts[block.Id] != 0)))
            {
                throw new ArgumentException("A filter alias has an unmapped generated block or source charge.", nameof(body));
            }
        }
    }
}
