using System.Collections.ObjectModel;

namespace WarpCLR.IR;

internal sealed class WarpStateDispatchTerminator : WarpBlockTerminator
{
    internal WarpStateDispatchTerminator(IEnumerable<WarpStateDispatchTarget> destinations, int resultWordCount = 1)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentOutOfRangeException.ThrowIfNegative(resultWordCount);
        WarpCompilationAdmission.Require("<state-dispatch>", WarpCompilationResourceKind.ValueSlots,
            resultWordCount, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
        Destinations = Array.AsReadOnly(WarpCompilationAdmission.Materialize(destinations, "<state-dispatch>",
            WarpCompilationResourceKind.Blocks, WarpCompilationAdmission.MaximumBlocksPerEntry));
        if (Destinations.Count == 0 || Destinations.Any(target => target.Function < 0 || target.Block < 0) ||
            Destinations.Distinct().Count() != Destinations.Count)
        {
            throw new ArgumentException("A nonlocal dispatch requires unique admitted continuation destinations.", nameof(destinations));
        }
        ResultWordCount = resultWordCount;
    }

    internal ReadOnlyCollection<WarpStateDispatchTarget> Destinations { get; }
    internal int ResultWordCount { get; }
}
