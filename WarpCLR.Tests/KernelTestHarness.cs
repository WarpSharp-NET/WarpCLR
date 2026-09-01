using System.Reflection;
using WarpCLR.Backend.Cpu;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Sdk;

namespace WarpCLR.Tests;

internal static class KernelTestHarness
{
    public static uint[] CompileAndEmulate(
        MethodInfo method,
        int inputBufferCount,
        WarpBackendKind backend,
        IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint>? scalarArguments = null)
    {
        var pipeline = new WarpBuildPipeline();
        WarpCompilation compilation = pipeline.CompileIntegerMap(method, inputBufferCount);

        Assert.HasCount(WarpBackendCatalog.Required.Count, compilation.Artifacts);
        foreach (WarpBackendKind requiredBackend in WarpBackendCatalog.Required)
        {
            Assert.IsTrue(compilation.Artifacts.TryGetValue(requiredBackend, out WarpBackendArtifact? artifact));
            Assert.IsNotNull(artifact);
            BackendArtifactAssertions.IsValid(artifact, requiredBackend, compilation.Kernel);
        }

        var emulator = new WarpIntegerMapSemanticEmulator();
        return emulator.Execute(compilation.Artifacts[backend], compilation.Kernel, inputs, scalarArguments);
    }
}
