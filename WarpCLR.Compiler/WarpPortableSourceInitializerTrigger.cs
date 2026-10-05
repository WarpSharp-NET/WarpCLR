using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceInitializerTrigger(string MethodIdentity, int? SourceOffset, ushort? SourceOpCode,
    int? EffectIndex, bool EntryInvocation, uint Type, string Initializer, WarpPortableInitializerTriggerKind Kind,
    bool BeforeFieldInit, bool ReentrantOwnInitializer);

