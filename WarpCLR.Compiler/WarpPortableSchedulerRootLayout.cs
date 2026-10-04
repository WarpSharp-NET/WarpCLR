using System.Collections.ObjectModel;

namespace WarpCLR.Compiler;

internal sealed class WarpPortableSchedulerRootLayout
{
    private readonly ReadOnlyCollection<uint> offsets;

    public WarpPortableSchedulerRootLayout(uint function, uint pc, IEnumerable<uint> referenceOffsets)
    {
        ArgumentNullException.ThrowIfNull(referenceOffsets);
        offsets = Array.AsReadOnly(referenceOffsets.ToArray());
        Function = function;
        PC = pc;
    }

    public uint Function { get; }

    public uint PC { get; }

    public ReadOnlyCollection<uint> ReferenceOffsets => offsets;
}
