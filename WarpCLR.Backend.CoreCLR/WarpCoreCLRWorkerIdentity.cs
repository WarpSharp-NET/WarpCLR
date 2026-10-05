using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

internal static class WarpCoreCLRWorkerIdentity
{
    private static readonly string[] DeploymentFiles =
    ["WarpCLR.CoreCLR.Worker.dll", "WarpCLR.CoreCLR.Worker.deps.json", "WarpCLR.CoreCLR.Worker.runtimeconfig.json",
        "WarpCLR.Backend.CoreCLR.dll", "WarpCLR.IR.dll"];

    internal static byte[] DeploymentDigest(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string name in DeploymentFiles)
        {
            string path = Path.Combine(directory, name);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > WarpCoreCLRBinaryPlanCodec.MaximumBytes) { throw new InvalidDataException("Worker deployment exceeds admission."); }
            hash.AppendData(Encoding.UTF8.GetBytes(name)); hash.AppendData(SHA256.HashData(stream));
        }
        return hash.GetHashAndReset();
    }

    internal static byte[] Hello(byte[] deployment, Guid workerModule, string dotnetHost)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(deployment); writer.Write(Environment.ProcessId); writer.Write(HostIdentity(dotnetHost));
        writer.Write(WarpCoreCLRWorkerProtocol.Version); writer.Write(WarpCoreCLRBinaryPlanCodec.Version);
        writer.Write(WarpProfileCatalog.ProfileId); writer.Write(WarpRuntimeAbi.Version);
        writer.Write(WarpLogicalMachineLayout.Version); writer.Write(WarpLogicalExecutionMetadata.Version);
        writer.Write(WarpManagedInvocationOpCode.Version); writer.Write(WarpManagedWideAtomicOpCode.Semantics);
        writer.Write(WarpLogicalMachineLayout.HeaderWords); writer.Write(WarpLogicalMachineLayout.FrameHeaderWords);
        writer.Write(WarpLogicalMachineLayout.PhysicalFrameCapacityFault); writer.Write(WarpLogicalMachineLayout.ManagedExceptionFault);
        writer.Write(WarpLogicalMachineLayout.AtomicAlignmentFault);
        writer.Write(RuntimeInformation.FrameworkDescription); writer.Write(RuntimeInformation.ProcessArchitecture.ToString());
        writer.Write(RuntimeFeature.IsDynamicCodeSupported); writer.Write(RuntimeFeature.IsDynamicCodeCompiled);
        writer.Write(BitConverter.IsLittleEndian);
        writer.Write(0x3FFFu); // Prior eleven features plus checked filter aliases, managed terminal and logical-worker invocation.
        writer.Write(typeof(object).Assembly.ManifestModule.ModuleVersionId.ToByteArray());
        writer.Write(workerModule.ToByteArray());
        writer.Write(typeof(CoreCLRResumableKernel).Assembly.ManifestModule.ModuleVersionId.ToByteArray());
        writer.Write(typeof(WarpControlFlowKernel).Assembly.ManifestModule.ModuleVersionId.ToByteArray());
        writer.Flush();
        return stream.ToArray();
    }

    internal static void ValidateHello(byte[] payload, byte[] deployment, int processId, Guid workerModule, string dotnetHost)
    {
        byte[] expected = Hello(deployment, workerModule, dotnetHost);
        BinaryPrimitivesWriteProcess(expected, processId);
        if (!CryptographicOperations.FixedTimeEquals(payload, expected))
        {
            throw new InvalidDataException("Worker deployment, runtime, module, or ABI identity changed.");
        }
    }

    internal static byte[] HostIdentity(string dotnetHost)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFile(hash, dotnetHost);
        string runtime = RuntimeEnvironment.GetRuntimeDirectory();
        foreach (string name in OperatingSystem.IsWindows() ? new[] { "coreclr.dll", "clrjit.dll" } : new[] { "libcoreclr.so", "libclrjit.so" })
        { AppendFile(hash, Path.Combine(runtime, name)); }
        return hash.GetHashAndReset();
    }

    private static void AppendFile(IncrementalHash hash, string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > WarpCoreCLRBinaryPlanCodec.MaximumBytes) { throw new InvalidDataException("Native host/runtime identity file exceeds admission."); }
        hash.AppendData(SHA256.HashData(file));
    }

    internal static Guid ReadWorkerModule(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var pe = new PEReader(file);
        MetadataReader metadata = pe.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
    }

    private static void BinaryPrimitivesWriteProcess(byte[] hello, int processId) =>
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(hello.AsSpan(32), processId);
}
