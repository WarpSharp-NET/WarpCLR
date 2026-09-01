using WarpCLR.IR;

namespace WarpCLR.Backend.Cpu;

public sealed class WarpIntegerMapSemanticEmulator
{
    public uint[] Execute(
        WarpBackendArtifact artifact,
        WarpLinearKernel kernel,
        IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint>? scalarArguments = null)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(inputs);

        if (kernel.Reduction.HasValue)
        {
            throw new ArgumentException("A reduction kernel requires reduction execution.", nameof(kernel));
        }

        if (artifact.Format != WarpArtifactFormatCatalog.ForBackend(artifact.Backend))
        {
            throw new ArgumentException("The artifact format does not match its backend.", nameof(artifact));
        }

        if (!string.Equals(
                artifact.EntryPoint,
                WarpDeviceAbi.GetEntryPoint(kernel),
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The artifact entry point does not match the device ABI.", nameof(artifact));
        }

        scalarArguments ??= Array.Empty<uint>();
        int length = ValidateArguments(kernel, inputs, scalarArguments);

        var output = new uint[length];
        var values = new uint[inputs.Count];

        for (int workItem = 0; workItem < length; workItem++)
        {
            for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                values[inputIndex] = inputs[inputIndex][workItem];
            }

            output[workItem] = Evaluate(kernel, values, scalarArguments);
        }

        return output;
    }

    public uint ExecuteReduction(
        WarpBackendArtifact artifact,
        WarpLinearKernel kernel,
        IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint>? scalarArguments = null)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(inputs);
        if (!kernel.Reduction.HasValue)
        {
            throw new ArgumentException("A map kernel does not define a reduction.", nameof(kernel));
        }

        if (artifact.Format != WarpArtifactFormatCatalog.ForBackend(artifact.Backend) ||
            !string.Equals(
                artifact.EntryPoint,
                WarpDeviceAbi.GetEntryPoint(kernel),
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The artifact does not match the reduction kernel.", nameof(artifact));
        }

        scalarArguments ??= Array.Empty<uint>();
        int length = ValidateArguments(kernel, inputs, scalarArguments);
        uint result = WarpReductionContract.GetDescriptor(kernel.Reduction.Value).Identity;
        var values = new uint[inputs.Count];
        for (int workItem = 0; workItem < length; workItem++)
        {
            for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                values[inputIndex] = inputs[inputIndex][workItem];
            }

            uint value = Evaluate(kernel, values, scalarArguments);
            result = WarpReductionContract.Apply(kernel.Reduction.Value, result, value);
        }

        return result;
    }

    private static int ValidateArguments(
        WarpLinearKernel kernel,
        IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint> scalarArguments)
    {
        if (inputs.Count != kernel.InputBufferCount)
        {
            throw new ArgumentException("The input buffer count does not match the verified kernel.", nameof(inputs));
        }

        if (scalarArguments.Count != kernel.ScalarArgumentCount)
        {
            throw new ArgumentException("The scalar argument count does not match the verified kernel.", nameof(scalarArguments));
        }

        int length = inputs[0]?.Length
            ?? throw new ArgumentException("An input buffer cannot be null.", nameof(inputs));
        for (int inputIndex = 1; inputIndex < inputs.Count; inputIndex++)
        {
            uint[] input = inputs[inputIndex]
                ?? throw new ArgumentException("An input buffer cannot be null.", nameof(inputs));
            if (input.Length != length)
            {
                throw new ArgumentException("All input buffers must have the same length.", nameof(inputs));
            }
        }

        return length;
    }

    private static uint Evaluate(
        WarpLinearKernel kernel,
        IReadOnlyList<uint> inputs,
        IReadOnlyList<uint> scalarArguments)
    {
        var values = new uint[kernel.ValueCount];

        foreach (WarpIrInstruction instruction in kernel.Instructions)
        {
            uint left = instruction.Left < 0 ? 0 : values[instruction.Left];
            uint right = instruction.Right < 0 ? 0 : values[instruction.Right];

            values[instruction.Result] = instruction.OpCode switch
            {
                WarpIrOpCode.LoadInput => inputs[checked((int)instruction.Immediate)],
                WarpIrOpCode.LoadScalar => scalarArguments[checked((int)instruction.Immediate)],
                WarpIrOpCode.Constant => instruction.Immediate,
                WarpIrOpCode.BitwiseNot => ~left,
                WarpIrOpCode.Add => unchecked(left + right),
                WarpIrOpCode.Subtract => unchecked(left - right),
                WarpIrOpCode.Multiply => unchecked(left * right),
                WarpIrOpCode.BitwiseAnd => left & right,
                WarpIrOpCode.BitwiseOr => left | right,
                WarpIrOpCode.ExclusiveOr => left ^ right,
                WarpIrOpCode.ShiftLeft => left << (int)(right & 31),
                WarpIrOpCode.ShiftRightLogical => left >> (int)(right & 31),
                _ => throw new InvalidOperationException(
                    "The semantic emulator received an unregistered opcode."),
            };
        }

        return values[kernel.Result];
    }
}
