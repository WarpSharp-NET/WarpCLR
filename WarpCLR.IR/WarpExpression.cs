namespace WarpCLR.IR;

public abstract record WarpExpression;

public sealed record WarpInputExpression(int BufferIndex) : WarpExpression;

public sealed record WarpScalarExpression(int ArgumentIndex) : WarpExpression;

public sealed record WarpConstantExpression(uint Value) : WarpExpression;

public sealed record WarpUnaryExpression(
    WarpUnaryOperator Operator,
    WarpExpression Operand) : WarpExpression;

public sealed record WarpBinaryExpression(
    WarpBinaryOperator Operator,
    WarpExpression Left,
    WarpExpression Right) : WarpExpression;

public enum WarpUnaryOperator
{
    BitwiseNot,
}

public enum WarpBinaryOperator
{
    Add,
    Subtract,
    Multiply,
    BitwiseAnd,
    BitwiseOr,
    ExclusiveOr,
    ShiftLeft,
    ShiftRightLogical,
}
