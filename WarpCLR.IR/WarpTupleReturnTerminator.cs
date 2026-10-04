using System.Collections.ObjectModel;

namespace WarpCLR.IR;

internal class WarpTupleReturnTerminator : WarpBlockTerminator
{
    public WarpTupleReturnTerminator(IEnumerable<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int[] words = WarpCompilationAdmission.Materialize(values, "<IR-result>", WarpCompilationResourceKind.Parameters,
            WarpCompilationAdmission.MaximumParametersPerBody);
        foreach (int value in words)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(values));
        }

        Values = Array.AsReadOnly(words);
    }

    public ReadOnlyCollection<int> Values { get; }
}
