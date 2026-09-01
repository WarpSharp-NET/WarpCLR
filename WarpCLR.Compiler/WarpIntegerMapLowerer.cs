using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal sealed class WarpIntegerMapLowerer
{
    public WarpLinearKernel Lower(WarpIntegerMapKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var instructions = new List<WarpIrInstruction>();
        var results = new Dictionary<WarpExpression, int>(ReferenceEqualityComparer.Instance);
        int result = LowerExpression(kernel.Result, instructions, results);

        return new WarpLinearKernel(
            kernel.Name,
            kernel.InputBufferCount,
            kernel.ScalarArgumentCount,
            instructions,
            result);
    }

    private static int LowerExpression(
        WarpExpression expression,
        List<WarpIrInstruction> instructions,
        Dictionary<WarpExpression, int> results)
    {
        if (results.TryGetValue(expression, out int existing))
        {
            return existing;
        }

        int result = instructions.Count;

        switch (expression)
        {
            case WarpInputExpression input:
                instructions.Add(new WarpIrInstruction(
                    result,
                    WarpIrOpCode.LoadInput,
                    Immediate: checked((uint)input.BufferIndex)));
                break;

            case WarpScalarExpression scalar:
                instructions.Add(new WarpIrInstruction(
                    result,
                    WarpIrOpCode.LoadScalar,
                    Immediate: checked((uint)scalar.ArgumentIndex)));
                break;

            case WarpConstantExpression constant:
                instructions.Add(new WarpIrInstruction(
                    result,
                    WarpIrOpCode.Constant,
                    Immediate: constant.Value));
                break;

            case WarpUnaryExpression unary:
                {
                    int operand = LowerExpression(unary.Operand, instructions, results);
                    result = instructions.Count;
                    instructions.Add(new WarpIrInstruction(
                        result,
                        GetOpCode(unary.Operator),
                        Left: operand));
                    break;
                }

            case WarpBinaryExpression binary:
                {
                    int left = LowerExpression(binary.Left, instructions, results);
                    int right = LowerExpression(binary.Right, instructions, results);
                    result = instructions.Count;
                    instructions.Add(new WarpIrInstruction(
                        result,
                        GetOpCode(binary.Operator),
                        Left: left,
                        Right: right));
                    break;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(expression));
        }

        results.Add(expression, result);
        return result;
    }

    private static WarpIrOpCode GetOpCode(WarpUnaryOperator @operator) => @operator switch
    {
        WarpUnaryOperator.BitwiseNot => WarpIrOpCode.BitwiseNot,
        _ => throw new ArgumentOutOfRangeException(nameof(@operator)),
    };

    private static WarpIrOpCode GetOpCode(WarpBinaryOperator @operator) => @operator switch
    {
        WarpBinaryOperator.Add => WarpIrOpCode.Add,
        WarpBinaryOperator.Subtract => WarpIrOpCode.Subtract,
        WarpBinaryOperator.Multiply => WarpIrOpCode.Multiply,
        WarpBinaryOperator.BitwiseAnd => WarpIrOpCode.BitwiseAnd,
        WarpBinaryOperator.BitwiseOr => WarpIrOpCode.BitwiseOr,
        WarpBinaryOperator.ExclusiveOr => WarpIrOpCode.ExclusiveOr,
        WarpBinaryOperator.ShiftLeft => WarpIrOpCode.ShiftLeft,
        WarpBinaryOperator.ShiftRightLogical => WarpIrOpCode.ShiftRightLogical,
        _ => throw new ArgumentOutOfRangeException(nameof(@operator)),
    };
}
