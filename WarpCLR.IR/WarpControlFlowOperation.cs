using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

public enum WarpControlFlowOperation
{
    BlockArguments,
    Branch,
    ConditionalBranch,
    Return,
}
