using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed class WarpIntegerMapSemanticEmulator
{
    public uint[] Execute(
        WarpBackendArtifact artifact,
        WarpControlFlowKernel kernel,
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
        WarpControlFlowKernel kernel,
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
        WarpControlFlowKernel kernel,
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
        WarpControlFlowKernel kernel,
        IReadOnlyList<uint> inputs,
        IReadOnlyList<uint> scalarArguments) =>
        EvaluateBody(
            kernel,
            kernel.Blocks,
            kernel.ValueCount,
            inputs,
            scalarArguments,
            Array.Empty<uint>());

    private static uint EvaluateBody(
        WarpControlFlowKernel kernel,
        IReadOnlyList<WarpBasicBlock> blocks,
        int valueCount,
        IReadOnlyList<uint> inputs,
        IReadOnlyList<uint> scalarArguments,
        IReadOnlyList<uint> functionArguments)
    {
        var values = new uint[valueCount];

        int blockId = 0;
        while (true)
        {
            WarpBasicBlock block = blocks[blockId];
            foreach (WarpIrInstruction instruction in block.Instructions)
            {
                uint left = instruction.Left < 0 ? 0 : values[instruction.Left];
                uint right = instruction.Right < 0 ? 0 : values[instruction.Right];
                uint third = instruction.Third < 0 ? 0 : values[instruction.Third];

                values[instruction.Result] = instruction.OpCode switch
                {
                    WarpIrOpCode.LoadInput => inputs[checked((int)instruction.Immediate)],
                    WarpIrOpCode.LoadScalar => scalarArguments[checked((int)instruction.Immediate)],
                    WarpIrOpCode.LoadArgument => functionArguments[checked((int)instruction.Immediate)],
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
                    WarpIrOpCode.Equal => left == right ? 1u : 0u,
                    WarpIrOpCode.NotEqual => left != right ? 1u : 0u,
                    WarpIrOpCode.LessThanUnsigned => left < right ? 1u : 0u,
                    WarpIrOpCode.LessThanOrEqualUnsigned => left <= right ? 1u : 0u,
                    WarpIrOpCode.GreaterThanUnsigned => left > right ? 1u : 0u,
                    WarpIrOpCode.GreaterThanOrEqualUnsigned => left >= right ? 1u : 0u,
                    WarpIrOpCode.Select => left != 0 ? right : third,
                    WarpIrOpCode.Call => EvaluateFunction(kernel, instruction, values),
                    _ => throw new InvalidOperationException(
                        "The semantic emulator received an unregistered opcode."),
                };
            }

            switch (block.Terminator)
            {
                case WarpBranchTerminator branch:
                    AssignParameters(blocks, values, branch.Target);
                    blockId = branch.Target.Block;
                    break;

                case WarpConditionalBranchTerminator conditional:
                    WarpBranchTarget target = values[conditional.Condition] != 0
                        ? conditional.WhenNonZero
                        : conditional.WhenZero;
                    AssignParameters(blocks, values, target);
                    blockId = target.Block;
                    break;

                case WarpReturnTerminator @return:
                    return values[@return.Value];

                default:
                    throw new InvalidOperationException(
                        "The semantic emulator received an unregistered terminator.");
            }
        }
    }

    private static uint EvaluateFunction(
        WarpControlFlowKernel kernel,
        WarpIrInstruction instruction,
        IReadOnlyList<uint> callerValues)
    {
        WarpControlFlowFunction function = kernel.Functions[instruction.Callee];
        var arguments = new uint[instruction.Arguments.Count];
        for (int index = 0; index < arguments.Length; index++)
        {
            arguments[index] = callerValues[instruction.Arguments[index]];
        }

        return EvaluateBody(
            kernel,
            function.Blocks,
            function.ValueCount,
            Array.Empty<uint>(),
            Array.Empty<uint>(),
            arguments);
    }

    private static void AssignParameters(
        IReadOnlyList<WarpBasicBlock> blocks,
        uint[] values,
        WarpBranchTarget target)
    {
        WarpBasicBlock destination = blocks[target.Block];
        var arguments = new uint[target.Arguments.Count];
        for (int index = 0; index < arguments.Length; index++)
        {
            arguments[index] = values[target.Arguments[index]];
        }

        for (int index = 0; index < arguments.Length; index++)
        {
            values[destination.Parameters[index].Value] = arguments[index];
        }
    }
}
