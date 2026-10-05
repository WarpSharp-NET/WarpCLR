using System.Collections.ObjectModel;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

internal static partial class WarpCoreCLRBinaryPlanCodec
{
    private static void WriteBody(BinaryWriter writer, ReadOnlyCollection<WarpBasicBlock> blocks)
    {
        writer.Write(blocks.Count);
        foreach (WarpBasicBlock block in blocks)
        {
            writer.Write(block.Id); writer.Write(block.Parameters.Count);
            foreach (WarpBlockParameter parameter in block.Parameters) { writer.Write(parameter.Value); writer.Write((int)parameter.Type); }
            writer.Write(block.Instructions.Count);
            foreach (WarpIrInstruction instruction in block.Instructions)
            {
                writer.Write(instruction.Result); writer.Write((int)instruction.OpCode); writer.Write((int)instruction.ResultType);
                writer.Write(instruction.Left); writer.Write(instruction.Right); writer.Write(instruction.Immediate); writer.Write(instruction.Third);
                writer.Write(instruction.Callee); writer.Write(instruction.ResultWordCount); WriteValues(writer, instruction.Arguments);
            }
            WriteTerminator(writer, block.Terminator);
        }
    }

    private static WarpBasicBlock[] ReadBody(BinaryReader reader, Usage usage)
    {
        int count = Count(reader, WarpCompilationAdmission.MaximumBlocksPerEntry);
        usage.Blocks += count;
        if (usage.Blocks > WarpCompilationAdmission.MaximumBlocksPerEntry) { throw new InvalidDataException("Binary block quota exhausted."); }
        var blocks = new WarpBasicBlock[count];
        foreach (ref WarpBasicBlock block in blocks.AsSpan())
        {
            int id = reader.ReadInt32();
            int parameters = Count(reader, WarpCompilationAdmission.MaximumParametersPerBody);
            var values = new WarpBlockParameter[parameters];
            foreach (ref WarpBlockParameter value in values.AsSpan()) { value = new(reader.ReadInt32(), (WarpIrValueType)reader.ReadInt32()); }
            int length = Count(reader, WarpCompilationAdmission.MaximumInstructionsPerEntry);
            usage.Instructions += length;
            if (usage.Instructions > WarpCompilationAdmission.MaximumInstructionsPerEntry) { throw new InvalidDataException("Binary instruction quota exhausted."); }
            var instructions = new WarpIrInstruction[length];
            foreach (ref WarpIrInstruction instruction in instructions.AsSpan()) { instruction = ReadInstruction(reader, usage); }
            block = new(id, values, instructions, ReadTerminator(reader, usage));
        }
        return blocks;
    }

    private static WarpIrInstruction ReadInstruction(BinaryReader reader, Usage usage)
    {
        int result = reader.ReadInt32();
        var operation = (WarpIrOpCode)reader.ReadInt32();
        var type = (WarpIrValueType)reader.ReadInt32();
        int left = reader.ReadInt32(), right = reader.ReadInt32();
        uint immediate = reader.ReadUInt32();
        int third = reader.ReadInt32(), callee = reader.ReadInt32();
        int resultWords = Count(reader, WarpCompilationAdmission.MaximumParametersPerBody);
        int[] arguments = ReadValues(reader, usage);
        if (!Enum.IsDefined(type)) { throw new InvalidDataException("Binary value type is invalid."); }
        if (WarpManagedWideAtomicOpCode.IsAtomic(operation))
        {
            if (type != WarpIrValueType.UInt32 || right != -1 || third != -1 || immediate != 0 || callee != -1)
            {
                throw new InvalidDataException("Wide atomic metadata is not canonical.");
            }
            return new WarpIrInstruction(result, operation, left, arguments, resultWords);
        }
        if (operation == WarpIrOpCode.Call)
        {
            if (type != WarpIrValueType.UInt32 || left != -1 || right != -1 || third != -1 || immediate != 0 || callee < 0)
            { throw new InvalidDataException("Call metadata is not canonical."); }
            return new WarpIrInstruction(result, callee, arguments, resultWords);
        }
        return resultWords != 1 ? throw new InvalidDataException("Only calls and pair atomics can have tuple/void results.") :
            new WarpIrInstruction(result, operation, left, right, immediate, third, type, callee, arguments);
    }

    private static void WriteValues(BinaryWriter writer, IReadOnlyList<int> values)
    {
        writer.Write(values.Count);
        foreach (int value in values) { writer.Write(value); }
    }

    private static int[] ReadValues(BinaryReader reader, Usage usage)
    {
        int length = Count(reader, WarpCompilationAdmission.MaximumParametersPerBody);
        usage.Operands += length;
        if (usage.Operands > WarpCompilationAdmission.MaximumOperandReferencesPerEntry ||
            length * 4L > reader.BaseStream.Length - reader.BaseStream.Position) { throw new InvalidDataException("Binary operand quota/truncation."); }
        int[] values = new int[length];
        foreach (ref int value in values.AsSpan()) { value = reader.ReadInt32(); }
        return values;
    }

    private sealed class Usage
    {
        internal int Blocks { get; set; }
        internal int Instructions { get; set; }
        internal int Operands { get; set; }
    }
}
