namespace WarpCLR.Compiler;

internal static class WarpPortableSourceDelegateLayout
{
    internal const string Semantics = "warp.delegate-record/closed-target-immutable-invocation-list/0.1";
    internal const int TargetWord = 0;
    internal const int MethodWord = 3;
    internal const int InvocationListWord = 4;
    internal const int PrefixWords = 7;
    internal const int PrefixBytes = PrefixWords * sizeof(uint);
}
