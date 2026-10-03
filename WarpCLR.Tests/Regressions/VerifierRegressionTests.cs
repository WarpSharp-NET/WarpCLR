using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Sdk;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Regressions;

[TestClass]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class VerifierRegressionTests
{
    [TestMethod]
    [FourBackends]
    public void DivisionIsRejectedForEveryTarget(WarpBackendKind backend)
    {
        WarpVerificationException exception = CompileRejected(nameof(TestKernels.Divide), 1, backend);

        Assert.AreEqual("WRPCIL1001", exception.Code, StringComparer.Ordinal);
        StringAssert.Contains(exception.Message, "div.un", StringComparison.Ordinal);
    }

    [TestMethod]
    [FourBackends]
    public void RecursiveCallsAreRejectedUntilThePortableStackExists(
        WarpBackendKind backend)
    {
        WarpVerificationException exception = CompileRejected(nameof(TestKernels.Recursive), 1, backend);

        Assert.AreEqual("WRPCIL1014", exception.Code, StringComparer.Ordinal);
        StringAssert.Contains(exception.Message, "portable logical stack", StringComparison.Ordinal);
    }

    [TestMethod]
    [FourBackends]
    public void CallsOutsideTheClosedModuleAreRejectedForEveryTarget(
        WarpBackendKind backend)
    {
        WarpVerificationException exception = CompileRejected(
            nameof(TestKernels.ExternalCall),
            1,
            backend);

        Assert.AreEqual("WRPCIL1013", exception.Code, StringComparer.Ordinal);
        StringAssert.Contains(exception.Message, "closed module", StringComparison.Ordinal);
    }

    [TestMethod]
    [FourBackends]
    public void InvalidSignaturesAreRejectedForEveryTarget(WarpBackendKind backend)
    {
        WarpVerificationException exception = CompileRejected(nameof(TestKernels.WrongParameter), 1, backend);

        Assert.AreEqual("WRPCIL1000", exception.Code, StringComparer.Ordinal);
        StringAssert.Contains(exception.Message, "System.UInt32", StringComparison.Ordinal);
    }

    [TestMethod]
    [FourBackends]
    public void FloatingPointIsRejectedForEveryTarget(WarpBackendKind backend)
    {
        WarpVerificationException exception = CompileRejected(nameof(TestKernels.FloatingPoint), 1, backend);

        Assert.AreEqual("WRPCIL1000", exception.Code, StringComparer.Ordinal);
        StringAssert.Contains(exception.Message, "System.UInt32", StringComparison.Ordinal);
    }

    [TestMethod]
    [FourBackends]
    public void BranchMergesRequireIdenticalStackDepths(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        byte[] il =
        [
            unchecked((byte)OpCodes.Ldarg_0.Value),
            unchecked((byte)OpCodes.Brfalse_S.Value),
            3,
            unchecked((byte)OpCodes.Ldc_I4_1.Value),
            unchecked((byte)OpCodes.Br_S.Value),
            0,
            unchecked((byte)OpCodes.Ldc_I4_2.Value),
            unchecked((byte)OpCodes.Ret.Value),
        ];

        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => WarpIntegerMapCilVerifier.Verify(
                new WarpIntegerMapMethodBody(
                    "Malformed.StackMerge",
                    parameterCount: 1,
                    inputBufferCount: 1,
                    maxStack: 2,
                    localCount: 0,
                    il: il)));

        Assert.AreEqual("WRPCIL1002", exception.Code, StringComparer.Ordinal);
        StringAssert.Contains(exception.Message, "different stack depths", StringComparison.Ordinal);
    }

    private static WarpVerificationException CompileRejected(
        string name,
        int inputBufferCount,
        WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        MethodInfo method = typeof(TestKernels).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Test kernel '{name}' was not found.");
        var pipeline = new WarpBuildPipeline();

        return Assert.ThrowsExactly<WarpVerificationException>(
            () => pipeline.CompileIntegerMap(method, inputBufferCount));
    }
}
