namespace WarpCLR.Verifier;

internal sealed record WarpPortableTypedFault(WarpPortableTypedFaultKind Kind, int SourceOffset, int EffectIndex);
