using System.Collections.ObjectModel;

namespace WarpCLR.IR;

internal sealed class WarpLogicalBodyMetadata
{
    internal WarpLogicalBodyMetadata(int privateWordCount, bool runtimeHelper, IEnumerable<int> sourceBlockCosts,
        bool? countsSourceDepth = null, int aliasOwnerFunction = -1, int aliasPrefixWords = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(privateWordCount);
        ArgumentNullException.ThrowIfNull(sourceBlockCosts);
        WarpCompilationAdmission.Require("<logical-body>", WarpCompilationResourceKind.ValueSlots,
            privateWordCount, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
        PrivateWordCount = privateWordCount;
        RuntimeHelper = runtimeHelper;
        CountsSourceDepth = countsSourceDepth ?? !runtimeHelper;
        ArgumentOutOfRangeException.ThrowIfLessThan(aliasOwnerFunction, -1);
        ArgumentOutOfRangeException.ThrowIfNegative(aliasPrefixWords);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(aliasPrefixWords, privateWordCount);
        if (runtimeHelper && CountsSourceDepth || aliasOwnerFunction == -1 && aliasPrefixWords != 0 ||
            aliasOwnerFunction != -1 && (runtimeHelper || CountsSourceDepth) ||
            !runtimeHelper && !CountsSourceDepth && aliasOwnerFunction == -1)
        {
            throw new ArgumentException("Only an admitted source filter aliases an owner prefix without adding source depth.", nameof(aliasOwnerFunction));
        }
        AliasOwnerFunction = aliasOwnerFunction;
        AliasPrefixWords = aliasPrefixWords;
        SourceBlockCosts = Array.AsReadOnly(WarpCompilationAdmission.Materialize(sourceBlockCosts, "<source-charges>",
            WarpCompilationResourceKind.Blocks, WarpCompilationAdmission.MaximumBlocksPerEntry));
        if (SourceBlockCosts.Any(cost => cost < 0 || runtimeHelper && cost != 0))
        {
            throw new ArgumentException("Source charges must be nonnegative, and runtime helpers have no source charge.", nameof(sourceBlockCosts));
        }
    }

    internal int PrivateWordCount { get; }
    internal bool RuntimeHelper { get; }
    internal bool CountsSourceDepth { get; }
    internal int AliasOwnerFunction { get; }
    internal int AliasPrefixWords { get; }
    internal ReadOnlyCollection<int> SourceBlockCosts { get; }
}
