using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Sdk;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Regressions;

[TestClass]
public sealed class VerifierRegressionTests
{
    [TestMethod]
    [FourBackends]
    public void Division_is_rejected_for_every_target(WarpBackendKind backend)
    {
        WarpVerificationException exception = CompileRejected(nameof(TestKernels.Divide), 1, backend);

        Assert.AreEqual("WRPCIL1001", exception.Code);
        StringAssert.Contains(exception.Message, "div.un");
    }

    [TestMethod]
    [FourBackends]
    public void Branches_are_rejected_for_every_target(WarpBackendKind backend)
    {
        WarpVerificationException exception = CompileRejected(nameof(TestKernels.Branch), 1, backend);

        Assert.AreEqual("WRPCIL1001", exception.Code);
        StringAssert.Contains(exception.Message, "brfalse.s");
    }

    [TestMethod]
    [FourBackends]
    public void Calls_are_rejected_for_every_target(WarpBackendKind backend)
    {
        WarpVerificationException exception = CompileRejected(nameof(TestKernels.Call), 1, backend);

        Assert.AreEqual("WRPCIL1001", exception.Code);
        StringAssert.Contains(exception.Message, "call");
    }

    [TestMethod]
    [FourBackends]
    public void Invalid_signatures_are_rejected_for_every_target(WarpBackendKind backend)
    {
        WarpVerificationException exception = CompileRejected(nameof(TestKernels.WrongParameter), 1, backend);

        Assert.AreEqual("WRPCIL1000", exception.Code);
        StringAssert.Contains(exception.Message, "System.UInt32");
    }

    [TestMethod]
    [FourBackends]
    public void Floating_point_is_rejected_for_every_target(WarpBackendKind backend)
    {
        WarpVerificationException exception = CompileRejected(nameof(TestKernels.FloatingPoint), 1, backend);

        Assert.AreEqual("WRPCIL1000", exception.Code);
        StringAssert.Contains(exception.Message, "System.UInt32");
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
