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
    private readonly WarpModuleVerifier moduleVerifier;
    private readonly WarpCompiler compiler;
    private readonly IReadOnlyList<IWarpBackendCompiler> backendCompilers;

    public WarpBuildPipeline()
        : this(
            new WarpIntegerMapVerifier(),
            new WarpModuleVerifier(),
            new WarpCompiler(),
            [
                new CpuBackendCompiler(),
                new NvidiaBackendCompiler(),
                new AmdBackendCompiler(),
                new IntelBackendCompiler(),
            ])
    {
    }

    internal WarpBuildPipeline(
        WarpIntegerMapVerifier verifier,
        WarpModuleVerifier moduleVerifier,
        WarpCompiler compiler,
        IReadOnlyList<IWarpBackendCompiler> backendCompilers)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(moduleVerifier);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(backendCompilers);

        this.verifier = verifier;
        this.moduleVerifier = moduleVerifier;
        this.compiler = compiler;
        this.backendCompilers = backendCompilers.ToArray();
    }

    internal WarpCompilation CompileIntegerMap(MethodInfo method, int inputBufferCount)
    {
        WarpIntegerMapKernel verifiedKernel = verifier.Verify(
            new WarpIntegerMapRequest(method, inputBufferCount));

        return compiler.Compile(verifiedKernel, backendCompilers);
    }

    public WarpModuleCompilation CompileModule(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        return CompileVerifiedModule(moduleVerifier.Verify(assemblyPath));
    }

    public WarpModuleCompilation CompileModule(ReadOnlyMemory<byte> assemblyBytes) =>
        CompileVerifiedModule(moduleVerifier.Verify(assemblyBytes));

    private WarpModuleCompilation CompileVerifiedModule(WarpVerifiedModule module)
    {
        var compilations = new Dictionary<string, WarpCompilation>(StringComparer.Ordinal);
        foreach (WarpVerifiedEntry entry in module.Entries)
        {
            compilations.Add(entry.Identity, compiler.Compile(entry.Kernel, backendCompilers));
        }

        return new WarpModuleCompilation(module, compilations);
    }
}
