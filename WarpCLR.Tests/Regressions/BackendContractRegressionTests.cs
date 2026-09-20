using System.Reflection;
using System.Text;
using WarpCLR.Backend.AMDGPU;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Backend.SPIRV;
using WarpCLR.Backend.NVPTX;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Sdk;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Regressions;

[TestClass]
public sealed class BackendContractRegressionTests
{
    [TestMethod]
    [FourBackends]
    public void A_missing_backend_prevents_compilation(WarpBackendKind backend)
    {
        WarpIntegerMapKernel verifiedKernel = VerifyCombine();
        IWarpBackendCompiler[] compilers = CreateCompilers()
            .Where(compiler => compiler.Backend != backend)
            .ToArray();

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => new WarpCompiler().Compile(verifiedKernel, compilers));

        StringAssert.Contains(exception.Message, backend.ToString());
    }

    [TestMethod]
    [FourBackends]
    public void A_backend_with_a_smaller_feature_set_prevents_compilation(
        WarpBackendKind backend)
    {
        WarpIntegerMapKernel verifiedKernel = VerifyCombine();
        var incompleteContract = new WarpBackendContract(
            WarpProfileCatalog.ProfileId,
            WarpProfileCatalog.IntegerMapInstructions.Where(
                instruction => instruction != WarpIrOpCode.Multiply),
            WarpProfileCatalog.BackendContract.Reductions);
        IWarpBackendCompiler[] compilers = CreateCompilers()
            .Select(
                compiler => compiler.Backend == backend
                    ? new ContractOverrideCompiler(compiler, incompleteContract)
                    : compiler)
            .ToArray();

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => new WarpCompiler().Compile(verifiedKernel, compilers));

        StringAssert.Contains(exception.Message, backend.ToString());
        StringAssert.Contains(exception.Message, nameof(WarpIrOpCode.Multiply));
    }

    [TestMethod]
    [FourBackends]
    public void An_artifact_cannot_use_another_backend_format(WarpBackendKind backend)
    {
        WarpArtifactFormat required = WarpArtifactFormatCatalog.ForBackend(backend);
        WarpArtifactFormat wrong = Enum.GetValues<WarpArtifactFormat>()
            .First(format => format != required);

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => new WarpBackendArtifact(
                backend,
                wrong,
                WarpDeviceAbi.IntegerMapEntryPoint,
                Encoding.UTF8.GetBytes("invalid")));

        StringAssert.Contains(exception.Message, backend.ToString());
    }

    [TestMethod]
    [FourBackends]
    public void A_shared_expression_is_lowered_once(WarpBackendKind backend)
    {
        var input = new WarpInputExpression(0);
        var constant = new WarpConstantExpression(1);
        var shared = new WarpBinaryExpression(WarpBinaryOperator.Add, input, constant);
        var result = new WarpBinaryExpression(WarpBinaryOperator.Multiply, shared, shared);
        var kernel = new WarpIntegerMapKernel("shared", 1, 0, result);

        WarpCompilation compilation = new WarpCompiler().Compile(kernel, CreateCompilers());

        Assert.HasCount(4, compilation.Kernel.Instructions);
        BackendArtifactAssertions.IsValid(
            compilation.Artifacts[backend],
            backend,
            compilation.Kernel);

        uint[] actual = new WarpIntegerMapSemanticEmulator().Execute(
            compilation.Artifacts[backend],
            compilation.Kernel,
            [new uint[] { 1u, 2u, uint.MaxValue }]);
        uint[] expected = [4u, 9u, 0u];
        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    [FourBackends]
    public void Backend_artifacts_are_deterministic(WarpBackendKind backend)
    {
        MethodInfo method = typeof(TestKernels).GetMethod(
            nameof(TestKernels.Combine),
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("The combine test kernel was not found.");
        var pipeline = new WarpBuildPipeline();

        WarpBackendArtifact first = pipeline.CompileIntegerMap(method, 2).Artifacts[backend];
        WarpBackendArtifact second = pipeline.CompileIntegerMap(method, 2).Artifacts[backend];

        CollectionAssert.AreEqual(first.Content.ToArray(), second.Content.ToArray());
    }

    [TestMethod]
    [FourBackends]
    public void Mismatched_input_lengths_are_rejected(WarpBackendKind backend)
    {
        WarpIntegerMapKernel verifiedKernel = VerifyCombine();
        WarpCompilation compilation = new WarpCompiler().Compile(verifiedKernel, CreateCompilers());

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => new WarpIntegerMapSemanticEmulator().Execute(
                compilation.Artifacts[backend],
                compilation.Kernel,
                [new uint[] { 1u }, new uint[] { 2u, 3u }],
                [4u, 5u]));

        StringAssert.Contains(exception.Message, "same length");
    }

    private static WarpIntegerMapKernel VerifyCombine()
    {
        MethodInfo method = typeof(TestKernels).GetMethod(
            nameof(TestKernels.Combine),
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("The combine test kernel was not found.");

        return new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(method, 2));
    }

    private static IEnumerable<IWarpBackendCompiler> CreateCompilers()
    {
        yield return new CoreCLRBackendCompiler();
        yield return new NVPTXBackendCompiler();
        yield return new AMDGPUBackendCompiler();
        yield return new SPIRVBackendCompiler();
    }

    private sealed class ContractOverrideCompiler : IWarpBackendCompiler
    {
        private readonly IWarpBackendCompiler compiler;

        public ContractOverrideCompiler(
            IWarpBackendCompiler compiler,
            WarpBackendContract contract)
        {
            this.compiler = compiler;
            Contract = contract;
        }

        public WarpBackendKind Backend => compiler.Backend;

        public WarpBackendContract Contract { get; }

        public WarpBackendArtifact Compile(WarpLinearKernel kernel) =>
            compiler.Compile(kernel);
    }
}
