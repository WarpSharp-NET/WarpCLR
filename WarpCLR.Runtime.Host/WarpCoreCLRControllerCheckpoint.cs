using System.Security.Cryptography;
using System.Runtime.InteropServices;
using WarpCLR.Compiler;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCoreCLRControllerCheckpoint
{
    private readonly byte[] stateDigest;
    private readonly byte[] arenaDigest;
    internal WarpCoreCLRControllerCheckpoint(ulong ordinal, ulong sequence, Guid module, byte[] requestDigest, byte[] response,
        uint[] state, uint[] arena, uint scheduler)
    {
        Ordinal = ordinal; Sequence = sequence; Module = module; RequestDigest = Convert.ToHexString(requestDigest);
        ResponseDigest = Convert.ToHexString(SHA256.HashData(response)); stateDigest = Digest(state); arenaDigest = Digest(arena);
        Dispatch = arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration];
        Collection = arena[scheduler + WarpPortableSchedulerLayout.GCEpoch];
    }
    internal ulong Ordinal { get; }
    internal ulong Sequence { get; }
    internal Guid Module { get; }
    internal string RequestDigest { get; }
    internal string ResponseDigest { get; }
    internal uint Dispatch { get; }
    internal uint Collection { get; }
    internal void ValidateStorage(uint[] state, uint[] arena)
    {
        if (!CryptographicOperations.FixedTimeEquals(stateDigest, Digest(state)) ||
            !CryptographicOperations.FixedTimeEquals(arenaDigest, Digest(arena)))
        { throw new InvalidOperationException("Controller storage changed outside its last authenticated committed checkpoint."); }
    }
    internal static byte[] Digest(uint[] words) => SHA256.HashData(MemoryMarshal.AsBytes(words.AsSpan()));
}
