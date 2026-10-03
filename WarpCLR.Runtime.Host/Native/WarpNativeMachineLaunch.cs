using System.Runtime.InteropServices;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct WarpNativeMachineLaunch(uint GridX, uint WorkgroupSize, ulong BytesRequired)
{
    public static WarpNativeMachineLaunch Admit(
        WarpNativeImage image,
        uint[] states,
        IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint> scalars,
        int itemCount,
        int inputBase,
        int maximumCallDepth,
        int quantum)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(scalars);
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        ArgumentOutOfRangeException.ThrowIfNegative(inputBase);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCallDepth);
        WarpLogicalMachineLayout layout = image.MachineLayout
            ?? throw new WarpHostException("WRPNATIVE1008", "The loaded module does not implement the resumable logical CLR ABI.");
        if (quantum < layout.MaximumBlockCost)
        {
            throw new WarpHostException("WRPNATIVE1004", "The logical quantum cannot be smaller than one block's maximum charged cost.");
        }

        if (inputs.Count != image.InputBufferCount || scalars.Count != image.ScalarArgumentCount)
        {
            throw new WarpHostException("WRPNATIVE1004", "The logical-machine arguments do not match the verified entry point.");
        }

        WarpNativeArgumentLayout.Validate(image.Target, inputs.Count, scalars.Count, machine: true);

        int stride = layout.GetStateWords(maximumCallDepth);
        if (states.Length != itemCount * (long)stride)
        {
            throw new WarpHostException("WRPNATIVE1004", "The logical-state buffer does not match the admitted worker count and stack depth.");
        }

        ulong bytes = checked((ulong)Math.Max(states.Length, 1) * sizeof(uint));
        foreach (uint[]? input in inputs)
        {
            if (input is null || inputBase > input.Length || itemCount > input.Length - inputBase)
            {
                throw new WarpHostException("WRPNATIVE1004", "The logical worker input range is outside a bound input buffer.");
            }

            bytes = checked(bytes + (ulong)Math.Max(input.Length, 1) * sizeof(uint));
        }

        ValidateContinuations(layout, states, itemCount, maximumCallDepth, stride);
        uint workgroup = WarpDeviceAbi.IntegerMapWorkgroupSize;
        ulong grid = ((ulong)itemCount + workgroup - 1) / workgroup;
        WarpNativeTarget target = image.Target;
        if (workgroup > target.MaxWorkgroupSize || grid > target.MaxGridX || bytes > target.GlobalMemoryBytes)
        {
            throw new WarpHostException("WRPNATIVE1005", "The logical stack, bound inputs, or launch geometry exceed device resources.");
        }

        return new WarpNativeMachineLaunch((uint)grid, workgroup, bytes);
    }

    private static void ValidateContinuations(WarpLogicalMachineLayout layout, uint[] states, int itemCount, int maximumCallDepth, int stride)
    {
        for (int worker = 0; worker < itemCount; worker++)
        {
            int offset = worker * stride;
            uint status = states[offset + WarpLogicalMachineLayout.StatusOffset];
            uint depth = states[offset + WarpLogicalMachineLayout.DepthOffset];
            if (status > WarpLogicalMachineLayout.Faulted || depth == 0 || depth > maximumCallDepth ||
                states[offset + WarpLogicalMachineLayout.RemainingStepsHighOffset] > int.MaxValue)
            {
                throw new WarpHostException("WRPNATIVE1004", "The supplied logical state has an invalid status or stack depth.");
            }

            ValidateFrames(layout, states, offset, depth, "WRPNATIVE1004");
        }
    }

    internal static void ValidateReturnedStates(WarpLogicalMachineLayout layout, uint[] states, int itemCount, int maximumCallDepth)
    {
        int stride = layout.GetStateWords(maximumCallDepth);
        if (states.Length != itemCount * (long)stride)
        {
            throw new WarpHostException("WRPNATIVE1007", "The native logical-state readback has an invalid worker count or stride.");
        }

        for (int worker = 0; worker < itemCount; worker++)
        {
            int offset = worker * stride;
            uint status = states[offset + WarpLogicalMachineLayout.StatusOffset];
            uint depth = states[offset + WarpLogicalMachineLayout.DepthOffset];
            uint faultKind = states[offset + WarpLogicalMachineLayout.FaultKindOffset];
            if (status > WarpLogicalMachineLayout.Faulted || depth == 0 || depth > maximumCallDepth ||
                states[offset + WarpLogicalMachineLayout.RemainingStepsHighOffset] > int.MaxValue ||
                (status == WarpLogicalMachineLayout.Completed && depth != 1) ||
                (status != WarpLogicalMachineLayout.Faulted && faultKind != 0) ||
                (status == WarpLogicalMachineLayout.Faulted &&
                    faultKind is not WarpLogicalMachineLayout.StepLimitFault and not WarpLogicalMachineLayout.CallDepthFault))
            {
                throw new WarpHostException("WRPNATIVE1007", "The native logical worker returned malformed status, depth, or fault metadata.");
            }

            ValidateFrames(layout, states, offset, depth, "WRPNATIVE1007");
            if (status == WarpLogicalMachineLayout.Faulted)
            {
                int frame = offset + WarpLogicalMachineLayout.HeaderWords + checked((int)(depth - 1) * layout.FrameWords);
                uint pc = states[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset];
                WarpLogicalMachineNode node = layout.Nodes[checked((int)pc)];
                if (states[offset + WarpLogicalMachineLayout.FaultFunctionOffset] != node.Function ||
                    states[offset + WarpLogicalMachineLayout.FaultBlockOffset] != node.Block)
                {
                    throw new WarpHostException("WRPNATIVE1007", "The native fault location does not match its verified logical continuation.");
                }
            }
        }
    }

    private static void ValidateFrames(WarpLogicalMachineLayout layout, uint[] states, int offset, uint depth, string errorCode)
    {
        for (int frame = 0; frame < depth; frame++)
        {
            int frameOffset = offset + WarpLogicalMachineLayout.HeaderWords + frame * layout.FrameWords;
            uint function = states[frameOffset + WarpLogicalMachineLayout.FrameFunctionOffset];
            uint pc = states[frameOffset + WarpLogicalMachineLayout.FrameProgramCounterOffset];
            if ((frame == 0 && function != 0) || pc >= layout.Nodes.Count || function > layout.Kernel.Functions.Count ||
                function != layout.Nodes[(int)pc].Function)
            {
                throw new WarpHostException(errorCode, "The logical continuation does not reference a verified function/program counter.");
            }

            if (frame != 0)
            {
                int callerOffset = frameOffset - layout.FrameWords;
                uint callerFunction = states[callerOffset + WarpLogicalMachineLayout.FrameFunctionOffset];
                int callerValues = callerFunction == 0 ? layout.Kernel.ValueCount : layout.Kernel.Functions[checked((int)callerFunction - 1)].ValueCount;
                if (states[frameOffset + WarpLogicalMachineLayout.FrameReturnValueOffset] >= callerValues)
                {
                    throw new WarpHostException(errorCode, "The logical continuation has an invalid caller return destination.");
                }
            }
        }
    }
}
