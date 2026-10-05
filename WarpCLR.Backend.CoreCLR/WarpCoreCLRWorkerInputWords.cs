using System.Security.Cryptography;
using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

internal static class WarpCoreCLRWorkerInputWords
{
    internal const int MaximumBindings = 64;

    internal static byte[] Write(uint tag, uint[][] inputs, uint[] scalars)
    {
        _ = Estimate(inputs, scalars);
        if (tag == 0) { throw new InvalidDataException("Immutable input binding tag is zero."); }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(tag); writer.Write(inputs.Length);
        foreach (uint[] input in inputs) { WarpCoreCLRWorkerWords.WriteWords(writer, input); }
        WarpCoreCLRWorkerWords.WriteWords(writer, scalars); writer.Flush();
        return stream.ToArray();
    }

    internal static long Estimate(uint[][] inputs, uint[] scalars)
    {
        long words = scalars.LongLength;
        foreach (uint[] input in inputs) { words += input.LongLength; }
        if (inputs.Length > WarpCompilationAdmission.MaximumParametersPerBody ||
            words * sizeof(uint) + inputs.Length * 4L + 12 > WarpCoreCLRWorkerWords.MaximumBytes)
        { throw new InvalidDataException("Immutable input binding exceeds its word admission."); }
        return words * sizeof(uint) + inputs.Length * 4L + 12;
    }

    internal static Binding Read(byte[] payload)
    {
        if (payload.Length > WarpCoreCLRWorkerWords.MaximumBytes) { throw new InvalidDataException("Immutable input binding exceeds admission."); }
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        uint tag = reader.ReadUInt32(); int count = reader.ReadInt32();
        if (tag == 0 || count < 0 || count > WarpCompilationAdmission.MaximumParametersPerBody)
        { throw new InvalidDataException("Immutable input binding has an invalid tag or count."); }
        uint[][] inputs = new uint[count][];
        foreach (ref uint[] input in inputs.AsSpan()) { input = WarpCoreCLRWorkerWords.ReadWords(reader); }
        uint[] scalars = WarpCoreCLRWorkerWords.ReadWords(reader);
        if (stream.Position != stream.Length) { throw new InvalidDataException("Trailing immutable input binding data."); }
        return new(tag, SHA256.HashData(payload), inputs, scalars, payload.Length);
    }

    internal static byte[] Reference(uint tag, ReadOnlySpan<byte> identity)
    {
        if (tag == 0 || identity.Length != 32) { throw new InvalidDataException("Input reference identity is invalid."); }
        byte[] result = new byte[36];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result, tag); identity.CopyTo(result.AsSpan(4));
        return result;
    }

    internal static Binding Resolve(byte[] payload, IReadOnlyDictionary<uint, Binding> bindings)
    {
        if (payload.Length < 36 || !bindings.TryGetValue(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload), out Binding? binding) ||
            !CryptographicOperations.FixedTimeEquals(binding.Identity, payload.AsSpan(4, 32)))
        { throw new InvalidDataException("Input reference is not the exact immutable admitted binding."); }
        return binding;
    }

    internal sealed record Binding(uint Tag, byte[] Identity, uint[][] Inputs, uint[] Scalars, int Bytes);
}
