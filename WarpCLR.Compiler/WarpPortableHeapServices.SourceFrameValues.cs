using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint ReadSourceFrameValue(uint[] state, uint[] arena, uint context, uint frame, uint generation,
        uint byteOffset, uint byteSpan, uint elementType, uint scratchOffset)
    {
        if (Begin(arena, 52) != 0 || ValidateSourceFrameOwner(state, arena, context, frame, generation, byteOffset, byteSpan, elementType) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint words = (byteSpan + 3) >> 2;
        if (RequireScratch(arena, scratchOffset, words) != 0 || RequireSourceValueLease(arena, elementType) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint input = (uint)WarpLogicalMachineLayout.HeaderWords + (frame - 1) * state[WarpLogicalMachineLayout.FrameStrideOffset] + state[WarpLogicalMachineLayout.PrivateBaseOffset];
        uint output = arena[WarpPortableHeapLayout.ScratchStart] + scratchOffset;
        for (uint word = 0; word < words; word++) { arena[output + word] = 0; }
        for (uint index = 0; index < byteSpan; index++) { SourceWriteByte(arena, output, index, SourceStateReadByte(state, input, byteOffset + index)); }
        return ValidateSourceValueReferences(arena, elementType, scratchOffset);
    }

    public static uint WriteSourceFrameValue(uint[] state, uint[] arena, uint context, uint frame, uint generation,
        uint byteOffset, uint byteSpan, uint elementType, uint scratchOffset)
    {
        if (Begin(arena, 53) != 0 || ValidateSourceFrameOwner(state, arena, context, frame, generation, byteOffset, byteSpan, elementType) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireScratch(arena, scratchOffset, (byteSpan + 3) >> 2) != 0 || RequireSourceValueLease(arena, elementType) != 0 ||
            ValidateSourceValueReferences(arena, elementType, scratchOffset) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint output = (uint)WarpLogicalMachineLayout.HeaderWords + (frame - 1) * state[WarpLogicalMachineLayout.FrameStrideOffset] + state[WarpLogicalMachineLayout.PrivateBaseOffset];
        uint input = arena[WarpPortableHeapLayout.ScratchStart] + scratchOffset;
        for (uint index = 0; index < byteSpan; index++) { SourceStateWriteByte(state, output, byteOffset + index, SourceReadByte(arena, input, index)); }
        return 0;
    }

    private static uint ValidateSourceFrameOwner(uint[] state, uint[] arena, uint context, uint frame, uint generation,
        uint byteOffset, uint byteSpan, uint elementType)
    {
        uint fault = WarpPortableFrameServices.ValidateOwner(state, context, frame, generation, byteOffset, byteSpan, elementType);
        if (fault != 0) { return Fail(arena, fault == WarpPortableFrameServices.InvalidSpan ? WarpPortableHeapLayout.Bounds : WarpPortableHeapLayout.InvalidReference, frame, generation); }
        uint start = (uint)WarpLogicalMachineLayout.HeaderWords + (frame - 1) * state[WarpLogicalMachineLayout.FrameStrideOffset];
        return ValidateSourceFrameView(arena, state[start + WarpLogicalMachineLayout.FrameFunctionOffset],
            state[start + WarpLogicalMachineLayout.FramePrivateWordsOffset], byteOffset, byteSpan, elementType);
    }

    private static uint SourceStateReadByte(uint[] state, uint payload, uint offset) =>
        (state[payload + (offset >> 2)] >> (int)((offset & 3u) * 8)) & 255u;

    private static uint SourceStateWriteByte(uint[] state, uint payload, uint offset, uint value)
    {
        uint shift = (offset & 3u) * 8;
        uint word = payload + (offset >> 2);
        state[word] = (state[word] & ~(255u << (int)shift)) | ((value & 255u) << (int)shift); return 0;
    }
}
