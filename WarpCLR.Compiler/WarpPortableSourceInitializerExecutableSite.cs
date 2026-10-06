using System.Collections.Immutable;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceInitializerExecutableSite(WarpPortableSourceOperationOrigin Origin,
    WarpPortableSourceInitializerTrigger Trigger, int CapturedMethodIndex, int Function,
    int InitializerCapturedMethodIndex, int InitializerFunction, int EntryProgramCounter,
    ImmutableArray<WarpPortableSourceInitializerExecutablePoint> Points, ImmutableArray<WarpPortableWordRoot> Roots,
    WarpPortableWordInvocationPrelude? Prelude, string SiteHash);
