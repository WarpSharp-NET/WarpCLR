using System.Buffers.Binary;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableRotateLeftTests
{
    [TestMethod]
    [DataRow(WarpBackendKind.NVPTX)]
    [DataRow(WarpBackendKind.AMDGPU)]
    [DataRow(WarpBackendKind.SPIRV)]
    public void AllSharedEmittersAndOrdinaryCodecRetainTheExactUInt32WordGraph(WarpBackendKind backend)
    {
        foreach (string name in new[] { nameof(WarpPortableRotateLeftSourceKernels.Rotate32), nameof(WarpPortableRotateLeftSourceKernels.Rotate64),
            nameof(WarpPortableRotateLeftSourceKernels.Transport32), nameof(WarpPortableRotateLeftSourceKernels.Transport64) })
        {
            var layout = new WarpLogicalMachineLayout(Capture(name).Kernel);
            string hash = WarpIrHash.Compute(layout.Kernel);
            byte[] bytes = WarpCoreCLRBinaryPlanCodec.Serialize(layout.Kernel);
            Assert.AreEqual(0x57425036u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
            WarpControlFlowKernel decoded = WarpCoreCLRBinaryPlanCodec.Deserialize(bytes, hash);
            Assert.AreEqual(hash, WarpIrHash.Compute(decoded), StringComparer.Ordinal);
            CollectionAssert.AreEqual(bytes, WarpCoreCLRBinaryPlanCodec.Serialize(decoded));
            string source = WarpPortableMachineEmitter.Emit(layout, backend);
            StringAssert.Contains(source, "load i32", StringComparison.Ordinal);
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(source,
                @"\b(fadd|fsub|fmul|fdiv|frem|fptrunc|fpext|sitofp|uitofp|fptosi|fptoui)\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)));
            Assert.IsFalse(source.Contains("llvm.fshl.i64", StringComparison.Ordinal));
            Assert.IsNull(decoded.Execution!.PrivateControllerProjection);
            Assert.AreEqual(WarpLogicalExecutionMetadata.Version, decoded.Execution.IdentityVersion, StringComparer.Ordinal);
            Assert.IsFalse(decoded.Execution.RuntimeStateAccess);
            Assert.IsFalse(decoded.Execution.ManagedExceptionTermination);
            Assert.IsFalse(decoded.Execution.LogicalWorkerAccess);
        }
    }
}
