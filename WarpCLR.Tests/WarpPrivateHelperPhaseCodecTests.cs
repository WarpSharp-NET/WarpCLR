using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpPrivateHelperPhaseCodecTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ConditionalPrivateProfilesRoundTripTheirExactDistinctIdentities(bool boundaries)
    {
        var fixture = new WarpPrivateHelperPhaseFixture(boundaries);
        string hash = WarpIrHash.Compute(fixture.Kernel);
        byte[] bytes = WarpCoreCLRBinaryPlanCodec.Serialize(fixture.Kernel);
        WarpControlFlowKernel restored = WarpCoreCLRBinaryPlanCodec.Deserialize(bytes, hash);
        Assert.AreEqual(boundaries, restored.Execution!.PrivateControllerProjection!.RequiresHelperBoundaries);
        Assert.AreEqual(fixture.Kernel.Execution!.IdentityVersion, restored.Execution.IdentityVersion, StringComparer.Ordinal);
        Assert.AreEqual(hash, WarpIrHash.Compute(restored), StringComparer.Ordinal);
        Assert.AreEqual(0x57425036u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        var other = new WarpPrivateHelperPhaseFixture(!boundaries);
        Assert.AreNotEqual(hash, WarpIrHash.Compute(other.Kernel), StringComparer.Ordinal);
    }

    [TestMethod]
    public void AuthenticatedDigestCannotMakeOldWireOrChangedPrivateSemanticsAdmissible()
    {
        var fixture = new WarpPrivateHelperPhaseFixture(); byte[] bytes = WarpCoreCLRBinaryPlanCodec.Serialize(fixture.Kernel);
        string hash = WarpIrHash.Compute(fixture.Kernel);
        byte[] old = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(old, 0x57425035); Rehash(old);
        Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRBinaryPlanCodec.Deserialize(old, hash));
        byte[] marker = RawString(WarpPrivateControllerProjection.HelperBoundarySemantics);
        int offset = bytes.AsSpan().IndexOf(marker); Assert.IsGreaterThanOrEqualTo(0, offset);
        byte[] changed = (byte[])bytes.Clone(); changed[offset + marker.Length - 2] ^= 1; Rehash(changed);
        Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRBinaryPlanCodec.Deserialize(changed, hash));
    }

    [TestMethod]
    [DataRow(WarpBackendKind.NVPTX)]
    [DataRow(WarpBackendKind.AMDGPU)]
    [DataRow(WarpBackendKind.SPIRV)]
    public void CommonEmissionUsesTheSamePrechargePrivatePauseWithoutANativeExecutionClaim(WarpBackendKind backend)
    {
        var fixture = new WarpPrivateHelperPhaseFixture(); string source = WarpPortableMachineEmitter.Emit(fixture.Layout, backend);
        int phase = source.IndexOf("private_check_", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, phase);
        StringAssert.Contains(source, "icmp ne i32 %warp_scalar_0, 0", StringComparison.Ordinal);
        StringAssert.Contains(source, "private_park_", StringComparison.Ordinal); StringAssert.Contains(source, "private_ack_", StringComparison.Ordinal);
        int guard = source.IndexOf("private_dispatch_header:", StringComparison.Ordinal);
        int frame = source.IndexOf("  %depth = load i32", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, guard); Assert.IsGreaterThan(guard, frame);
        StringAssert.Contains(source, "private_dispatch_site_", StringComparison.Ordinal);
        var old = new WarpPrivateHelperPhaseFixture(boundaries: false);
        Assert.IsFalse(WarpPortableMachineEmitter.Emit(old.Layout, backend).Contains("private_dispatch_", StringComparison.Ordinal));
    }

    private static byte[] RawString(string value)
    {
        byte[] bytes = new byte[checked(4 + value.Length * 2)]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value.Length);
        for (int index = 0; index < value.Length; index++) { BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4 + index * 2), value[index]); }
        return bytes;
    }

    private static void Rehash(byte[] bytes) => SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32), bytes.AsSpan(bytes.Length - 32));
}
