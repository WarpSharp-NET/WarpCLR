namespace WarpCLR.Runtime.Host;

internal sealed record WarpSourcePublicationStep(WarpSourcePublicationKind Kind, ulong CommandOrdinal, string ArenaHash, uint RootRevision);
