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
        Parameters = Array.AsReadOnly(WarpCompilationAdmission.Materialize(parameters, "<IR-block>", WarpCompilationResourceKind.ValueSlots, WarpCompilationAdmission.MaximumValueSlotsPerEntry));
        Instructions = Array.AsReadOnly(WarpCompilationAdmission.Materialize(instructions, "<IR-block>", WarpCompilationResourceKind.Instructions, WarpCompilationAdmission.MaximumInstructionsPerEntry));
        Terminator = terminator;
    }

    public int Id { get; }

    public ReadOnlyCollection<WarpBlockParameter> Parameters { get; }

    public ReadOnlyCollection<WarpIrInstruction> Instructions { get; }

    public WarpBlockTerminator Terminator { get; }
}
