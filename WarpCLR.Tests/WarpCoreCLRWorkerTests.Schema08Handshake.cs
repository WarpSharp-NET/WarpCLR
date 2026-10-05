using System.Text;
using System.Runtime.InteropServices;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    public void HandshakeBindsExactSchemasFrameFaultsCapabilitiesAndActualHost()
    {
        string host = WarpCoreCLRWorkerProcess.GetDotnetHost(new());
        byte[] deployment = new byte[32];
        Guid worker = Guid.NewGuid();
        byte[] hello = WarpCoreCLRWorkerIdentity.Hello(deployment, worker, host);
        WarpCoreCLRWorkerIdentity.ValidateHello(hello, deployment, Environment.ProcessId, worker, host);
        using var stream = new MemoryStream(hello, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        stream.Position = 68;
        var offsets = new List<int> { 0, 32, 36 };
        foreach (string expected in new[] { WarpCoreCLRWorkerProtocol.Version, WarpCoreCLRBinaryPlanCodec.Version,
            WarpProfileCatalog.ProfileId, WarpRuntimeAbi.Version, WarpLogicalMachineLayout.Version,
            WarpLogicalExecutionMetadata.Version, WarpManagedInvocationOpCode.Version, WarpManagedWideAtomicOpCode.Semantics })
        {
            offsets.Add(checked((int)stream.Position + 1));
            Assert.AreEqual(expected, reader.ReadString(), StringComparer.Ordinal);
        }
        foreach (int value in new[] { 64, 8, 7, 8, 9 })
        {
            offsets.Add(checked((int)stream.Position)); Assert.AreEqual(value, reader.ReadInt32());
        }
        offsets.Add(checked((int)stream.Position + 1)); _ = reader.ReadString();
        offsets.Add(checked((int)stream.Position + 1)); _ = reader.ReadString();
        offsets.Add(checked((int)stream.Position)); Assert.IsTrue(reader.ReadBoolean());
        offsets.Add(checked((int)stream.Position)); Assert.IsTrue(reader.ReadBoolean());
        offsets.Add(checked((int)stream.Position)); Assert.IsTrue(reader.ReadBoolean());
        offsets.Add(checked((int)stream.Position)); Assert.AreEqual(0x3FFFu, reader.ReadUInt32());
        for (int index = 0; index < 4; index++) { offsets.Add(checked((int)stream.Position)); _ = reader.ReadBytes(16); }
        Assert.AreEqual(stream.Length, stream.Position);
        foreach (ref readonly int offset in CollectionsMarshal.AsSpan(offsets))
        {
            byte[] forged = (byte[])hello.Clone(); forged[offset] ^= 1;
            Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRWorkerIdentity.ValidateHello(forged, deployment, Environment.ProcessId, worker, host));
        }
    }
}
