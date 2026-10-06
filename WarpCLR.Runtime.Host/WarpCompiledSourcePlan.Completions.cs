using WarpCLR.Compiler;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourcePlan
{
    internal void RequireCompletionCompilerSeal()
    {
        WarpPortableWordProgramIdentity identity = WarpPortableWordLowerer.RequireRuntimeCompilerSeal(completionGraph, TypeSchema, Program);
        if (!ReferenceEquals(identity, CompilerIdentity))
        { throw new InvalidOperationException("Historical source completion requires the exact compiler-issued program identity."); }
    }
}
