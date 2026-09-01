using System.Reflection;
using WarpCLR.Backend.Amd;
using WarpCLR.Backend.Cpu;
using WarpCLR.Backend.Intel;
using WarpCLR.Backend.Nvidia;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Sdk;

public sealed class WarpBuildPipeline
{
    private readonly WarpIntegerMapVerifier verifier;
    private readonly WarpCompiler compiler;
    private readonly IReadOnlyList<IWarpBackendCompiler> backendCompilers;

    public WarpBuildPipeline()
        : this(
            new WarpIntegerMapVerifier(),
            new WarpCompiler(),
            [
                new CpuBackendCompiler(),
                new NvidiaBackendCompiler(),
                new AmdBackendCompiler(),
                new IntelBackendCompiler(),
            ])
    {
    }

    public WarpBuildPipeline(
        WarpIntegerMapVerifier verifier,
        WarpCompiler compiler,
        IReadOnlyList<IWarpBackendCompiler> backendCompilers)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(backendCompilers);

        this.verifier = verifier;
        this.compiler = compiler;
        this.backendCompilers = backendCompilers.ToArray();
    }

    public WarpCompilation CompileIntegerMap(MethodInfo method, int inputBufferCount)
    {
        WarpIntegerMapKernel verifiedKernel = verifier.Verify(
            new WarpIntegerMapRequest(method, inputBufferCount));

        return compiler.Compile(verifiedKernel, backendCompilers);
    }
}
