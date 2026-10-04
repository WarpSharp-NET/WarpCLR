using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal static class WarpPortableMethodGraphDecoder
{
    private static readonly FrozenDictionary<short, OpCode> OpCodesByValue = CreateOpCodes();

    public static ImmutableArray<WarpPortableMethodGraphInstruction> Decode(ReadOnlySpan<byte> cil, string identity)
    {
        var instructions = ImmutableArray.CreateBuilder<WarpPortableMethodGraphInstruction>();
        int position = 0;
        while (position < cil.Length)
        {
            WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.Instructions,
                instructions.Count + 1L, WarpCompilationAdmission.MaximumInstructionsPerEntry);
            instructions.Add(ReadInstruction(cil, ref position, identity));
        }

        var boundaries = new HashSet<int>(instructions.Select(instruction => instruction.Offset));
        foreach (WarpPortableMethodGraphInstruction instruction in instructions)
        {
            foreach (int target in instruction.BranchTargets)
            {
                if (!boundaries.Contains(target))
                {
                    throw Error(identity, instruction.Offset, "A branch enters the middle of an instruction.");
                }
            }
        }

        return instructions.ToImmutable();
    }

    private static WarpPortableMethodGraphInstruction ReadInstruction(ReadOnlySpan<byte> cil, ref int position, string identity)
    {
        int offset = position;
        short value = ReadByte(cil, ref position, identity, offset);
        if (value == 0xFE)
        {
            value = unchecked((short)(0xFE00 | ReadByte(cil, ref position, identity, offset)));
        }

        if (!OpCodesByValue.TryGetValue(value, out OpCode opCode))
        {
            throw Error(identity, offset, "Unknown CIL opcode.");
        }

        (ulong operand, ImmutableArray<int> targets) = ReadOperand(cil, ref position, opCode.OperandType, identity, offset);
        return new WarpPortableMethodGraphInstruction(offset, position, opCode, operand, targets, null, null, null, null);
    }

    private static (ulong Operand, ImmutableArray<int> Targets) ReadOperand(ReadOnlySpan<byte> cil, ref int position,
        OperandType type, string identity, int offset)
    {
        switch (type)
        {
            case OperandType.InlineNone:
                return (0, []);
            case OperandType.ShortInlineI:
                return (unchecked((uint)(sbyte)ReadByte(cil, ref position, identity, offset)), []);
            case OperandType.ShortInlineVar:
                return (ReadByte(cil, ref position, identity, offset), []);
            case OperandType.InlineVar:
                RequireBytes(cil, position, sizeof(ushort), identity, offset);
                ushort variable = BinaryPrimitives.ReadUInt16LittleEndian(cil[position..]);
                position += sizeof(ushort);
                return (variable, []);
            case OperandType.InlineI:
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
            case OperandType.ShortInlineR:
                return (ReadUInt32(cil, ref position, identity, offset), []);
            case OperandType.InlineI8:
            case OperandType.InlineR:
                RequireBytes(cil, position, sizeof(ulong), identity, offset);
                ulong wide = BinaryPrimitives.ReadUInt64LittleEndian(cil[position..]);
                position += sizeof(ulong);
                return (wide, []);
            case OperandType.ShortInlineBrTarget:
                int shortDelta = unchecked((sbyte)ReadByte(cil, ref position, identity, offset));
                return (0, [BranchTarget(position, shortDelta, cil.Length, identity, offset)]);
            case OperandType.InlineBrTarget:
                int delta = unchecked((int)ReadUInt32(cil, ref position, identity, offset));
                return (0, [BranchTarget(position, delta, cil.Length, identity, offset)]);
            case OperandType.InlineSwitch:
                return (0, ReadSwitch(cil, ref position, identity, offset));
            default:
                throw Error(identity, offset, "The operand encoding is not supported.");
        }
    }

    private static ImmutableArray<int> ReadSwitch(ReadOnlySpan<byte> cil, ref int position, string identity, int offset)
    {
        uint count = ReadUInt32(cil, ref position, identity, offset);
        if (count > (uint)((cil.Length - position) / sizeof(int)))
        {
            throw Error(identity, offset, "The switch operand is truncated.");
        }

        WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.OperandReferences,
            count, WarpCompilationAdmission.MaximumOperandReferencesPerEntry);
        int switchEnd = checked(position + (int)count * sizeof(int));
        var targets = ImmutableArray.CreateBuilder<int>((int)count);
        for (uint index = 0; index < count; index++)
        {
            targets.Add(BranchTarget(switchEnd, unchecked((int)ReadUInt32(cil, ref position, identity, offset)), cil.Length, identity, offset));
        }

        return targets.MoveToImmutable();
    }

    private static int BranchTarget(int nextOffset, int delta, int length, string identity, int offset)
    {
        long target = (long)nextOffset + delta;
        if (target < 0 || target >= length)
        {
            throw Error(identity, offset, "A branch target is outside the method body.");
        }

        return (int)target;
    }

    private static byte ReadByte(ReadOnlySpan<byte> cil, ref int position, string identity, int offset)
    {
        RequireBytes(cil, position, 1, identity, offset);
        return cil[position++];
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> cil, ref int position, string identity, int offset)
    {
        RequireBytes(cil, position, sizeof(uint), identity, offset);
        uint result = BinaryPrimitives.ReadUInt32LittleEndian(cil[position..]);
        position += sizeof(uint);
        return result;
    }

    private static void RequireBytes(ReadOnlySpan<byte> cil, int position, int count, string identity, int offset)
    {
        if (count > cil.Length - position)
        {
            throw Error(identity, offset, "The CIL operand is truncated.");
        }
    }

    private static FrozenDictionary<short, OpCode> CreateOpCodes()
    {
        var result = new Dictionary<short, OpCode>();
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode code && code.OpCodeType != OpCodeType.Nternal)
            {
                result.Add(code.Value, code);
            }
        }

        return result.ToFrozenDictionary();
    }

    private static WarpVerificationException Error(string identity, int offset, string message) =>
        new("WRPCLR2101", $"Method '{identity}' at IL_{offset:X4}: {message}", offset);
}
