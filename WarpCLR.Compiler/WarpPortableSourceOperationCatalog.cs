using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static class WarpPortableSourceOperationCatalog
{
    internal static WarpPortableSourceOperationMetadata Describe(string methodIdentity, string operation,
        WarpPortableTypedInstruction instruction, IReadOnlyDictionary<string, WarpPortableTypedType> types)
    {
        bool write = instruction.Effects.Contains(WarpPortableTypedEffect.WriteMemory);
        bool read = instruction.Effects.Contains(WarpPortableTypedEffect.ReadMemory);
        ImmutableArray<int> ownerOffsets = (write || read) && instruction.MemoryType is { } memory ? types[memory].ManagedRootByteOffsets : [];
        ImmutableArray<WarpPortableTypedProvenance> provenance = Destination(operation, instruction);
        bool privateStore = operation.StartsWith("stloc", StringComparison.Ordinal) || operation.StartsWith("starg", StringComparison.Ordinal);
        bool privateOwnerCopy = privateStore && !instruction.EntryStack.IsEmpty &&
            (instruction.EntryStack[^1].Category == WarpPortableStackCategory.Reference || !types[instruction.EntryStack[^1].TypeIdentity].ManagedRootByteOffsets.IsEmpty);
        bool caller = provenance.Any(origin => origin.Kind == WarpPortableProvenanceKind.Argument ||
            origin.Kind is WarpPortableProvenanceKind.FrameArgument or WarpPortableProvenanceKind.FrameLocal && !string.Equals(origin.OwnerMethod, methodIdentity, StringComparison.Ordinal));
        bool inlineValue = operation is "ldfld" && instruction.EntryStack[^1].Category == WarpPortableStackCategory.Value;
        return new(write && !ownerOffsets.IsEmpty || privateOwnerCopy, read && !ownerOffsets.IsEmpty,
            !ownerOffsets.IsEmpty && !inlineValue, caller, ownerOffsets, provenance);
    }

    private static ImmutableArray<WarpPortableTypedProvenance> Destination(string operation, WarpPortableTypedInstruction instruction)
    {
        int slot = operation is "initobj" ? instruction.EntryStack.Length - 1 :
            operation is "stobj" or "cpobj" or "stfld" || operation.StartsWith("stind", StringComparison.Ordinal) ? instruction.EntryStack.Length - 2 :
            operation is "ldobj" or "ldfld" || operation.StartsWith("ldind", StringComparison.Ordinal) ? instruction.EntryStack.Length - 1 : -1;
        return slot >= 0 && instruction.EntryStack[slot].Category == WarpPortableStackCategory.ManagedByref ? instruction.EntryStack[slot].Provenance : [];
    }
}
