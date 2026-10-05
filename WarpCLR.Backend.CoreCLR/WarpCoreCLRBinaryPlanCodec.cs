using System.Security.Cryptography;
using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

internal static partial class WarpCoreCLRBinaryPlanCodec
{
    internal const string Version = "warp.coreclr.binary-plan/owned-alias-managed-terminal-logical-worker-pair-atomics-raw-utf16-private-controller/0.5";
    private const string SupportedMachine = "warp.logical-machine/0.8";
    private const string SupportedFrames = "warp.logical-source-frames/0.6";
    private const string SupportedPrivateFrames = "warp.logical-source-frames/private-controller-service-projection/0.7";
    internal const int MaximumBytes = WarpCompilationAdmission.MaximumSourceBytes;
    private const uint Magic = 0x57425035;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static void RequireSupportedSchemas()
    {
        if (!string.Equals(WarpLogicalMachineLayout.Version, SupportedMachine, StringComparison.Ordinal) ||
            !string.Equals(WarpLogicalExecutionMetadata.Version, SupportedFrames, StringComparison.Ordinal) ||
            !string.Equals(WarpLogicalExecutionMetadata.PrivateControllerVersion, SupportedPrivateFrames, StringComparison.Ordinal))
        {
            throw new NotSupportedException("The binary codec requires a separately validated version for changed logical metadata.");
        }
    }

    internal static byte[] Serialize(WarpControlFlowKernel kernel)
    {
        RequireSupportedSchemas();
        ArgumentNullException.ThrowIfNull(kernel);
        WarpCompilationAdmission.Validate(kernel);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
        writer.Write(Magic);
        WriteString(writer, Version);
        WriteString(writer, WarpProfileCatalog.ProfileId);
        WriteString(writer, WarpRuntimeAbi.Version);
        WriteString(writer, WarpRuntimeAbi.SafepointPolicy);
        WriteString(writer, WarpLogicalMachineLayout.Version);
        WriteString(writer, kernel.Execution?.IdentityVersion ?? WarpLogicalExecutionMetadata.Version);
        WriteString(writer, WarpIrHash.Compute(kernel));
        WriteString(writer, kernel.Name);
        writer.Write(kernel.InputBufferCount); writer.Write(kernel.ScalarArgumentCount);
        writer.Write(kernel.Reduction.HasValue ? (int)kernel.Reduction.Value : -1);
        writer.Write(kernel.Functions.Count);
        foreach (WarpControlFlowFunction function in kernel.Functions)
        {
            writer.Write(function.Id); WriteString(writer, function.Name); writer.Write(function.ParameterCount);
            WriteBody(writer, function.Blocks);
        }
        WriteBody(writer, kernel.Blocks);
        WriteExecution(writer, kernel.Execution);
        writer.Flush();
        if (stream.Length > MaximumBytes - 32) { throw new InvalidDataException("The full CoreCLR plan exceeds binary admission."); }
        byte[] payload = stream.ToArray();
        byte[] framed = new byte[payload.Length + 32];
        payload.CopyTo(framed, 0);
        SHA256.HashData(payload).CopyTo(framed, payload.Length);
        return framed;
    }

    internal static WarpControlFlowKernel Deserialize(ReadOnlySpan<byte> bytes, string expectedIrHash)
    {
        RequireSupportedSchemas();
        ArgumentNullException.ThrowIfNull(expectedIrHash);
        if (bytes.Length < 36 || bytes.Length > MaximumBytes ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes[..^32]), bytes[^32..]))
        {
            throw new InvalidDataException("The full CoreCLR plan digest/length is invalid.");
        }
        using var stream = new MemoryStream(bytes[..^32].ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
        try { return ReadPlan(reader, expectedIrHash); }
        catch (Exception error) when (error is ArgumentException or OverflowException or EndOfStreamException or DecoderFallbackException)
        {
            throw new InvalidDataException("The full CoreCLR plan is malformed or outside admission.", error);
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        if (value.Length > WarpCompilationAdmission.MaximumIdentityCharacters) { throw new InvalidDataException("Binary identity exceeds admission."); }
        writer.Write(value.Length);
        foreach (char unit in value) { writer.Write((ushort)unit); }
    }

    private static string ReadString(BinaryReader reader)
    {
        int length = Count(reader, WarpCompilationAdmission.MaximumIdentityCharacters);
        if (length * (long)sizeof(ushort) > reader.BaseStream.Length - reader.BaseStream.Position) { throw new InvalidDataException("Truncated binary identity."); }
        char[] units = new char[length];
        foreach (ref char unit in units.AsSpan()) { unit = (char)reader.ReadUInt16(); }
        return new string(units);
    }

    private static int Count(BinaryReader reader, int maximum)
    {
        int value = reader.ReadInt32();
        return value < 0 || value > maximum ? throw new InvalidDataException("Binary collection length exceeds admission.") : value;
    }

    private static bool Flag(BinaryReader reader)
    {
        byte value = reader.ReadByte();
        return value > 1 ? throw new InvalidDataException("Binary capability flag is invalid.") : value != 0;
    }
}
