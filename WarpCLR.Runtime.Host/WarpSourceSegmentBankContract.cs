using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal static class WarpSourceSegmentBankContract
{
    internal static void Validate(WarpLogicalMachineLayout layout, ReadOnlySpan<uint> state, int maximumDepth)
    {
        if (state.Length != layout.GetStateWords(maximumDepth) || !layout.HasValidRuntimeHeader(state) ||
            state[WarpLogicalMachineLayout.StatusOffset] > WarpLogicalMachineLayout.Faulted ||
            state[WarpLogicalMachineLayout.RemainingStepsHighOffset] > int.MaxValue ||
            state[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Faulted && state[WarpLogicalMachineLayout.FaultKindOffset] != 0 ||
            state[WarpLogicalMachineLayout.DepthOffset] == 0 || state[WarpLogicalMachineLayout.DepthOffset] > layout.GetPhysicalFrameCapacity(maximumDepth))
        { throw new InvalidOperationException("The candidate has no exact admitted physical bank/header."); }
        uint parentActivation = 0; int sourceDepth = 0;
        for (int depth = 0; depth < state[WarpLogicalMachineLayout.DepthOffset]; depth++)
        {
            int frame = checked(WarpLogicalMachineLayout.HeaderWords + depth * layout.FrameWords);
            uint function = state[frame + WarpLogicalMachineLayout.FrameFunctionOffset];
            uint pc = state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset];
            if (depth == 0 && function != 0 || depth != 0 && function == 0 ||
                function > layout.Kernel.Functions.Count || pc >= layout.Nodes.Count || layout.Nodes[(int)pc].Function != function ||
                !layout.HasValidFrameIdentity(state, frame, (int)function, parentActivation))
            { throw new InvalidOperationException("Every live frame needs exact function/PC, private width and nonreused activation provenance."); }
            parentActivation = state[frame + WarpLogicalMachineLayout.FrameActivationOffset];
            if (depth != 0 && layout.GetAliasOwnerFunction((int)function) == -1) { ValidateReturn(layout, state, frame, (int)function); }
            if (layout.CountsSourceDepth((int)function)) { sourceDepth++; }
        }
        if (sourceDepth > maximumDepth || state[WarpLogicalMachineLayout.LogicalDepthOffset] != sourceDepth)
        { throw new InvalidOperationException("The candidate logical source depth differs from its complete physical census."); }
    }

    private static void ValidateReturn(WarpLogicalMachineLayout layout, ReadOnlySpan<uint> state, int frame, int function)
    {
        uint caller = state[frame - layout.FrameWords + WarpLogicalMachineLayout.FrameFunctionOffset];
        int values = caller == 0 ? layout.Kernel.ValueCount : layout.Kernel.Functions[(int)caller - 1].ValueCount;
        uint count = (uint)layout.Kernel.Functions[function - 1].ResultWordCount;
        uint destination = state[frame + WarpLogicalMachineLayout.FrameReturnValueOffset];
        if (state[frame + WarpLogicalMachineLayout.FrameReturnWordCountOffset] != count || count != 0 && (destination >= values || count > values - destination))
        { throw new InvalidOperationException("A helper/source return must target its exact complete caller SSA tuple."); }
    }
}
