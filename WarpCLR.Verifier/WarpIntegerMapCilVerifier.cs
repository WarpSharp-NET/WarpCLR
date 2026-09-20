using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed class WarpIntegerMapMethodBody
{
    public WarpIntegerMapMethodBody(
        string identity,
        int parameterCount,
        int inputBufferCount,
        int maxStack,
        int localCount,
        ReadOnlySpan<byte> il,
        WarpReductionOperation? reduction = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentOutOfRangeException.ThrowIfNegative(parameterCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputBufferCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(inputBufferCount, parameterCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maxStack);
        ArgumentOutOfRangeException.ThrowIfNegative(localCount);
        if (reduction.HasValue && !Enum.IsDefined(reduction.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(reduction));
        }

        Identity = identity;
        ParameterCount = parameterCount;
        InputBufferCount = inputBufferCount;
        MaxStack = maxStack;
        LocalCount = localCount;
        Il = il.ToArray();
        Reduction = reduction;
    }

    public string Identity { get; }

    public int ParameterCount { get; }

    public int InputBufferCount { get; }

    public int MaxStack { get; }

    public int LocalCount { get; }

    public byte[] Il { get; }

    public WarpReductionOperation? Reduction { get; }
}

internal static class WarpIntegerMapCilVerifier
{
    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue = CreateOpCodeMap();

    public static WarpIntegerMapKernel Verify(WarpIntegerMapMethodBody method)
    {
        ArgumentNullException.ThrowIfNull(method);

        HashSet<int> instructionOffsets = ReadInstructionOffsets(method.Il);
        var visitedOffsets = new HashSet<int>();
        var stackDepths = new Dictionary<int, int>();
        var state = new ExecutionState(
            new List<WarpExpression>(method.MaxStack),
            new WarpExpression?[method.LocalCount]);
        WarpExpression result = EvaluatePath(
            method,
            0,
            state,
            instructionOffsets,
            visitedOffsets,
            stackDepths,
            []);

        if (!instructionOffsets.SetEquals(visitedOffsets))
        {
            int unreachableOffset = instructionOffsets
                .Where(offset => !visitedOffsets.Contains(offset))
                .Min();
            throw CilError(
                "WRPCIL1004",
                "Unreachable CIL is not permitted.",
                unreachableOffset);
        }

        return new WarpIntegerMapKernel(
            method.Identity,
            method.InputBufferCount,
            method.ParameterCount - method.InputBufferCount,
            result,
            method.Reduction);
    }

    private static WarpExpression EvaluatePath(
        WarpIntegerMapMethodBody method,
        int startOffset,
        ExecutionState state,
        HashSet<int> instructionOffsets,
        HashSet<int> visitedOffsets,
        Dictionary<int, int> stackDepths,
        HashSet<int> activeOffsets)
    {
        var reader = new IlReader(method.Il, startOffset);

        while (!reader.IsComplete)
        {
            int offset = reader.Offset;
            if (!instructionOffsets.Contains(offset))
            {
                throw CilError(
                    "WRPCIL1012",
                    "Control flow targets the middle of a CIL instruction.",
                    offset);
            }

            if (state.Stack.Count > method.MaxStack)
            {
                throw CilError(
                    "WRPCIL1002",
                    "The CIL evaluation stack exceeds maxstack.",
                    offset);
            }

            if (stackDepths.TryGetValue(offset, out int expectedStackDepth))
            {
                if (state.Stack.Count != expectedStackDepth)
                {
                    throw CilError(
                        "WRPCIL1002",
                        "Control-flow paths reach an instruction with different stack depths.",
                        offset);
                }
            }
            else
            {
                stackDepths.Add(offset, state.Stack.Count);
            }

            if (!activeOffsets.Add(offset))
            {
                throw CilError(
                    "WRPCIL1013",
                    "Loops require the WarpCLR control-flow graph profile.",
                    offset);
            }

            visitedOffsets.Add(offset);
            OpCode opCode = ReadOpCode(ref reader, offset);

            if (Is(opCode, OpCodes.Nop))
            {
                continue;
            }

            if (TryReadArgument(opCode, ref reader, out int argumentIndex))
            {
                PushArgument(state.Stack, method, argumentIndex, offset);
                continue;
            }

            if (TryReadConstant(opCode, ref reader, out uint constant))
            {
                state.Stack.Add(new WarpConstantExpression(constant));
                continue;
            }

            if (TryReadLocal(opCode, ref reader, out int localIndex))
            {
                state.Stack.Add(GetLocal(state.Locals, localIndex, offset));
                continue;
            }

            if (TryWriteLocal(opCode, ref reader, out localIndex))
            {
                SetLocal(
                    state.Locals,
                    localIndex,
                    Pop(state.Stack, offset),
                    offset);
                continue;
            }

            if (TryGetBinaryOperator(opCode, out WarpBinaryOperator binaryOperator))
            {
                WarpExpression right = Pop(state.Stack, offset);
                WarpExpression left = Pop(state.Stack, offset);
                state.Stack.Add(new WarpBinaryExpression(binaryOperator, left, right));
                continue;
            }

            if (TryGetComparisonOperator(opCode, out WarpBinaryOperator comparisonOperator))
            {
                WarpExpression right = Pop(state.Stack, offset);
                WarpExpression left = Pop(state.Stack, offset);
                state.Stack.Add(new WarpBinaryExpression(comparisonOperator, left, right));
                continue;
            }

            if (Is(opCode, OpCodes.Not))
            {
                state.Stack.Add(
                    new WarpUnaryExpression(
                        WarpUnaryOperator.BitwiseNot,
                        Pop(state.Stack, offset)));
                continue;
            }

            if (Is(opCode, OpCodes.Conv_U4) || Is(opCode, OpCodes.Conv_I4))
            {
                RequireStackValue(state.Stack, offset);
                continue;
            }

            if (Is(opCode, OpCodes.Dup))
            {
                WarpExpression value = Peek(state.Stack, offset);
                state.Stack.Add(value);
                continue;
            }

            if (Is(opCode, OpCodes.Pop))
            {
                _ = Pop(state.Stack, offset);
                continue;
            }

            if (Is(opCode, OpCodes.Br) || Is(opCode, OpCodes.Br_S))
            {
                int target = ReadBranchTarget(opCode, ref reader, offset);
                ValidateBranchTarget(target, offset, instructionOffsets);
                reader = new IlReader(method.Il, target);
                continue;
            }

            if (IsConditionalBranch(opCode))
            {
                int target = ReadBranchTarget(opCode, ref reader, offset);
                int fallthrough = reader.Offset;
                ValidateBranchTarget(target, offset, instructionOffsets);

                WarpExpression condition;
                bool branchWhenNonZero;
                if (Is(opCode, OpCodes.Brtrue) || Is(opCode, OpCodes.Brtrue_S))
                {
                    condition = Pop(state.Stack, offset);
                    branchWhenNonZero = true;
                }
                else if (Is(opCode, OpCodes.Brfalse) || Is(opCode, OpCodes.Brfalse_S))
                {
                    condition = Pop(state.Stack, offset);
                    branchWhenNonZero = false;
                }
                else
                {
                    WarpExpression right = Pop(state.Stack, offset);
                    WarpExpression left = Pop(state.Stack, offset);
                    condition = new WarpBinaryExpression(
                        GetBranchComparisonOperator(opCode),
                        left,
                        right);
                    branchWhenNonZero = true;
                }

                WarpExpression branchResult = EvaluatePath(
                    method,
                    target,
                    state.Clone(),
                    instructionOffsets,
                    visitedOffsets,
                    stackDepths,
                    new HashSet<int>(activeOffsets));
                WarpExpression fallthroughResult = EvaluatePath(
                    method,
                    fallthrough,
                    state.Clone(),
                    instructionOffsets,
                    visitedOffsets,
                    stackDepths,
                    new HashSet<int>(activeOffsets));

                return branchWhenNonZero
                    ? new WarpConditionalExpression(
                        condition,
                        branchResult,
                        fallthroughResult)
                    : new WarpConditionalExpression(
                        condition,
                        fallthroughResult,
                        branchResult);
            }

            if (Is(opCode, OpCodes.Ret))
            {
                if (state.Stack.Count != 1)
                {
                    throw CilError(
                        "WRPCIL1002",
                        "The evaluation stack must contain one result at ret.",
                        offset);
                }

                return state.Stack[0];
            }

            throw CilError(
                "WRPCIL1001",
                $"Opcode '{opCode.Name}' is outside the integer map profile.",
                offset);
        }

        throw CilError("WRPCIL1005", "The entry point does not end with ret.", method.Il.Length);
    }

    private static HashSet<int> ReadInstructionOffsets(ReadOnlySpan<byte> il)
    {
        var offsets = new HashSet<int>();
        var reader = new IlReader(il);
        while (!reader.IsComplete)
        {
            int offset = reader.Offset;
            offsets.Add(offset);
            OpCode opCode = ReadOpCode(ref reader, offset);
            SkipOperand(opCode, ref reader, offset);
        }

        return offsets;
    }

    private static void SkipOperand(
        OpCode opCode,
        ref IlReader reader,
        int offset)
    {
        switch (opCode.OperandType)
        {
            case OperandType.InlineNone:
                return;

            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                _ = reader.ReadByte();
                return;

            case OperandType.InlineVar:
                _ = reader.ReadUInt16();
                return;

            case OperandType.InlineBrTarget:
            case OperandType.InlineField:
            case OperandType.InlineI:
            case OperandType.InlineMethod:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
            case OperandType.ShortInlineR:
                _ = reader.ReadInt32();
                return;

            case OperandType.InlineI8:
            case OperandType.InlineR:
                _ = reader.ReadInt32();
                _ = reader.ReadInt32();
                return;

            case OperandType.InlineSwitch:
                int count = reader.ReadInt32();
                if (count < 0 || count > reader.Remaining / sizeof(int))
                {
                    throw CilError(
                        "WRPCIL1011",
                        "The CIL switch operand is incomplete.",
                        offset);
                }

                for (int index = 0; index < count; index++)
                {
                    _ = reader.ReadInt32();
                }

                return;

            default:
                throw CilError(
                    "WRPCIL1010",
                    $"Opcode '{opCode.Name}' has an unknown operand encoding.",
                    offset);
        }
    }

    private sealed class ExecutionState
    {
        public ExecutionState(
            List<WarpExpression> stack,
            WarpExpression?[] locals)
        {
            Stack = stack;
            Locals = locals;
        }

        public List<WarpExpression> Stack { get; }

        public WarpExpression?[] Locals { get; }

        public ExecutionState Clone() => new(
            new List<WarpExpression>(Stack),
            (WarpExpression?[])Locals.Clone());
    }

    private static void PushArgument(
        List<WarpExpression> stack,
        WarpIntegerMapMethodBody method,
        int argumentIndex,
        int offset)
    {
        if ((uint)argumentIndex >= (uint)method.ParameterCount)
        {
            throw CilError("WRPCIL1006", "The CIL references an invalid argument index.", offset);
        }

        WarpExpression expression = argumentIndex < method.InputBufferCount
            ? new WarpInputExpression(argumentIndex)
            : new WarpScalarExpression(argumentIndex - method.InputBufferCount);

        stack.Add(expression);
    }

    private static WarpExpression GetLocal(WarpExpression?[] locals, int index, int offset)
    {
        if ((uint)index >= (uint)locals.Length)
        {
            throw CilError("WRPCIL1007", "The CIL reads an invalid local variable index.", offset);
        }

        return locals[index]
            ?? throw CilError("WRPCIL1008", "The CIL reads a local variable before assignment.", offset);
    }

    private static void SetLocal(WarpExpression?[] locals, int index, WarpExpression value, int offset)
    {
        if ((uint)index >= (uint)locals.Length)
        {
            throw CilError("WRPCIL1009", "The CIL writes an invalid local variable index.", offset);
        }

        locals[index] = value;
    }

    private static WarpExpression Pop(List<WarpExpression> stack, int offset)
    {
        RequireStackValue(stack, offset);
        int index = stack.Count - 1;
        WarpExpression result = stack[index];
        stack.RemoveAt(index);
        return result;
    }

    private static WarpExpression Peek(List<WarpExpression> stack, int offset)
    {
        RequireStackValue(stack, offset);
        return stack[^1];
    }

    private static void RequireStackValue(List<WarpExpression> stack, int offset)
    {
        if (stack.Count == 0)
        {
            throw CilError("WRPCIL1002", "The CIL evaluation stack is empty.", offset);
        }
    }

    private static bool TryReadArgument(OpCode opCode, ref IlReader reader, out int index)
    {
        if (Is(opCode, OpCodes.Ldarg_0))
        {
            index = 0;
            return true;
        }

        if (Is(opCode, OpCodes.Ldarg_1))
        {
            index = 1;
            return true;
        }

        if (Is(opCode, OpCodes.Ldarg_2))
        {
            index = 2;
            return true;
        }

        if (Is(opCode, OpCodes.Ldarg_3))
        {
            index = 3;
            return true;
        }

        if (Is(opCode, OpCodes.Ldarg_S))
        {
            index = reader.ReadByte();
            return true;
        }

        if (Is(opCode, OpCodes.Ldarg))
        {
            index = reader.ReadUInt16();
            return true;
        }

        index = 0;
        return false;
    }

    private static bool TryReadConstant(OpCode opCode, ref IlReader reader, out uint value)
    {
        if (Is(opCode, OpCodes.Ldc_I4_M1))
        {
            value = uint.MaxValue;
            return true;
        }

        OpCode[] smallConstants =
        [
            OpCodes.Ldc_I4_0,
            OpCodes.Ldc_I4_1,
            OpCodes.Ldc_I4_2,
            OpCodes.Ldc_I4_3,
            OpCodes.Ldc_I4_4,
            OpCodes.Ldc_I4_5,
            OpCodes.Ldc_I4_6,
            OpCodes.Ldc_I4_7,
            OpCodes.Ldc_I4_8,
        ];

        for (int index = 0; index < smallConstants.Length; index++)
        {
            if (Is(opCode, smallConstants[index]))
            {
                value = (uint)index;
                return true;
            }
        }

        if (Is(opCode, OpCodes.Ldc_I4_S))
        {
            value = unchecked((uint)(sbyte)reader.ReadByte());
            return true;
        }

        if (Is(opCode, OpCodes.Ldc_I4))
        {
            value = unchecked((uint)reader.ReadInt32());
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryReadLocal(OpCode opCode, ref IlReader reader, out int index)
    {
        if (TryGetFixedIndex(
                opCode,
                OpCodes.Ldloc_0,
                OpCodes.Ldloc_1,
                OpCodes.Ldloc_2,
                OpCodes.Ldloc_3,
                out index))
        {
            return true;
        }

        if (Is(opCode, OpCodes.Ldloc_S))
        {
            index = reader.ReadByte();
            return true;
        }

        if (Is(opCode, OpCodes.Ldloc))
        {
            index = reader.ReadUInt16();
            return true;
        }

        index = 0;
        return false;
    }

    private static bool TryWriteLocal(OpCode opCode, ref IlReader reader, out int index)
    {
        if (TryGetFixedIndex(
                opCode,
                OpCodes.Stloc_0,
                OpCodes.Stloc_1,
                OpCodes.Stloc_2,
                OpCodes.Stloc_3,
                out index))
        {
            return true;
        }

        if (Is(opCode, OpCodes.Stloc_S))
        {
            index = reader.ReadByte();
            return true;
        }

        if (Is(opCode, OpCodes.Stloc))
        {
            index = reader.ReadUInt16();
            return true;
        }

        index = 0;
        return false;
    }

    private static bool TryGetFixedIndex(
        OpCode actual,
        OpCode zero,
        OpCode one,
        OpCode two,
        OpCode three,
        out int index)
    {
        if (Is(actual, zero))
        {
            index = 0;
            return true;
        }

        if (Is(actual, one))
        {
            index = 1;
            return true;
        }

        if (Is(actual, two))
        {
            index = 2;
            return true;
        }

        if (Is(actual, three))
        {
            index = 3;
            return true;
        }

        index = 0;
        return false;
    }

    private static bool TryGetBinaryOperator(OpCode opCode, out WarpBinaryOperator @operator)
    {
        if (Is(opCode, OpCodes.Add))
        {
            @operator = WarpBinaryOperator.Add;
            return true;
        }

        if (Is(opCode, OpCodes.Sub))
        {
            @operator = WarpBinaryOperator.Subtract;
            return true;
        }

        if (Is(opCode, OpCodes.Mul))
        {
            @operator = WarpBinaryOperator.Multiply;
            return true;
        }

        if (Is(opCode, OpCodes.And))
        {
            @operator = WarpBinaryOperator.BitwiseAnd;
            return true;
        }

        if (Is(opCode, OpCodes.Or))
        {
            @operator = WarpBinaryOperator.BitwiseOr;
            return true;
        }

        if (Is(opCode, OpCodes.Xor))
        {
            @operator = WarpBinaryOperator.ExclusiveOr;
            return true;
        }

        if (Is(opCode, OpCodes.Shl))
        {
            @operator = WarpBinaryOperator.ShiftLeft;
            return true;
        }

        if (Is(opCode, OpCodes.Shr_Un))
        {
            @operator = WarpBinaryOperator.ShiftRightLogical;
            return true;
        }

        @operator = default;
        return false;
    }

    private static bool TryGetComparisonOperator(
        OpCode opCode,
        out WarpBinaryOperator @operator)
    {
        if (Is(opCode, OpCodes.Ceq))
        {
            @operator = WarpBinaryOperator.Equal;
            return true;
        }

        if (Is(opCode, OpCodes.Clt_Un))
        {
            @operator = WarpBinaryOperator.LessThanUnsigned;
            return true;
        }

        if (Is(opCode, OpCodes.Cgt_Un))
        {
            @operator = WarpBinaryOperator.GreaterThanUnsigned;
            return true;
        }

        @operator = default;
        return false;
    }

    private static bool IsConditionalBranch(OpCode opCode) =>
        Is(opCode, OpCodes.Brtrue) ||
        Is(opCode, OpCodes.Brtrue_S) ||
        Is(opCode, OpCodes.Brfalse) ||
        Is(opCode, OpCodes.Brfalse_S) ||
        Is(opCode, OpCodes.Beq) ||
        Is(opCode, OpCodes.Beq_S) ||
        Is(opCode, OpCodes.Bne_Un) ||
        Is(opCode, OpCodes.Bne_Un_S) ||
        Is(opCode, OpCodes.Blt_Un) ||
        Is(opCode, OpCodes.Blt_Un_S) ||
        Is(opCode, OpCodes.Ble_Un) ||
        Is(opCode, OpCodes.Ble_Un_S) ||
        Is(opCode, OpCodes.Bgt_Un) ||
        Is(opCode, OpCodes.Bgt_Un_S) ||
        Is(opCode, OpCodes.Bge_Un) ||
        Is(opCode, OpCodes.Bge_Un_S);

    private static WarpBinaryOperator GetBranchComparisonOperator(OpCode opCode)
    {
        if (Is(opCode, OpCodes.Beq) || Is(opCode, OpCodes.Beq_S))
        {
            return WarpBinaryOperator.Equal;
        }

        if (Is(opCode, OpCodes.Bne_Un) || Is(opCode, OpCodes.Bne_Un_S))
        {
            return WarpBinaryOperator.NotEqual;
        }

        if (Is(opCode, OpCodes.Blt_Un) || Is(opCode, OpCodes.Blt_Un_S))
        {
            return WarpBinaryOperator.LessThanUnsigned;
        }

        if (Is(opCode, OpCodes.Ble_Un) || Is(opCode, OpCodes.Ble_Un_S))
        {
            return WarpBinaryOperator.LessThanOrEqualUnsigned;
        }

        if (Is(opCode, OpCodes.Bgt_Un) || Is(opCode, OpCodes.Bgt_Un_S))
        {
            return WarpBinaryOperator.GreaterThanUnsigned;
        }

        if (Is(opCode, OpCodes.Bge_Un) || Is(opCode, OpCodes.Bge_Un_S))
        {
            return WarpBinaryOperator.GreaterThanOrEqualUnsigned;
        }

        throw new ArgumentOutOfRangeException(nameof(opCode));
    }

    private static int ReadBranchTarget(
        OpCode opCode,
        ref IlReader reader,
        int offset)
    {
        int delta = opCode.OperandType switch
        {
            OperandType.ShortInlineBrTarget => reader.ReadSByte(),
            OperandType.InlineBrTarget => reader.ReadInt32(),
            _ => throw CilError(
                "WRPCIL1010",
                $"Opcode '{opCode.Name}' does not encode a branch target.",
                offset),
        };
        long target = (long)reader.Offset + delta;
        if (target is < 0 or > int.MaxValue)
        {
            throw CilError(
                "WRPCIL1012",
                "The CIL branch target is outside the method body.",
                offset);
        }

        return (int)target;
    }

    private static void ValidateBranchTarget(
        int target,
        int branchOffset,
        HashSet<int> instructionOffsets)
    {
        if (!instructionOffsets.Contains(target))
        {
            throw CilError(
                "WRPCIL1012",
                "The CIL branch target is not an instruction boundary.",
                branchOffset);
        }

        if (target <= branchOffset)
        {
            throw CilError(
                "WRPCIL1013",
                "Loops require the WarpCLR control-flow graph profile.",
                branchOffset);
        }
    }

    private static OpCode ReadOpCode(ref IlReader reader, int offset)
    {
        byte first = reader.ReadByte();
        short value = first == 0xFE
            ? unchecked((short)(0xFE00 | reader.ReadByte()))
            : first;

        return OpCodesByValue.TryGetValue(value, out OpCode opCode)
            ? opCode
            : throw CilError("WRPCIL1010", "The CIL contains an unknown opcode.", offset);
    }

    private static IReadOnlyDictionary<short, OpCode> CreateOpCodeMap()
    {
        return typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);
    }

    private static bool Is(OpCode left, OpCode right) => left.Value == right.Value;

    private static WarpVerificationException CilError(string code, string message, int offset)
    {
        return new WarpVerificationException(code, $"{message} IL offset: 0x{offset:X4}.", offset);
    }

    private ref struct IlReader
    {
        private readonly ReadOnlySpan<byte> bytes;

        public IlReader(ReadOnlySpan<byte> bytes)
            : this(bytes, 0)
        {
        }

        public IlReader(ReadOnlySpan<byte> bytes, int offset)
        {
            if ((uint)offset > (uint)bytes.Length)
            {
                throw CilError(
                    "WRPCIL1012",
                    "The CIL offset is outside the method body.",
                    offset);
            }

            this.bytes = bytes;
            Offset = offset;
        }

        public int Offset { get; private set; }

        public bool IsComplete => Offset == bytes.Length;

        public int Remaining => bytes.Length - Offset;

        public byte ReadByte()
        {
            if ((uint)Offset >= (uint)bytes.Length)
            {
                throw CilError("WRPCIL1011", "The CIL operand is incomplete.", Offset);
            }

            return bytes[Offset++];
        }

        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

        public ushort ReadUInt16()
        {
            uint low = ReadByte();
            uint high = ReadByte();
            return (ushort)(low | (high << 8));
        }

        public int ReadInt32()
        {
            uint value = ReadByte();
            value |= (uint)ReadByte() << 8;
            value |= (uint)ReadByte() << 16;
            value |= (uint)ReadByte() << 24;
            return unchecked((int)value);
        }
    }
}
