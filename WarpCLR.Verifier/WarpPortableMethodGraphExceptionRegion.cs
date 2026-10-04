namespace WarpCLR.Verifier;

internal sealed record WarpPortableMethodGraphExceptionRegion(
    int Kind,
    int TryOffset,
    int TryLength,
    int HandlerOffset,
    int HandlerLength,
    int FilterOffset,
    string? CatchType);
