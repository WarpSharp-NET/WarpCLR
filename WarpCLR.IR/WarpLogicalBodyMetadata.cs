using System.Collections.ObjectModel;

namespace WarpCLR.IR;

internal sealed class WarpLogicalBodyMetadata
{
    internal WarpLogicalBodyMetadata(int privateWordCount, bool runtimeHelper, IEnumerable<int> sourceBlockCosts)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(privateWordCount);
        ArgumentNullException.ThrowIfNull(sourceBlockCosts);
        WarpCompilationAdmission.Require("<logical-body>", WarpCompilationResourceKind.ValueSlots,
            privateWordCount, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
        PrivateWordCount = privateWordCount;
        RuntimeHelper = runtimeHelper;
        SourceBlockCosts = Array.AsReadOnly(WarpCompilationAdmission.Materialize(sourceBlockCosts, "<source-charges>",
            WarpCompilationResourceKind.Blocks, WarpCompilationAdmission.MaximumBlocksPerEntry));
        if (SourceBlockCosts.Any(cost => cost < 0 || runtimeHelper && cost != 0))
        {
            throw new ArgumentException("Source charges must be nonnegative, and runtime helpers have no source charge.", nameof(sourceBlockCosts));
        }
    }

    internal int PrivateWordCount { get; }
    internal bool RuntimeHelper { get; }
    internal ReadOnlyCollection<int> SourceBlockCosts { get; }
}
