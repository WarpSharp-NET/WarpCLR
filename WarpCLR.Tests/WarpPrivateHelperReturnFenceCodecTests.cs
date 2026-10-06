using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpPrivateHelperReturnFenceCodecTests
{
    [TestMethod]
    public void FencedMetadataAndWireIdentityRoundTripAndCannotBeDowngraded()
    {
        var fixture = new WarpPrivateHelperReturnFenceFixture(); string hash = WarpIrHash.Compute(fixture.Kernel);
        byte[] bytes = WarpCoreCLRBinaryPlanCodec.Serialize(fixture.Kernel);
        Assert.AreEqual(0x57425038u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        WarpControlFlowKernel restored = WarpCoreCLRBinaryPlanCodec.Deserialize(bytes, hash);
        Assert.IsTrue(restored.Execution!.PrivateControllerProjection!.RequiresHelperReturnFences);
        Assert.AreEqual(WarpLogicalExecutionMetadata.PrivateHelperReturnFenceVersion, restored.Execution.IdentityVersion, StringComparer.Ordinal);
        Assert.AreEqual(hash, WarpIrHash.Compute(restored), StringComparer.Ordinal);
        byte[] downgraded = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(downgraded, 0x57425036); Rehash(downgraded);
        Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRBinaryPlanCodec.Deserialize(downgraded, hash));
        byte[] priorWire = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(priorWire, 0x57425037); Rehash(priorWire);
        Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRBinaryPlanCodec.Deserialize(priorWire, hash));
        var old = new WarpPrivateHelperReturnFenceFixture(returnFences: false);
        byte[] oldBytes = WarpCoreCLRBinaryPlanCodec.Serialize(old.Kernel);
        Assert.AreEqual(0x57425036u, BinaryPrimitives.ReadUInt32LittleEndian(oldBytes));
        WarpControlFlowKernel oldRestored = WarpCoreCLRBinaryPlanCodec.Deserialize(oldBytes, WarpIrHash.Compute(old.Kernel));
        Assert.IsFalse(oldRestored.Execution!.PrivateControllerProjection!.RequiresHelperReturnFences);
        Assert.AreEqual(WarpLogicalExecutionMetadata.PrivateHelperBoundaryVersion, oldRestored.Execution.IdentityVersion, StringComparer.Ordinal);
        Assert.IsFalse(old.Layout.HasValidRuntimeHeader(fixture.ParkAfterHelper([1], 4096)));
    }

    [TestMethod]
    public void AReturnFenceCannotExistWithoutAnExactHelperEntryProfile() =>
        Assert.ThrowsExactly<ArgumentException>(() => new WarpPrivateControllerProjection(
            [new(1, 1, 8, 1, 0, 11, WarpPrivateHelperReturnFenceFixture.Service)], requiresHelperReturnFences: true));

    [TestMethod]
    [DataRow(WarpBackendKind.NVPTX)]
    [DataRow(WarpBackendKind.AMDGPU)]
    [DataRow(WarpBackendKind.SPIRV)]
    public void AllDialectGeneratorsIncludeTheSameConditionalFenceWithoutAnExecutionClaim(WarpBackendKind backend)
    {
        var fixture = new WarpPrivateHelperReturnFenceFixture(nested: true);
        string source = WarpPortableMachineEmitter.Emit(fixture.Layout, backend);
        StringAssert.Contains(source, "private_return_fence_", StringComparison.Ordinal);
        StringAssert.Contains(source, "private_release_word:", StringComparison.Ordinal);
        StringAssert.Contains(source, "icmp eq i32 %warp_scalar_0, 0", StringComparison.Ordinal);
        StringAssert.Contains(source, "scope_", StringComparison.Ordinal);
        var old = new WarpPrivateHelperReturnFenceFixture(returnFences: false);
        string ordinary = WarpPortableMachineEmitter.Emit(old.Layout, backend);
        Assert.IsFalse(ordinary.Contains("private_return_fence_", StringComparison.Ordinal));
        Assert.IsFalse(ordinary.Contains("private_release_word:", StringComparison.Ordinal));
    }

    private static void Rehash(byte[] bytes) => SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32), bytes.AsSpan(bytes.Length - 32));
}
