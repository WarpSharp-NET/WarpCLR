using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal static class WarpCoreCLRReadOnlyWordIdentity
{
    internal static byte[] FromSerializedRequest(byte[] request)
    {
        if (request.Length is < 20 or > WarpCoreCLRWorkerWords.MaximumBytes)
        { throw new InvalidDataException("The readonly input identity requires one bounded complete word request."); }
        int count = BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(12, 4));
        if (count < 0 || count > WarpCompilationAdmission.MaximumParametersPerBody)
        { throw new InvalidDataException("The readonly input count exceeds the exact request profile."); }
        int end = 16;
        for (int i = 0; i <= count; i++)
        {
            if (end > request.Length - 4) { throw new InvalidDataException("The readonly request words are truncated."); }
            int words = BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(end, 4));
            long next = end + 4L + words * 4L;
            if (words < 0 || next > request.Length)
            { throw new InvalidDataException("The readonly request bank shape is invalid."); }
            end = checked((int)next);
        }
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("warp.coreclr.readonly-word-inputs/little-endian-length-framed/0.1"u8);
        hash.AppendData(request.AsSpan(12, end - 12));
        return hash.GetHashAndReset();
    }

    internal static byte[] FromArguments(uint[][] inputs, uint[] scalars)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("warp.coreclr.readonly-word-inputs/little-endian-length-framed/0.1"u8);
        AppendCount(hash, inputs.Length);
        foreach (uint[] input in inputs) { AppendWords(hash, input); }
        AppendWords(hash, scalars);
        return hash.GetHashAndReset();
    }

    private static void AppendCount(IncrementalHash hash, int count)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, count);
        hash.AppendData(bytes);
    }

    private static void AppendWords(IncrementalHash hash, uint[] words)
    {
        AppendCount(hash, words.Length);
        if (BitConverter.IsLittleEndian) { hash.AppendData(MemoryMarshal.AsBytes(words.AsSpan())); return; }
        Span<byte> bytes = stackalloc byte[4];
        foreach (uint word in words)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, word);
            hash.AppendData(bytes);
        }
    }
}
