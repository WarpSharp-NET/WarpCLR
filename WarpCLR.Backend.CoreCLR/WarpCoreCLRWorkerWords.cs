using System.Security.Cryptography;
using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

internal static class WarpCoreCLRWorkerWords
{
    internal const int MaximumBytes = 64 * 1024 * 1024;
    private const int MaximumWords = MaximumBytes / sizeof(uint);

    internal static byte[] Request(uint[][] inputs, uint[] scalars, int worker, uint[] state, int depth, int quantum, uint[] arena)
    {
        _ = Estimate(inputs, scalars, state, arena);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(worker); writer.Write(depth); writer.Write(quantum); writer.Write(inputs.Length);
        foreach (uint[] input in inputs) { WriteWords(writer, input); }
        WriteWords(writer, scalars); WriteWords(writer, state); WriteWords(writer, arena);
        writer.Flush();
        return stream.ToArray();
    }

    internal static long Estimate(uint[][] inputs, uint[] scalars, uint[] state, uint[] arena)
    {
        ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(scalars);
        ArgumentNullException.ThrowIfNull(state); ArgumentNullException.ThrowIfNull(arena);
        long words = scalars.LongLength + state.LongLength + arena.LongLength;
        foreach (uint[] input in inputs) { ArgumentNullException.ThrowIfNull(input, nameof(inputs)); words += input.LongLength; }
        if (inputs.Length > WarpCompilationAdmission.MaximumParametersPerBody || words * sizeof(uint) + 64L + inputs.Length * 4L > MaximumBytes)
        {
            throw new InvalidDataException("Worker word transaction exceeds admission.");
        }
        return words * sizeof(uint) + 64L + inputs.Length * 4L;
    }

    internal static Invocation ReadRequest(byte[] payload)
    {
        if (payload.Length > MaximumBytes) { throw new InvalidDataException("Worker word transaction exceeds admission."); }
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        int worker = reader.ReadInt32(), depth = reader.ReadInt32(), quantum = reader.ReadInt32();
        int count = reader.ReadInt32();
        if (count < 0 || count > WarpCompilationAdmission.MaximumParametersPerBody) { throw new InvalidDataException("Worker input count exceeds admission."); }
        uint[][] inputs = new uint[count][];
        foreach (ref uint[] input in inputs.AsSpan()) { input = ReadWords(reader); }
        uint[] scalars = ReadWords(reader), state = ReadWords(reader), arena = ReadWords(reader);
        if (stream.Position != stream.Length) { throw new InvalidDataException("Trailing worker word transaction data."); }
        return new Invocation(inputs, scalars, worker, state, depth, quantum, arena);
    }

    internal static byte[] Response(byte[] request, uint[] state, uint[] arena)
    {
        if ((state.LongLength + arena.LongLength) * sizeof(uint) + 40 > MaximumBytes)
        { throw new InvalidDataException("Worker word result exceeds admission."); }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(SHA256.HashData(request)); WriteWords(writer, state); WriteWords(writer, arena);
        writer.Flush();
        return stream.ToArray();
    }

    internal static (uint[] State, uint[] Arena) ReadResponse(byte[] payload, byte[] request, int stateLength, int arenaLength)
    {
        if (payload.Length > MaximumBytes) { throw new InvalidDataException("Worker word result exceeds admission."); }
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (!CryptographicOperations.FixedTimeEquals(reader.ReadBytes(32), SHA256.HashData(request)))
        {
            throw new InvalidDataException("Worker result changed the admitted invocation identity.");
        }
        uint[] state = ReadWords(reader), arena = ReadWords(reader);
        if (state.Length != stateLength || arena.Length != arenaLength || stream.Position != stream.Length)
        {
            throw new InvalidDataException("Worker result changed the admitted buffer shape.");
        }
        return (state, arena);
    }

    internal static void WriteWords(BinaryWriter writer, uint[] words)
    {
        writer.Write(words.Length);
        foreach (uint word in words) { writer.Write(word); }
    }

    internal static uint[] ReadWords(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > MaximumWords || count * 4L > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw new InvalidDataException("Worker word buffer length is invalid.");
        }
        uint[] words = new uint[count];
        foreach (ref uint word in words.AsSpan()) { word = reader.ReadUInt32(); }
        return words;
    }

    internal sealed record Invocation(uint[][] Inputs, uint[] Scalars, int Worker, uint[] State, int Depth, int Quantum, uint[] Arena);
}
