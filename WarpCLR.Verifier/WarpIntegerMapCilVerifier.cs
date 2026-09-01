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

        var stack = new List<WarpExpression>(method.MaxStack);
        var locals = new WarpExpression?[method.LocalCount];
        var reader = new IlReader(method.Il);

        while (!reader.IsComplete)
        {
            int offset = reader.Offset;
            OpCode opCode = ReadOpCode(ref reader, offset);

            if (Is(opCode, OpCodes.Nop))
            {
                continue;
            }

            if (TryReadArgument(opCode, ref reader, out int argumentIndex))
            {
                PushArgument(stack, method, argumentIndex, offset);
                continue;
            }

            if (TryReadConstant(opCode, ref reader, out uint constant))
            {
                stack.Add(new WarpConstantExpression(constant));
                continue;
            }

            if (TryReadLocal(opCode, ref reader, out int localIndex))
            {
                stack.Add(GetLocal(locals, localIndex, offset));
                continue;
            }

            if (TryWriteLocal(opCode, ref reader, out localIndex))
            {
                SetLocal(locals, localIndex, Pop(stack, offset), offset);
                continue;
            }

            if (TryGetBinaryOperator(opCode, out WarpBinaryOperator binaryOperator))
            {
                WarpExpression right = Pop(stack, offset);
                WarpExpression left = Pop(stack, offset);
                stack.Add(new WarpBinaryExpression(binaryOperator, left, right));
                continue;
            }

            if (Is(opCode, OpCodes.Not))
            {
                stack.Add(new WarpUnaryExpression(WarpUnaryOperator.BitwiseNot, Pop(stack, offset)));
                continue;
            }

            if (Is(opCode, OpCodes.Conv_U4) || Is(opCode, OpCodes.Conv_I4))
            {
                RequireStackValue(stack, offset);
                continue;
            }

            if (Is(opCode, OpCodes.Dup))
            {
                WarpExpression value = Peek(stack, offset);
                stack.Add(value);
                continue;
            }

            if (Is(opCode, OpCodes.Pop))
            {
                _ = Pop(stack, offset);
                continue;
            }

            if (Is(opCode, OpCodes.Ret))
            {
                if (!reader.IsComplete)
                {
                    throw CilError("WRPCIL1004", "Unreachable CIL after ret is not permitted.", offset);
                }

                if (stack.Count != 1)
                {
                    throw CilError("WRPCIL1002", "The evaluation stack must contain one result at ret.", offset);
                }

                return new WarpIntegerMapKernel(
                    method.Identity,
                    method.InputBufferCount,
                    method.ParameterCount - method.InputBufferCount,
                    stack[0],
                    method.Reduction);
            }

            throw CilError(
                "WRPCIL1001",
                $"Opcode '{opCode.Name}' is outside the integer map profile.",
                offset);
        }

        throw CilError("WRPCIL1005", "The entry point does not end with ret.", method.Il.Length);
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
        {
            this.bytes = bytes;
        }

        public int Offset { get; private set; }

        public bool IsComplete => Offset == bytes.Length;

        public byte ReadByte()
        {
            if ((uint)Offset >= (uint)bytes.Length)
            {
                throw CilError("WRPCIL1011", "The CIL operand is incomplete.", Offset);
            }

            return bytes[Offset++];
        }

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
