using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Security.Cryptography;
using WarpCLR.Backend.CoreCLR;

namespace WarpCLR.Runtime.Host;

// A snapshot is immutable candidate DATA, even when its bytes match a real RPC.
// It intentionally contains no authority/mint/release method or issuer secret.
internal sealed class WarpSourceSegmentSnapshot
{
    internal WarpSourceSegmentSnapshot(ulong ordinal, ulong sequence, int processId, Guid module, string irHash,
        ReadOnlySpan<uint> state, ReadOnlySpan<uint> arena)
    {
        if (ordinal == 0 || sequence == 0 || processId <= 0 || module == Guid.Empty || irHash.Length != 64 ||
            irHash.Any(character => !char.IsAsciiHexDigit(character)))
        { throw new ArgumentException("A candidate requires exact nonzero command and module identity.", nameof(ordinal)); }
        if (((long)state.Length + arena.Length) * sizeof(uint) + 40 > WarpCoreCLRWorkerWords.MaximumBytes)
        { throw new ArgumentException("The complete candidate exceeds the existing bounded IPC bank admission.", nameof(state)); }
        Ordinal = ordinal; Sequence = sequence; ProcessId = processId; Module = module; IrHash = irHash;
        State = ImmutableArray.Create(state.ToArray()); Arena = ImmutableArray.Create(arena.ToArray());
        StateHash = Digest(State.AsSpan()); ArenaHash = Digest(Arena.AsSpan());
    }
    internal ulong Ordinal { get; }
    internal ulong Sequence { get; }
    internal int ProcessId { get; }
    internal Guid Module { get; }
    internal string IrHash { get; }
    internal ImmutableArray<uint> State { get; }
    internal ImmutableArray<uint> Arena { get; }
    internal string StateHash { get; }
    internal string ArenaHash { get; }
    internal static string Digest(ReadOnlySpan<uint> words)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> block = stackalloc byte[256];
        while (!words.IsEmpty)
        {
            int count = Math.Min(words.Length, block.Length / sizeof(uint));
            for (int index = 0; index < count; index++) { BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(index * sizeof(uint)), words[index]); }
            hash.AppendData(block[..(count * sizeof(uint))]); words = words[count..];
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
