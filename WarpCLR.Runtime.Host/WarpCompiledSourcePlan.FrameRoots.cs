using System.Collections.Immutable;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourcePlan
{
    private const string FrameRootSemantics = "warp.runtime-host.source-frame-roots/live-namespace-activation-exact-view-embedded-reference-alias-prefix/0.1";

    private int FrameRootReservation(WarpPortableWordBody body, WarpPortableWordSourceBlock block) =>
        checked(block.Roots.Sum(root => root.Source.IsInteriorOwner ?
            Math.Max(1, RootPointee(body, block, root).ManagedRootByteOffsets.Length) : 1) +
            block.ReturnedRoots.Sum(root => root.IsInteriorOwner ?
                Math.Max(1, ReturnedPointee(block).ManagedRootByteOffsets.Length) : 1));

    private WarpPortableTypedType RootPointee(WarpPortableWordBody body, WarpPortableWordSourceBlock block, WarpPortableWordRoot root)
    {
        WarpPortableTypedType pointer = root.Source.Storage switch
        {
            "argument" => body.Arguments[root.Source.Slot].Type,
            "local" => body.Locals[root.Source.Slot].Type,
            "stack" => rootTypes[block.Instruction.EntryStack[root.Source.Slot].TypeIdentity],
            _ => throw new InvalidOperationException("An interior source root has no exact captured pointer slot."),
        };
        return Pointee(pointer);
    }

    private WarpPortableTypedType ReturnedPointee(WarpPortableWordSourceBlock block) =>
        Pointee(rootTypes[block.Instruction.ExitStack[^1].TypeIdentity]);

    private WarpPortableTypedType Pointee(WarpPortableTypedType pointer) =>
        pointer.Category == WarpPortableStackCategory.ManagedByref && pointer.ElementType is { } element ? rootTypes[element] :
            throw new InvalidOperationException("An interior source root must retain its exact managed pointer type.");

    private uint ReadPrivateWord(uint[] state, int frame, int word)
    {
        int function = checked((int)state[frame + WarpLogicalMachineLayout.FrameFunctionOffset]);
        int count = Layout.GetPrivateWordCount(function);
        if ((uint)word >= (uint)count) { throw new InvalidOperationException("A precise root extends beyond its captured private bank."); }
        int alias = Layout.GetAliasOwnerFunction(function);
        if (alias != -1 && word < Layout.GetAliasPrefixWords(function))
        {
            uint ownerDepth = state[frame + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset];
            int depth = checked((frame - WarpLogicalMachineLayout.HeaderWords) / Layout.FrameWords + 1);
            if (ownerDepth == 0 || ownerDepth >= depth)
            { throw new InvalidOperationException("A precise alias root has no live original prefix owner."); }
            int owner = checked(WarpLogicalMachineLayout.HeaderWords + ((int)ownerDepth - 1) * Layout.FrameWords);
            if (state[owner + WarpLogicalMachineLayout.FrameFunctionOffset] != alias ||
                state[owner + WarpLogicalMachineLayout.FrameActivationOffset] != state[frame + WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset] ||
                word >= Layout.GetPrivateWordCount(alias))
            { throw new InvalidOperationException("A precise alias root changed its exact original prefix activation."); }
            frame = owner;
        }
        return state[checked(frame + Layout.PrivateOffset + word)];
    }

    private void CopyFrameInterior(uint[] state, int sourceFrame, ReadOnlySpan<uint> capability, WarpPortableTypedType pointee,
        ImmutableArray<WarpPortableTypedProvenance> provenance, uint[] roots, ref int next)
    {
        if (capability.Length != 6) { throw new InvalidOperationException("A frame interior root requires all six capability words."); }
        if (capability.IndexOfAnyExcept(0u) < 0) { return; }
        int owner = RequireRootFrame(state, sourceFrame, capability, pointee);
        bool matched = false;
        foreach (WarpPortableTypedProvenance origin in provenance)
        {
            matched |= MatchesRootOrigin(state, owner, capability, origin);
        }
        if (!matched)
        { throw new InvalidOperationException("The frame root differs from its captured source provenance."); }
        foreach (int offset in pointee.ManagedRootByteOffsets)
        {
            if (offset < 0 || offset > pointee.ByteSize - 12)
            { throw new InvalidOperationException("An embedded frame owner exceeds its exact captured pointee view."); }
            uint start = checked(capability[3] + (uint)offset);
            uint[] tuple = new uint[3];
            for (uint word = 0; word < 3; word++)
            {
                uint value = 0;
                for (uint part = 0; part < 4; part++)
                {
                    uint address = checked(start + word * 4 + part);
                    uint stored = ReadPrivateWord(state, owner, checked((int)(address / 4)));
                    value |= ((stored >> checked((int)(address % 4 * 8))) & 255) << checked((int)(part * 8));
                }
                tuple[word] = value;
            }
            CopyRoot(tuple, roots, ref next);
        }
    }

    private int RequireRootFrame(uint[] state, int sourceFrame, ReadOnlySpan<uint> capability, WarpPortableTypedType pointee)
    {
        uint depth = state[WarpLogicalMachineLayout.DepthOffset];
        uint sourceDepth = checked((uint)((sourceFrame - WarpLogicalMachineLayout.HeaderWords) / Layout.FrameWords + 1));
        if (!Layout.HasValidRuntimeHeader(state) || state.Length != Layout.GetStateWords(MaximumDepth) ||
            capability[0] != state[WarpLogicalMachineLayout.OwnerContextOffset] || capability[0] == 0 ||
            capability[1] == 0 || capability[1] > depth || capability[1] > sourceDepth || capability[2] == 0 ||
            capability[4] != pointee.ByteSize || capability[5] != TypeSchema.TypeId(pointee.Identity))
        { throw new InvalidOperationException("The precise frame owner changed its namespace, live depth, activation, or pointee type."); }
        int owner = checked(WarpLogicalMachineLayout.HeaderWords + ((int)capability[1] - 1) * Layout.FrameWords);
        uint function = state[owner + WarpLogicalMachineLayout.FrameFunctionOffset];
        uint parent = capability[1] == 1 ? 0 : state[owner - Layout.FrameWords + WarpLogicalMachineLayout.FrameActivationOffset];
        uint byteOffset = capability[3], byteSpan = capability[4], elementType = capability[5];
        if (function > int.MaxValue || !frameBodies.TryGetValue(function, out WarpPortableSourceFrameBody? body) ||
            state[owner + WarpLogicalMachineLayout.FrameActivationOffset] != capability[2] ||
            !Layout.HasValidFrameIdentity(state, owner, (int)function, parent) ||
            state[owner + WarpLogicalMachineLayout.FramePrivateWordsOffset] != body.PrivateWords ||
            capability[3] > body.PrivateWords * 4 || capability[4] > body.PrivateWords * 4 - capability[3] ||
            !body.Views.Any(view => view.ByteOffset == byteOffset && view.ByteSpan == byteSpan && view.ElementType == elementType))
        { throw new InvalidOperationException("The precise frame owner is stale or outside its exact compiled byte view."); }
        return owner;
    }

    private bool MatchesRootOrigin(uint[] state, int frame, ReadOnlySpan<uint> capability, WarpPortableTypedProvenance origin)
    {
        if (origin.ByteLength != capability[4]) { return false; }
        if (origin.Kind == WarpPortableProvenanceKind.Argument) { return true; }
        if (origin.Kind is not (WarpPortableProvenanceKind.FrameArgument or WarpPortableProvenanceKind.FrameLocal)) { return false; }
        int function = checked((int)state[frame + WarpLogicalMachineLayout.FrameFunctionOffset]);
        if (!bodies.TryGetValue(function, out WarpPortableWordBody? body) ||
            !string.Equals(body.MethodIdentity, origin.OwnerMethod, StringComparison.Ordinal)) { return false; }
        ImmutableArray<WarpPortableWordStorageSlot> slots = origin.Kind == WarpPortableProvenanceKind.FrameArgument ? body.Arguments : body.Locals;
        return (uint)origin.OwnerIndex < (uint)slots.Length && origin.ByteOffset >= 0 &&
            string.Equals(slots[origin.OwnerIndex].Type.Identity, origin.OwnerType, StringComparison.Ordinal) &&
            capability[3] == checked((uint)(slots[origin.OwnerIndex].WordOffset * 4 + origin.ByteOffset));
    }
}
