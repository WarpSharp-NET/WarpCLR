namespace WarpCLR.Compiler;

internal static class WarpPortableSourceExceptionLayout
{
    internal const string Semantics = "warp.exception-record/message-inner-trace-refs-logical-site/0.1";
    internal const int MessageWord = 0;
    internal const int InnerExceptionWord = 3;
    internal const int TraceReferenceWord = 6;
    internal const int TraceIdentityWord = 9;
    internal const int ThrowMethodWord = 10;
    internal const int ThrowOffsetWord = 11;
    internal const int PrefixWords = 12;
    internal const int PrefixBytes = PrefixWords * sizeof(uint);
}
