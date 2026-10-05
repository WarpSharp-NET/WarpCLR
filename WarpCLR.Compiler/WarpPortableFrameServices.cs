using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpPortableFrameServices
{
    internal const string Semantics = "warp.portable-byref/frame-activation-byte-owner-span/0.1";
    internal const uint InvalidOwner = 1;
    internal const uint InvalidSpan = 2;
    internal const uint InvalidType = 3;

    public static uint ValidateOwner(uint[] state, uint context, uint frame, uint generation,
        uint byteOffset, uint byteSpan, uint typeId)
    {
        if ((uint)state.Length < (uint)WarpLogicalMachineLayout.HeaderWords) { return InvalidOwner; }
        uint fault = CheckOwner(state, context, frame, generation);
        if (fault != 0) { return fault; }
        uint start = (uint)WarpLogicalMachineLayout.HeaderWords + (frame - 1) * state[WarpLogicalMachineLayout.FrameStrideOffset];
        uint words = state[start + (uint)WarpLogicalMachineLayout.FramePrivateWordsOffset];
        if (words > uint.MaxValue / 4 || byteSpan == 0 || byteOffset > words * 4 || byteSpan > words * 4 - byteOffset)
        {
            return InvalidSpan;
        }
        return typeId == 0 ? InvalidType : 0;
    }

    public static uint ReadByte(uint[] state, uint context, uint frame, uint generation,
        uint byteOffset, uint byteSpan, uint typeId, uint relativeByte)
    {
        uint fault = Validate(state, context, frame, generation, byteOffset, byteSpan, typeId, relativeByte);
        if (fault != 0) { return fault; }
        uint address = (uint)WarpLogicalMachineLayout.HeaderWords + (frame - 1) * state[WarpLogicalMachineLayout.FrameStrideOffset] +
            state[WarpLogicalMachineLayout.PrivateBaseOffset] + ((byteOffset + relativeByte) >> 2);
        uint shift = ((byteOffset + relativeByte) & 3) * 8;
        state[WarpLogicalMachineLayout.InteriorResultOffset] = (state[address] >> (int)shift) & 255;
        return 0;
    }

    public static uint WriteByte(uint[] state, uint context, uint frame, uint generation,
        uint byteOffset, uint byteSpan, uint typeId, uint relativeByte, uint value)
    {
        uint fault = Validate(state, context, frame, generation, byteOffset, byteSpan, typeId, relativeByte);
        if (fault != 0) { return fault; }
        uint address = (uint)WarpLogicalMachineLayout.HeaderWords + (frame - 1) * state[WarpLogicalMachineLayout.FrameStrideOffset] +
            state[WarpLogicalMachineLayout.PrivateBaseOffset] + ((byteOffset + relativeByte) >> 2);
        uint shift = ((byteOffset + relativeByte) & 3) * 8;
        state[address] = (state[address] & ~(255u << (int)shift)) | ((value & 255) << (int)shift);
        state[WarpLogicalMachineLayout.InteriorResultOffset] = value & 255;
        return 0;
    }

    private static uint Validate(uint[] state, uint context, uint frame, uint generation,
        uint byteOffset, uint byteSpan, uint typeId, uint relativeByte)
    {
        if ((uint)state.Length < (uint)WarpLogicalMachineLayout.HeaderWords) { return InvalidOwner; }
        state[WarpLogicalMachineLayout.InteriorResultOffset] = 0;
        uint fault = ValidateOwner(state, context, frame, generation, byteOffset, byteSpan, typeId);
        if (fault == 0 && relativeByte >= byteSpan) { fault = InvalidSpan; }
        state[WarpLogicalMachineLayout.InteriorFaultOffset] = fault;
        return fault;
    }

    private static uint CheckOwner(uint[] state, uint context, uint frame, uint generation)
    {
        uint stride = state[WarpLogicalMachineLayout.FrameStrideOffset];
        uint privateOffset = state[WarpLogicalMachineLayout.PrivateBaseOffset];
        if (context == 0 || context != state[WarpLogicalMachineLayout.OwnerContextOffset] || frame == 0 ||
            frame > state[WarpLogicalMachineLayout.DepthOffset] || generation == 0 ||
            stride < (uint)WarpLogicalMachineLayout.FrameHeaderWords || privateOffset < (uint)WarpLogicalMachineLayout.FrameHeaderWords || privateOffset > stride ||
            frame > WarpPortableInteger32.DivideUnsigned((uint)state.Length - (uint)WarpLogicalMachineLayout.HeaderWords, stride))
        {
            return InvalidOwner;
        }
        uint start = (uint)WarpLogicalMachineLayout.HeaderWords + (frame - 1) * stride;
        return state[start + (uint)WarpLogicalMachineLayout.FrameActivationOffset] != generation ||
            state[start + (uint)WarpLogicalMachineLayout.FramePrivateWordsOffset] > stride - privateOffset ? InvalidOwner : 0;
    }
}
