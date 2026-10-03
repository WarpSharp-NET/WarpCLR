using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

public sealed class WarpBranchTarget
{
    private readonly ReadOnlyCollection<int> arguments;

    public WarpBranchTarget(int block, IEnumerable<int> arguments)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(block);
        ArgumentNullException.ThrowIfNull(arguments);

        Block = block;
        this.arguments = Array.AsReadOnly(WarpCompilationAdmission.Materialize(arguments, "<IR-edge>",
            WarpCompilationResourceKind.OperandReferences, WarpCompilationAdmission.MaximumValueSlotsPerEntry));
    }

    public int Block { get; }

    public IReadOnlyList<int> Arguments => arguments;
}
