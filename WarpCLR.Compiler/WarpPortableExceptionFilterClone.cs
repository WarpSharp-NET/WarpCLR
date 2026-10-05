using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// Compile-time projection of already lowered, verified filter blocks. It does
// not decode or execute runtime source opcodes.
internal static class WarpPortableExceptionFilterClone
{
    internal static Dictionary<int, int> Values(WarpControlFlowFunction original, Dictionary<int, int> blocks)
    {
        int[] definitions = blocks.Keys.Select(block => original.Blocks[block]).SelectMany(block =>
            block.Parameters.Select(parameter => parameter.Value).Concat(block.Instructions.Where(instruction => instruction.ResultWordCount != 0)
                .SelectMany(instruction => Enumerable.Range(instruction.Result, instruction.ResultWordCount)))).Distinct().Order().ToArray();
        return definitions.Select((value, index) => (value, index)).ToDictionary(pair => pair.value, pair => pair.index);
    }

    internal static WarpBasicBlock Block(WarpBasicBlock original, int target, Dictionary<int, int> blocks, Dictionary<int, int> values,
        int sourceFunction, int aliasFunction) => new(target, original.Parameters.Select(parameter => new WarpBlockParameter(values[parameter.Value], parameter.Type)),
            original.Instructions.Select(instruction => Instruction(instruction, values)), Terminator(original.Terminator, blocks, values, sourceFunction, aliasFunction));

    private static WarpIrInstruction Instruction(WarpIrInstruction instruction, Dictionary<int, int> values)
    {
        if (instruction.OpCode == WarpIrOpCode.Call)
        {
            return new(Value(instruction.Result, values), instruction.Callee, instruction.Arguments.Select(argument => Value(argument, values)), instruction.ResultWordCount);
        }
        return new(Value(instruction.Result, values), instruction.OpCode, Value(instruction.Left, values), Value(instruction.Right, values),
            instruction.Immediate, Value(instruction.Third, values), instruction.ResultType, instruction.Callee,
            instruction.Arguments.Select(argument => Value(argument, values)));
    }

    private static int Value(int value, Dictionary<int, int> values) => value < 0 ? value : values.TryGetValue(value, out int remapped) ? remapped :
        throw new WarpVerificationException("WRPCLR2300", "A filter alias cannot capture a source SSA definition outside its admitted private prefix/evaluation storage.", 0);

    private static WarpBlockTerminator Terminator(WarpBlockTerminator terminator, Dictionary<int, int> blocks, Dictionary<int, int> values,
        int sourceFunction, int aliasFunction) => terminator switch
    {
        WarpBranchTerminator branch => new WarpBranchTerminator(Edge(branch.Target, blocks, values)),
        WarpConditionalBranchTerminator branch => new WarpConditionalBranchTerminator(Value(branch.Condition, values), Edge(branch.WhenNonZero, blocks, values), Edge(branch.WhenZero, blocks, values)),
        WarpStateDispatchTerminator dispatch => new WarpStateDispatchTerminator(dispatch.Destinations.Select(destination =>
            destination.Function == sourceFunction ? new WarpStateDispatchTarget(aliasFunction, blocks[destination.Block]) : destination), dispatch.ResultWordCount),
        _ => throw new WarpVerificationException("WRPCLR2300", "A filter alias must terminate through its admitted decision continuation.", 0),
    };

    private static WarpBranchTarget Edge(WarpBranchTarget edge, Dictionary<int, int> blocks, Dictionary<int, int> values) =>
        new(blocks[edge.Block], edge.Arguments.Select(argument => Value(argument, values)));
}
