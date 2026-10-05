namespace WarpCLR.Compiler;

internal static class WarpPortableSourceIdentity
{
    internal const string Semantics = "warp.object.identity/nonmoving-slot-generation-context-independent-mix32/0.1";

    public static uint Hash(uint slot, uint generation)
    {
        if ((slot | generation) == 0) { return 0; }
        uint hash = slot ^ ((generation - 1u) * 0x9E3779B9u);
        hash ^= hash >> 16; hash *= 0x85EBCA6Bu;
        hash ^= hash >> 13; hash *= 0xC2B2AE35u;
        return hash ^ (hash >> 16);
    }
}
