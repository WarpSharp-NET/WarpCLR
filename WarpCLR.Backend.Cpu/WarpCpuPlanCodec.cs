using System.Globalization;
using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Backend.Cpu;

public static class WarpCpuPlanCodec
{
    private const string Header = "warp.cpu.linear/0.1";

    public static byte[] Serialize(WarpLinearKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var plan = new StringBuilder();
        plan.Append(Header).Append('\n');
        plan.Append(WarpDeviceAbi.DevelopmentConformanceMarker).Append('\n');
        plan.Append("entry=").Append(WarpDeviceAbi.GetEntryPoint(kernel)).Append('\n');
        plan.Append("operation=").Append(GetOperationName(kernel.Reduction)).Append('\n');

        foreach (WarpIrInstruction instruction in kernel.Instructions)
        {
            plan.Append(instruction.Result)
                .Append('=')
                .Append(instruction.OpCode)
                .Append(',')
                .Append(instruction.Left)
                .Append(',')
                .Append(instruction.Right)
                .Append(',')
                .Append(instruction.Immediate.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        plan.Append("result=")
            .Append(kernel.Result.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
        return Encoding.UTF8.GetBytes(plan.ToString());
    }

    public static WarpLinearKernel Deserialize(
        ReadOnlySpan<byte> content,
        string name,
        int inputBufferCount,
        int scalarArgumentCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string text = new UTF8Encoding(false, true).GetString(content);
        string[] lines = text.Split('\n');
        if (lines.Length < 6 || lines[^1].Length != 0)
        {
            throw new InvalidDataException("The CPU plan does not have canonical line endings.");
        }

        if (!string.Equals(lines[0], Header, StringComparison.Ordinal) ||
            !string.Equals(lines[1], WarpDeviceAbi.DevelopmentConformanceMarker, StringComparison.Ordinal) ||
            !lines[2].StartsWith("entry=", StringComparison.Ordinal) ||
            !lines[3].StartsWith("operation=", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The CPU plan header is invalid.");
        }

        WarpReductionOperation? reduction = ParseOperation(lines[3]["operation=".Length..]);
        string entryPoint = reduction.HasValue
            ? WarpDeviceAbi.IntegerReductionEntryPoint
            : WarpDeviceAbi.IntegerMapEntryPoint;
        if (!string.Equals(lines[2], $"entry={entryPoint}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The CPU plan entry point does not match its operation.");
        }

        var instructions = new List<WarpIrInstruction>(lines.Length - 5);
        for (int lineIndex = 4; lineIndex < lines.Length - 2; lineIndex++)
        {
            instructions.Add(ParseInstruction(lines[lineIndex]));
        }

        const string resultPrefix = "result=";
        string resultLine = lines[^2];
        if (!resultLine.StartsWith(resultPrefix, StringComparison.Ordinal) ||
            !int.TryParse(
                resultLine.AsSpan(resultPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int result))
        {
            throw new InvalidDataException("The CPU plan result is invalid.");
        }

        var kernel = new WarpLinearKernel(
            name,
            inputBufferCount,
            scalarArgumentCount,
            instructions,
            result,
            reduction);
        if (!content.SequenceEqual(Serialize(kernel)))
        {
            throw new InvalidDataException("The CPU plan is not canonical.");
        }

        return kernel;
    }

    private static WarpIrInstruction ParseInstruction(string line)
    {
        int equals = line.IndexOf('=');
        string[] fields = equals < 1
            ? []
            : line[(equals + 1)..].Split(',');
        if (fields.Length != 4 ||
            !int.TryParse(line.AsSpan(0, equals), NumberStyles.None, CultureInfo.InvariantCulture, out int result) ||
            !Enum.TryParse(fields[0], ignoreCase: false, out WarpIrOpCode opCode) ||
            !Enum.IsDefined(opCode) ||
            !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int left) ||
            !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int right) ||
            !uint.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out uint immediate))
        {
            throw new InvalidDataException("The CPU plan contains an invalid instruction.");
        }

        return new WarpIrInstruction(result, opCode, left, right, immediate);
    }

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
        _ => throw new InvalidDataException($"CPU plan operation '{value}' is not registered."),
    };
}
