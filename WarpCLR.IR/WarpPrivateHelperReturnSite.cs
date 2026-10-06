using System.Collections.ObjectModel;

namespace WarpCLR.IR;

// Derived solely from the immutable admitted Call graph. This is a consistency
// descriptor, never a proof of execution, publication or controller release.
internal sealed record WarpPrivateHelperReturnSite(int CallerFunction, int HelperFunction,
    int CallProgramCounter, int Continuation, int ResultValue, int ResultWordCount,
    ReadOnlyCollection<int> HelperFunctions);
