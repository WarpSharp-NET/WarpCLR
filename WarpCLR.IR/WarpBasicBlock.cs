using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

public sealed class WarpBasicBlock
{
    public WarpBasicBlock(
        int id,
        IEnumerable<WarpBlockParameter> parameters,
        IEnumerable<WarpIrInstruction> instructions,
        WarpBlockTerminator terminator)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(terminator);

        Id = id;
        Parameters = Array.AsReadOnly(parameters.ToArray());
        Instructions = Array.AsReadOnly(instructions.ToArray());
        Terminator = terminator;
    }

    public int Id { get; }

    public ReadOnlyCollection<WarpBlockParameter> Parameters { get; }

    public ReadOnlyCollection<WarpIrInstruction> Instructions { get; }

    public WarpBlockTerminator Terminator { get; }
}
