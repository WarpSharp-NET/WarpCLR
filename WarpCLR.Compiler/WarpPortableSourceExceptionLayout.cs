namespace WarpCLR.Compiler;

internal static class WarpPortableSourceExceptionLayout
{
    internal const string Semantics = "warp.exception-record/base-message-inner-trace-param-actual-type-name-hresult-generation-single-initialization/0.2";
    internal const int MessageWord = 0;
    internal const int InnerExceptionWord = 3;
    internal const int TraceReferenceWord = 6;
    internal const int TraceIdentityWord = 9;
    internal const int ThrowMethodWord = 10;
    internal const int ThrowOffsetWord = 11;
    internal const int ParamNameWord = 12;
    internal const int ActualValueWord = 15;
    internal const int TypeNameWord = 18;
    internal const int HResultWord = 21;
    internal const int PrefixWords = 22;
    internal const int PrefixBytes = PrefixWords * sizeof(uint);
    internal const uint ExceptionTypeWords = 2;
    internal const uint ExceptionTypeKind = 0;
    internal const uint ExceptionTypeHResult = 1;
    internal const uint DataStateWords = 2;
    internal const uint DataGeneration = 0;
    internal const uint DataInitialized = 1;
    internal const uint InputWords = 15;
}
