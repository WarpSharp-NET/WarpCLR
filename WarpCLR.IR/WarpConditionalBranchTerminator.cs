using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

public sealed class WarpConditionalBranchTerminator : WarpBlockTerminator
{
    public WarpConditionalBranchTerminator(
        int condition,
        WarpBranchTarget whenNonZero,
        WarpBranchTarget whenZero)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(condition);
        ArgumentNullException.ThrowIfNull(whenNonZero);
        ArgumentNullException.ThrowIfNull(whenZero);

        Condition = condition;
        WhenNonZero = whenNonZero;
        WhenZero = whenZero;
    }

    public int Condition { get; }

    public WarpBranchTarget WhenNonZero { get; }

    public WarpBranchTarget WhenZero { get; }
}
