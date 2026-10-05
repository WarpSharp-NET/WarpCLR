using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class Builder
    {
        private WarpPortableWordEntryProjection EntryProjection()
        {
            WarpPortableTypedMethod entry = methods[graph.EntryIdentity];
            WarpPortableWordBody body = bodies.First(item => string.Equals(item.MethodIdentity, graph.EntryIdentity, StringComparison.Ordinal));
            WarpPortableTypedInstruction initial = entry.Instructions.First(instruction => instruction.Reachable);
            ImmutableArray<WarpPortableWordTransientRoot> inputs = initial.Roots.Where(root => root.Storage is "argument")
                .Select(root => new WarpPortableWordTransientRoot(body.Arguments[root.Slot].WordOffset + root.WordOffset, root.IsInteriorOwner, root.Provenance)).ToImmutableArray();
            WarpPortableTypedType result = types[entry.ReturnType];
            bool interior = result.Category == WarpPortableStackCategory.ManagedByref;
            ImmutableArray<WarpPortableTypedProvenance> provenance = interior ? entry.ReturnSummary.Origins : [];
            int words = body.Arguments.Sum(argument => argument.Type.WordCount);
            return new(result, inputs, result.ManagedRootByteOffsets.Select(offset => new WarpPortableWordTransientRoot(words + offset / 4, interior, provenance)).ToImmutableArray(),
                result.ManagedRootByteOffsets.Select(offset => new WarpPortableWordResultRoot(offset / 4, interior, provenance)).ToImmutableArray());
        }
    }

    private sealed partial class MethodLowerer
    {
        private void RecordReturnedRoots(WarpPortableTypedInstruction instruction, int resultWord, WarpPortableTypedType type)
        {
            bool interior = type.Category == WarpPortableStackCategory.ManagedByref;
            ImmutableArray<WarpPortableTypedProvenance> provenance = interior ? instruction.ExitStack[^1].Provenance : [];
            returnedRoots.Add(instruction.Offset, type.ManagedRootByteOffsets.Select(offset =>
                new WarpPortableWordTransientRoot(resultWord + offset / 4, interior, provenance)).ToImmutableArray());
        }

        private ImmutableArray<WarpPortableWordRoot> ProjectRoots(WarpPortableTypedInstruction instruction)
        {
            var result = ImmutableArray.CreateBuilder<WarpPortableWordRoot>();
            foreach (WarpPortableTypedRoot root in instruction.Roots)
            {
                (int offset, int words) = root.Storage switch
                {
                    "argument" when (uint)root.Slot < (uint)arguments.Length => (arguments[root.Slot].WordOffset, arguments[root.Slot].Type.WordCount),
                    "local" when (uint)root.Slot < (uint)locals.Length => (locals[root.Slot].WordOffset, locals[root.Slot].Type.WordCount),
                    "stack" when (uint)root.Slot < (uint)instruction.EntryStack.Length => (StackWord(instruction, root.Slot), instruction.EntryStack[root.Slot].WordCount),
                    _ => throw Error("A verified root has an invalid source storage slot.", instruction.Offset),
                };
                if (root.WordOffset < 0 || root.WordOffset > words - 3)
                {
                    throw Error("A verified managed owner root extends beyond its exact private slot.", instruction.Offset);
                }
                result.Add(new(checked(offset + root.WordOffset), root));
            }
            var declared = result.ToDictionary(root => root.PrivateWordOffset);
            AddDeclaredRoots(declared, arguments, instruction.EntryArguments, "argument");
            AddDeclaredRoots(declared, locals, instruction.EntryLocals, "local");
            return declared.Values.OrderBy(root => root.PrivateWordOffset).ThenBy(root => root.Source.Storage, StringComparer.Ordinal).ToImmutableArray();
        }

        private static void AddDeclaredRoots(Dictionary<int, WarpPortableWordRoot> roots,
            ImmutableArray<WarpPortableWordStorageSlot> slots, ImmutableArray<WarpPortableTypedSlot> state, string storage)
        {
            foreach (WarpPortableWordStorageSlot slot in slots)
            {
                bool interior = slot.Type.Category == WarpPortableStackCategory.ManagedByref;
                ImmutableArray<WarpPortableTypedProvenance> provenance = interior ? state[slot.Index].Value.Provenance : [];
                foreach (int byteOffset in slot.Type.ManagedRootByteOffsets)
                {
                    int word = byteOffset / 4;
                    roots.TryAdd(slot.WordOffset + word, new(slot.WordOffset + word, new(storage, slot.Index, word, interior, provenance)));
                }
            }
        }
    }
}
