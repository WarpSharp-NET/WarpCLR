using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

internal static partial class WarpCoreCLRBinaryPlanCodec
{
    private static void WriteTerminator(BinaryWriter writer, WarpBlockTerminator terminator)
    {
        switch (terminator)
        {
            case WarpBranchTerminator branch:
                writer.Write(0); WriteTarget(writer, branch.Target); break;
            case WarpConditionalBranchTerminator conditional:
                writer.Write(1); writer.Write(conditional.Condition); WriteTarget(writer, conditional.WhenNonZero); WriteTarget(writer, conditional.WhenZero); break;
            case WarpReturnTerminator result:
                writer.Write(2); writer.Write(result.Value); break;
            case WarpTupleReturnTerminator tuple:
                writer.Write(3); WriteValues(writer, tuple.Values); break;
            case WarpStateDispatchTerminator dispatch:
                writer.Write(4); writer.Write(dispatch.ResultWordCount); writer.Write(dispatch.Destinations.Count);
                foreach (WarpStateDispatchTarget destination in dispatch.Destinations)
                {
                    writer.Write(destination.Function); writer.Write(destination.Block);
                }
                break;
            case WarpManagedExceptionTerminator managed:
                writer.Write(5); writer.Write(managed.Context); writer.Write(managed.ObjectId);
                writer.Write(managed.Generation); writer.Write(managed.ResultWordCount); break;
            default:
                throw new InvalidDataException("Unsupported binary terminator kind.");
        }
    }

    private static WarpBlockTerminator ReadTerminator(BinaryReader reader, Usage usage) => reader.ReadInt32() switch
    {
        0 => new WarpBranchTerminator(ReadTarget(reader, usage)),
        1 => new WarpConditionalBranchTerminator(reader.ReadInt32(), ReadTarget(reader, usage), ReadTarget(reader, usage)),
        2 => new WarpReturnTerminator(reader.ReadInt32()),
        3 => new WarpTupleReturnTerminator(ReadValues(reader, usage)),
        4 => ReadStateDispatch(reader, usage),
        5 => new WarpManagedExceptionTerminator(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
            Count(reader, WarpCompilationAdmission.MaximumValueSlotsPerEntry)),
        _ => throw new InvalidDataException("Unsupported binary terminator kind."),
    };

    private static WarpStateDispatchTerminator ReadStateDispatch(BinaryReader reader, Usage usage)
    {
        int resultWords = Count(reader, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
        int count = Count(reader, WarpCompilationAdmission.MaximumBlocksPerEntry);
        usage.Operands += count * 2;
        if (usage.Operands > WarpCompilationAdmission.MaximumOperandReferencesPerEntry)
        { throw new InvalidDataException("Binary nonlocal destination quota exhausted."); }
        if (count * 8L > reader.BaseStream.Length - reader.BaseStream.Position) { throw new InvalidDataException("Truncated nonlocal destinations."); }
        var destinations = new WarpStateDispatchTarget[count];
        foreach (ref WarpStateDispatchTarget destination in destinations.AsSpan()) { destination = new(reader.ReadInt32(), reader.ReadInt32()); }
        return new(destinations, resultWords);
    }

    private static void WriteTarget(BinaryWriter writer, WarpBranchTarget target)
    {
        writer.Write(target.Block); WriteValues(writer, target.Arguments);
    }

    private static WarpBranchTarget ReadTarget(BinaryReader reader, Usage usage) => new(reader.ReadInt32(), ReadValues(reader, usage));
}
