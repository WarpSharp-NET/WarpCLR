using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

public enum WarpIrOpCode
{
    LoadInput,
    LoadScalar,
    LoadArgument,
    Constant,
    BitwiseNot,
    Add,
    Subtract,
    Multiply,
    BitwiseAnd,
    BitwiseOr,
    ExclusiveOr,
    ShiftLeft,
    ShiftRightLogical,
    Equal,
    NotEqual,
    LessThanUnsigned,
    LessThanOrEqualUnsigned,
    GreaterThanUnsigned,
    GreaterThanOrEqualUnsigned,
    Select,
    Call,
}
