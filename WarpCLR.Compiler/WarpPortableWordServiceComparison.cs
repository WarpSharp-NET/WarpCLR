using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpPortableWordServiceComparison
{
    internal static bool Equal(WarpControlFlowFunction left, WarpControlFlowFunction right) =>
        left.ParameterCount == right.ParameterCount && left.ResultWordCount == right.ResultWordCount && left.Blocks.Count == right.Blocks.Count &&
        left.Blocks.Zip(right.Blocks).All(pair => Equal(pair.First, pair.Second));

    private static bool Equal(WarpBasicBlock left, WarpBasicBlock right) => left.Id == right.Id && left.Parameters.Count == right.Parameters.Count &&
        left.Parameters.Zip(right.Parameters).All(pair => pair.First.Value == pair.Second.Value && pair.First.Type == pair.Second.Type) &&
        left.Instructions.Count == right.Instructions.Count && left.Instructions.Zip(right.Instructions).All(pair => Equal(pair.First, pair.Second)) &&
        Equal(left.Terminator, right.Terminator);

    private static bool Equal(WarpIrInstruction left, WarpIrInstruction right) => left.Result == right.Result && left.OpCode == right.OpCode &&
        left.ResultType == right.ResultType && left.ResultWordCount == right.ResultWordCount && left.Left == right.Left && left.Right == right.Right &&
        left.Immediate == right.Immediate && left.Third == right.Third && left.Callee == right.Callee && left.Arguments.SequenceEqual(right.Arguments);

    private static bool Equal(WarpBlockTerminator left, WarpBlockTerminator right) => (left, right) switch
    {
        (WarpBranchTerminator first, WarpBranchTerminator second) => Equal(first.Target, second.Target),
        (WarpConditionalBranchTerminator first, WarpConditionalBranchTerminator second) => first.Condition == second.Condition &&
            Equal(first.WhenNonZero, second.WhenNonZero) && Equal(first.WhenZero, second.WhenZero),
        (WarpReturnTerminator first, WarpReturnTerminator second) => first.Value == second.Value,
        (WarpTupleReturnTerminator first, WarpTupleReturnTerminator second) => first.Values.SequenceEqual(second.Values),
        _ => false,
    };

    private static bool Equal(WarpBranchTarget left, WarpBranchTarget right) => left.Block == right.Block && left.Arguments.SequenceEqual(right.Arguments);
}
