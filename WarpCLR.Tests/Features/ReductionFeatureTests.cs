using WarpCLR.Backend.Amd;
using WarpCLR.Backend.Cpu;
using WarpCLR.Backend.Intel;
using WarpCLR.Backend.Nvidia;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Sdk;

namespace WarpCLR.Tests.Features;

[TestClass]
public sealed class ReductionFeatureTests
{
    [TestMethod]
    [FourBackends]
    public void Wrapping_sum_dispatches_with_exact_unsigned_semantics(WarpBackendKind backend)
    {
        WarpDevelopmentSession session = LoadSession(backend, out string directory);

        try
        {
            uint[] input = [0u, 1u, uint.MaxValue, 0x80000000u, 0x12345678u];
            const uint scalar = 0xA5A5A5A5u;
            uint expected = 0u;
            foreach (uint value in input)
            {
                expected = unchecked(expected + TestKernels.ManifestReduction(value, scalar));
            }

            uint actual = session.DispatchUInt32Reduction(
                ManifestAssemblyFixture.ReductionEntryIdentity,
                [input],
                [scalar]);

            Assert.AreEqual(expected, actual);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [FourBackends]
    public void Empty_wrapping_sum_returns_zero(WarpBackendKind backend)
    {
        WarpDevelopmentSession session = LoadSession(backend, out string directory);

        try
        {
            uint actual = session.DispatchUInt32Reduction(
                ManifestAssemblyFixture.ReductionEntryIdentity,
                [Array.Empty<uint>()],
                [17u]);

            Assert.AreEqual(0u, actual);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [FourBackends]
    public void Minimum_reduction_uses_unsigned_order_and_identity(WarpBackendKind backend)
    {
        WarpCompilation compilation = CompileIdentityReduction(WarpReductionOperation.Minimum);
        var emulator = new WarpIntegerMapSemanticEmulator();

        BackendArtifactAssertions.IsValid(
            compilation.Artifacts[backend],
            backend,
            compilation.Kernel);
        Assert.AreEqual(
            1u,
            emulator.ExecuteReduction(
                compilation.Artifacts[backend],
                compilation.Kernel,
                [new uint[] { uint.MaxValue, 0x80000000u, 42u, 1u }]));
        Assert.AreEqual(
            uint.MaxValue,
            emulator.ExecuteReduction(
                compilation.Artifacts[backend],
                compilation.Kernel,
                [Array.Empty<uint>()]));
    }

    [TestMethod]
    [FourBackends]
    public void Maximum_reduction_uses_unsigned_order_and_identity(WarpBackendKind backend)
    {
        WarpCompilation compilation = CompileIdentityReduction(WarpReductionOperation.Maximum);
        var emulator = new WarpIntegerMapSemanticEmulator();

        BackendArtifactAssertions.IsValid(
            compilation.Artifacts[backend],
            backend,
            compilation.Kernel);
        Assert.AreEqual(
            uint.MaxValue,
            emulator.ExecuteReduction(
                compilation.Artifacts[backend],
                compilation.Kernel,
                [new uint[] { 0u, 42u, 0x80000000u, uint.MaxValue }]));
        Assert.AreEqual(
            0u,
            emulator.ExecuteReduction(
                compilation.Artifacts[backend],
                compilation.Kernel,
                [Array.Empty<uint>()]));
    }

    private static WarpCompilation CompileIdentityReduction(WarpReductionOperation operation)
    {
        var kernel = new WarpIntegerMapKernel(
            $"test.{operation}",
            1,
            0,
            new WarpInputExpression(0),
            operation);

        return new WarpCompiler().Compile(
            kernel,
            [
                new CpuBackendCompiler(),
                new NvidiaBackendCompiler(),
                new AmdBackendCompiler(),
                new IntelBackendCompiler(),
            ]);
    }

    private static WarpDevelopmentSession LoadSession(
        WarpBackendKind backend,
        out string directory)
    {
        byte[] assembly = ManifestAssemblyFixture.ReadAssembly();
        WarpAotPackage package = new WarpBuildPipeline().CompilePackage(assembly);
        directory = AotPackagingFeatureTests.CreateTestDirectory(backend);
        package.WriteToDirectory(directory);
        WarpLoadedModule module = new WarpDevelopmentModuleLoader().Load(assembly, directory);
        return new WarpDevelopmentSession(
            module,
            backend,
            WarpDevelopmentExecutionMode.SemanticEmulation);
    }
}
