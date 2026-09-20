using System.Globalization;
using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public static class WarpCoreCLRPlanCodec
{
    private const string Header = "warp.coreclr.cfg/0.3";

    public static byte[] Serialize(WarpControlFlowKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var plan = new StringBuilder();
        plan.Append(Header).Append('\n');
        plan.Append(WarpDeviceAbi.DevelopmentConformanceMarker).Append('\n');
        plan.Append("entry=").Append(WarpDeviceAbi.GetEntryPoint(kernel)).Append('\n');
        plan.Append("operation=").Append(GetOperationName(kernel.Reduction)).Append('\n');
        plan.Append("blocks=").Append(Invariant(kernel.Blocks.Count)).Append('\n');

        foreach (WarpBasicBlock block in kernel.Blocks)
        {
            plan.Append("block=").Append(Invariant(block.Id)).Append('\n');
            foreach (WarpBlockParameter parameter in block.Parameters)
            {
                plan.Append("parameter=")
                    .Append(Invariant(parameter.Value))
                    .Append(',')
                    .Append(parameter.Type)
                    .Append('\n');
            }

            foreach (WarpIrInstruction instruction in block.Instructions)
            {
                plan.Append("instruction=")
                    .Append(Invariant(instruction.Result))
                    .Append(',')
                    .Append(instruction.ResultType)
                    .Append(',')
                    .Append(instruction.OpCode)
                    .Append(',')
                    .Append(Invariant(instruction.Left))
                    .Append(',')
                    .Append(Invariant(instruction.Right))
                    .Append(',')
                    .Append(Invariant(instruction.Immediate))
                    .Append(',')
                    .Append(Invariant(instruction.Third))
                    .Append('\n');
            }

            AppendTerminator(plan, block.Terminator);
            plan.Append("endblock\n");
        }

        return Encoding.UTF8.GetBytes(plan.ToString());
    }

    public static WarpControlFlowKernel Deserialize(
        ReadOnlySpan<byte> content,
        string name,
        int inputBufferCount,
        int scalarArgumentCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string text = new UTF8Encoding(false, true).GetString(content);
        string[] lines = text.Split('\n');
        if (lines.Length < 8 || lines[^1].Length != 0)
        {
            throw new InvalidDataException("The CoreCLR plan does not have canonical line endings.");
        }

        if (!string.Equals(lines[0], Header, StringComparison.Ordinal) ||
            !string.Equals(lines[1], WarpDeviceAbi.DevelopmentConformanceMarker, StringComparison.Ordinal) ||
            !lines[2].StartsWith("entry=", StringComparison.Ordinal) ||
            !lines[3].StartsWith("operation=", StringComparison.Ordinal) ||
            !TryParsePrefixedInt(lines[4], "blocks=", out int blockCount) ||
            blockCount <= 0)
        {
            throw new InvalidDataException("The CoreCLR plan header is invalid.");
        }

        WarpReductionOperation? reduction = ParseOperation(lines[3]["operation=".Length..]);
        string entryPoint = reduction.HasValue
            ? WarpDeviceAbi.IntegerReductionEntryPoint
            : WarpDeviceAbi.IntegerMapEntryPoint;
        if (!string.Equals(lines[2], $"entry={entryPoint}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The CoreCLR plan entry point does not match its operation.");
        }

        int lineIndex = 5;
        var blocks = new List<WarpBasicBlock>(blockCount);
        for (int expectedBlock = 0; expectedBlock < blockCount; expectedBlock++)
        {
            if (lineIndex >= lines.Length - 1 ||
                !TryParsePrefixedInt(lines[lineIndex++], "block=", out int blockId) ||
                blockId != expectedBlock)
            {
                throw new InvalidDataException("The CoreCLR plan block sequence is invalid.");
            }

            var parameters = new List<WarpBlockParameter>();
            while (lineIndex < lines.Length - 1 &&
                   lines[lineIndex].StartsWith("parameter=", StringComparison.Ordinal))
            {
                parameters.Add(ParseParameter(lines[lineIndex++]));
            }

            var instructions = new List<WarpIrInstruction>();
            while (lineIndex < lines.Length - 1 &&
                   lines[lineIndex].StartsWith("instruction=", StringComparison.Ordinal))
            {
                instructions.Add(ParseInstruction(lines[lineIndex++]));
            }

            if (lineIndex >= lines.Length - 1 ||
                !lines[lineIndex].StartsWith("terminator=", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The CoreCLR plan block terminator is missing.");
            }

            WarpBlockTerminator terminator = ParseTerminator(lines[lineIndex++]);
            if (lineIndex >= lines.Length - 1 ||
                !string.Equals(lines[lineIndex++], "endblock", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The CoreCLR plan block ending is invalid.");
            }

            blocks.Add(new WarpBasicBlock(blockId, parameters, instructions, terminator));
        }

        if (lineIndex != lines.Length - 1)
        {
            throw new InvalidDataException("The CoreCLR plan contains trailing data.");
        }

        var kernel = new WarpControlFlowKernel(
            name,
            inputBufferCount,
            scalarArgumentCount,
            blocks,
            reduction);
        if (!content.SequenceEqual(Serialize(kernel)))
        {
            throw new InvalidDataException("The CoreCLR plan is not canonical.");
        }

        return kernel;
    }

    private static void AppendTerminator(StringBuilder plan, WarpBlockTerminator terminator)
    {
        plan.Append("terminator=");
        switch (terminator)
        {
            case WarpBranchTerminator branch:
                plan.Append("branch,");
                AppendTarget(plan, branch.Target);
                break;

            case WarpConditionalBranchTerminator conditional:
                plan.Append("conditional,")
                    .Append(Invariant(conditional.Condition))
                    .Append(',');
                AppendTarget(plan, conditional.WhenNonZero);
                plan.Append(',');
                AppendTarget(plan, conditional.WhenZero);
                break;

            case WarpReturnTerminator @return:
                plan.Append("return,").Append(Invariant(@return.Value));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(terminator));
        }

        plan.Append('\n');
    }

    private static void AppendTarget(StringBuilder plan, WarpBranchTarget target)
    {
        plan.Append(Invariant(target.Block)).Append(',');
        if (target.Arguments.Count == 0)
        {
            plan.Append('-');
            return;
        }

        for (int index = 0; index < target.Arguments.Count; index++)
        {
            if (index != 0)
            {
                plan.Append(';');
            }

            plan.Append(Invariant(target.Arguments[index]));
        }
    }

    private static WarpBlockParameter ParseParameter(string line)
    {
        string[] fields = line["parameter=".Length..].Split(',');
        if (fields.Length != 2 ||
            !TryParseInt(fields[0], out int value) ||
            !Enum.TryParse(fields[1], ignoreCase: false, out WarpIrValueType type) ||
            !Enum.IsDefined(type))
        {
            throw new InvalidDataException("The CoreCLR plan contains an invalid block parameter.");
        }

        return new WarpBlockParameter(value, type);
    }

    private static WarpIrInstruction ParseInstruction(string line)
    {
        string[] fields = line["instruction=".Length..].Split(',');
        if (fields.Length != 7 ||
            !TryParseInt(fields[0], out int result) ||
            !Enum.TryParse(fields[1], ignoreCase: false, out WarpIrValueType resultType) ||
            !Enum.IsDefined(resultType) ||
            !Enum.TryParse(fields[2], ignoreCase: false, out WarpIrOpCode opCode) ||
            !Enum.IsDefined(opCode) ||
            !TryParseInt(fields[3], out int left) ||
            !TryParseInt(fields[4], out int right) ||
            !uint.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out uint immediate) ||
            !TryParseInt(fields[6], out int third))
        {
            throw new InvalidDataException("The CoreCLR plan contains an invalid instruction.");
        }

        return new WarpIrInstruction(result, opCode, left, right, immediate, third, resultType);
    }

    private static WarpBlockTerminator ParseTerminator(string line)
    {
        string[] fields = line["terminator=".Length..].Split(',');
        return fields[0] switch
        {
            "branch" when fields.Length == 3 =>
                new WarpBranchTerminator(ParseTarget(fields[1], fields[2])),
            "conditional" when fields.Length == 6 && TryParseInt(fields[1], out int condition) =>
                new WarpConditionalBranchTerminator(
                    condition,
                    ParseTarget(fields[2], fields[3]),
                    ParseTarget(fields[4], fields[5])),
            "return" when fields.Length == 2 && TryParseInt(fields[1], out int value) =>
                new WarpReturnTerminator(value),
            _ => throw new InvalidDataException("The CoreCLR plan contains an invalid terminator."),
        };
    }

    private static WarpBranchTarget ParseTarget(string blockText, string argumentText)
    {
        if (!TryParseInt(blockText, out int block))
        {
            throw new InvalidDataException("The CoreCLR plan contains an invalid branch target.");
        }

        int[] arguments = argumentText == "-"
            ? []
            : argumentText.Split(';').Select(ParseRequiredInt).ToArray();
        return new WarpBranchTarget(block, arguments);
    }

    private static int ParseRequiredInt(string value) => TryParseInt(value, out int result)
        ? result
        : throw new InvalidDataException("The CoreCLR plan contains an invalid integer.");

    private static bool TryParsePrefixedInt(string line, string prefix, out int value)
    {
        value = 0;
        return line.StartsWith(prefix, StringComparison.Ordinal) &&
            TryParseInt(line[prefix.Length..], out value);
    }

    private static bool TryParseInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static string GetOperationName(WarpReductionOperation? reduction) => reduction switch
    {
        null => "map",
        WarpReductionOperation.WrappingSum => "reduce-wrapping-sum",
        WarpReductionOperation.Minimum => "reduce-minimum",
        WarpReductionOperation.Maximum => "reduce-maximum",
        _ => throw new ArgumentOutOfRangeException(nameof(reduction)),
    };

    private static WarpReductionOperation? ParseOperation(string value) => value switch
    {
        "map" => null,
        "reduce-wrapping-sum" => WarpReductionOperation.WrappingSum,
        "reduce-minimum" => WarpReductionOperation.Minimum,
        "reduce-maximum" => WarpReductionOperation.Maximum,
        _ => throw new InvalidDataException($"CoreCLR plan operation '{value}' is not registered."),
    };

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(uint value) => value.ToString(CultureInfo.InvariantCulture);
}
