using System.Collections.Immutable;

namespace WarpCLR.Verifier;

internal sealed record WarpPortableTypedInstruction(
    int Offset, int NextOffset, short OpCode, bool Reachable,
    ImmutableArray<WarpPortableTypedValue> EntryStack,
    ImmutableArray<WarpPortableTypedValue> ExitStack,
    ImmutableArray<WarpPortableTypedSlot> EntryArguments,
    ImmutableArray<WarpPortableTypedSlot> EntryLocals,
    ImmutableArray<WarpPortableTypedExceptionMembership> ExceptionMemberships,
    ImmutableArray<int> Successors, ImmutableArray<int> ExceptionalSuccessors,
    ImmutableArray<int> UnwindRegions, ImmutableArray<WarpPortableTypedEffect> Effects,
    ImmutableArray<WarpPortableTypedFault> Faults,
    ImmutableArray<WarpPortableTypedRoot> Roots, string? MemoryType, int StorageBits,
    bool ReadOnlyAccess, string? RequiredIntrinsic)
{
    public WarpPortableTypedInitializerTrigger? InitializerTrigger { get; init; }
}
