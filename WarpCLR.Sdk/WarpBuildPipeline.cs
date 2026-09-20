using System.Reflection;
using WarpCLR.Backend.AMDGPU;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Backend.SPIRV;
using WarpCLR.Backend.NVPTX;
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
                new CoreCLRBackendCompiler(),
                new NVPTXBackendCompiler(),
                new AMDGPUBackendCompiler(),
                new SPIRVBackendCompiler(),
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

    public WarpAotPackage CompilePackage(string assemblyPath) =>
        WarpAotPackageBuilder.Build(CompileModule(assemblyPath));

    public WarpAotPackage CompilePackage(ReadOnlyMemory<byte> assemblyBytes) =>
        WarpAotPackageBuilder.Build(CompileModule(assemblyBytes));

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
