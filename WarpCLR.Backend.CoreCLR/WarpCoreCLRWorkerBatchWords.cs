using System.Security.Cryptography;
using System.Text;

namespace WarpCLR.Backend.CoreCLR;

internal static class WarpCoreCLRWorkerBatchWords
{
    internal const int MaximumWorkers = 4096;

    internal static byte[] Request(uint tag, ReadOnlySpan<byte> identity, int inputBase, int depth, int quantum, uint[][] states)
    {
        Admit(states, 52);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(WarpCoreCLRWorkerInputWords.Reference(tag, identity));
        writer.Write(inputBase); writer.Write(depth); writer.Write(quantum); writer.Write(states.Length);
        foreach (uint[] state in states) { WarpCoreCLRWorkerWords.WriteWords(writer, state); }
        writer.Flush(); return stream.ToArray();
    }

    internal static Invocation ReadRequest(byte[] payload)
    {
        if (payload.Length > WarpCoreCLRWorkerWords.MaximumBytes) { throw new InvalidDataException("Worker batch exceeds admission."); }
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        _ = reader.ReadBytes(36);
        int inputBase = reader.ReadInt32(), depth = reader.ReadInt32(), quantum = reader.ReadInt32();
        uint[][] states = ReadStates(reader);
        if (inputBase < 0 || stream.Position != stream.Length) { throw new InvalidDataException("Worker batch range or trailing data is invalid."); }
        return new(inputBase, depth, quantum, states);
    }

    internal static byte[] Response(byte[] request, uint[][] states)
    {
        Admit(states, 36);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(SHA256.HashData(request)); writer.Write(states.Length);
        foreach (uint[] state in states) { WarpCoreCLRWorkerWords.WriteWords(writer, state); }
        writer.Flush(); return stream.ToArray();
    }

    internal static uint[][] ReadResponse(byte[] payload, byte[] request, uint[][] admitted)
    {
        if (payload.Length > WarpCoreCLRWorkerWords.MaximumBytes) { throw new InvalidDataException("Worker batch result exceeds admission."); }
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (!CryptographicOperations.FixedTimeEquals(reader.ReadBytes(32), SHA256.HashData(request)))
        { throw new InvalidDataException("Worker batch result changed its admitted transaction."); }
        uint[][] states = ReadStates(reader);
        if (states.Length != admitted.Length || stream.Position != stream.Length)
        { throw new InvalidDataException("Worker batch result changed its admitted shape."); }
        for (int index = 0; index < states.Length; index++)
        { if (states[index].Length != admitted[index].Length) { throw new InvalidDataException("Worker batch state width changed."); } }
        return states;
    }

    private static uint[][] ReadStates(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count <= 0 || count > MaximumWorkers || count * 4L > reader.BaseStream.Length - reader.BaseStream.Position)
        { throw new InvalidDataException("Worker batch count exceeds admission."); }
        uint[][] states = new uint[count][];
        foreach (ref uint[] state in states.AsSpan()) { state = WarpCoreCLRWorkerWords.ReadWords(reader); }
        return states;
    }

    private static void Admit(uint[][] states, int overhead)
    {
        if (states.Length is <= 0 or > MaximumWorkers) { throw new InvalidDataException("Worker batch count exceeds admission."); }
        long bytes = overhead + states.Length * 4L;
        foreach (uint[] state in states) { bytes += state.LongLength * sizeof(uint); }
        if (bytes > WarpCoreCLRWorkerWords.MaximumBytes)
        { throw new InvalidDataException("Worker batch exceeds its bounded word admission."); }
    }

    internal sealed record Invocation(int InputBase, int Depth, int Quantum, uint[][] States);
}
