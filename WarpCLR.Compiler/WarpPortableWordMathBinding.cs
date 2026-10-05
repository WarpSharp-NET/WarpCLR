using System.Collections.Immutable;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableWordMathBinding(string ManagedSignature, Type Implementation, string Semantics,
    ImmutableArray<string> ValueEntrypoints, ImmutableArray<uint> AdditionalValueWords,
    string? FaultEntrypoint, ImmutableArray<int> FaultOperandWords, ImmutableArray<WarpPortableWordMathFault> Faults)
{
    internal string Identity => "warp.math.binding/exact-word-value-fault/0.1/" + ManagedSignature + "/" + Semantics + "/value=" +
        string.Join(',', ValueEntrypoints) + "/additional=" + string.Join(',', AdditionalValueWords.Select(word => word.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "/fault=" + FaultEntrypoint +
        "/fault-words=" + string.Join(',', FaultOperandWords.Select(word => word.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "/fault-types=" +
        string.Join(',', Faults.Select(fault => fault.Descriptor.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + fault.ExceptionType));
}
