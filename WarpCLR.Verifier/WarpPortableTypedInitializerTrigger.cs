namespace WarpCLR.Verifier;

internal sealed record WarpPortableTypedInitializerTrigger(string DeclaringType, string Initializer,
    WarpPortableInitializerTriggerKind Kind, bool BeforeFieldInit);

