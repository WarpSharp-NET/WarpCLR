using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

public sealed class WarpBranchTerminator : WarpBlockTerminator
{
    public WarpBranchTerminator(WarpBranchTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
    }

    public WarpBranchTarget Target { get; }
}
