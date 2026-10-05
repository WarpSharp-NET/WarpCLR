using System.Security.Cryptography;
using WarpCLR.Compiler;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRGenerationBaseline
{
    private readonly uint[] header;
    private readonly byte[] structure;
    private readonly byte[] barriers;
    private readonly uint[] initialBarrierGenerations;

    internal WarpCoreCLRGenerationBaseline(uint[] state, uint[] arena, string operation, uint scheduler, uint controller)
    {
        State = state; Operation = operation; Scheduler = scheduler; Controller = controller;
        header = arena.AsSpan(checked((int)scheduler), checked((int)WarpPortableSchedulerLayout.HeaderWords)).ToArray();
        if (header[0] != WarpPortableSchedulerLayout.Magic || header[1] != 2 || header[3] != arena.Length ||
            header[WarpPortableSchedulerLayout.ControllerOwner] != controller || controller == 0)
        { throw new InvalidDataException("A generation service requires the exact version-two admitted controller domain."); }
        structure = StructuralIdentity(arena, scheduler);
        barriers = BarrierIdentity(arena, scheduler, string.Equals(operation, WarpCoreCLRRecoveryCatalog.BeginDispatch, StringComparison.Ordinal));
        initialBarrierGenerations = ReadBarrierGenerations(arena);
    }
    internal uint[] State { get; }
    internal string Operation { get; }
    internal uint Scheduler { get; }
    internal uint Controller { get; }
    internal uint Dispatch => header[WarpPortableSchedulerLayout.DispatchGeneration];
    internal uint Collection => header[WarpPortableSchedulerLayout.GCEpoch];

    internal (uint Dispatch, uint Collection) Validate(uint[] arena)
    {
        if (!structure.AsSpan().SequenceEqual(StructuralIdentity(arena, Scheduler)))
        { throw new InvalidDataException("A committed generation service changed its schema, context, topology, memberships, or root maps."); }
        ReadOnlySpan<uint> after = arena.AsSpan(checked((int)Scheduler), checked((int)WarpPortableSchedulerLayout.HeaderWords));
        if (after[(int)WarpPortableSchedulerLayout.ControllerOwner] != Controller)
        { throw new InvalidDataException("A generation service changed its captured controller grant."); }
        if (string.Equals(Operation, WarpCoreCLRRecoveryCatalog.RequestCollection, StringComparison.Ordinal)) { ValidateCollection(arena, after); }
        else { ValidateDispatch(after); }
        return (after[(int)WarpPortableSchedulerLayout.DispatchGeneration], after[(int)WarpPortableSchedulerLayout.GCEpoch]);
    }

    private void ValidateCollection(uint[] arena, ReadOnlySpan<uint> after)
    {
        uint beforeState = header[WarpPortableSchedulerLayout.GCState];
        uint expected = beforeState == WarpPortableSchedulerLayout.GCRequested ? Collection : checked(Collection + 1);
        if (beforeState > WarpPortableSchedulerLayout.GCRequested || after[(int)WarpPortableSchedulerLayout.GCState] != WarpPortableSchedulerLayout.GCRequested ||
            after[(int)WarpPortableSchedulerLayout.GCEpoch] != expected || after[(int)WarpPortableSchedulerLayout.DispatchGeneration] != Dispatch ||
            after[(int)WarpPortableSchedulerLayout.Result] != expected)
        { throw new InvalidDataException("RequestCollection did not commit its fixed nonwrapping generation and result contract."); }
        if (header[WarpPortableSchedulerLayout.HeapLinked] != 0 &&
            (arena[WarpPortableHeapLayout.CollectionEpoch] != expected || arena[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Requested))
        { throw new InvalidDataException("The linked heap did not commit the same collection epoch."); }
    }

    private void ValidateDispatch(ReadOnlySpan<uint> after)
    {
        if (header[WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.CompletedContext ||
            header[WarpPortableSchedulerLayout.GCState] != WarpPortableSchedulerLayout.GCIdle ||
            after[(int)WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Active ||
            after[(int)WarpPortableSchedulerLayout.GCState] != WarpPortableSchedulerLayout.GCIdle ||
            after[(int)WarpPortableSchedulerLayout.GCEpoch] != Collection || after[(int)WarpPortableSchedulerLayout.DispatchGeneration] != checked(Dispatch + 1))
        { throw new InvalidDataException("BeginDispatch did not commit its completed-to-active nonwrapping generation contract."); }
    }

    internal void ValidateBarriers(uint[] arena)
    {
        if (!barriers.AsSpan().SequenceEqual(BarrierIdentity(arena, Scheduler, advance: false)))
        { throw new InvalidDataException("The generation service changed the fixed adjacent barrier generations."); }
    }

    private static byte[] BarrierIdentity(uint[] arena, uint scheduler, bool advance)
    {
        uint count = arena[scheduler + WarpPortableSchedulerLayout.BarrierCount];
        uint start = checked(scheduler + arena[scheduler + WarpPortableSchedulerLayout.BarrierStart]);
        if (start > arena.Length || count > ((uint)arena.Length - start) / WarpPortableSchedulerLayout.BarrierWords)
        { throw new InvalidDataException("The admitted barrier table is out of range."); }
        uint[] generations = new uint[checked((int)count)];
        for (uint index = 0; index < count; index++)
        {
            uint generation = arena[start + index * WarpPortableSchedulerLayout.BarrierWords];
            generations[index] = advance ? unchecked(generation + 1) : generation;
        }
        return SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(generations.AsSpan()));
    }

    private static byte[] StructuralIdentity(uint[] arena, uint scheduler)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ReadOnlySpan<uint> layoutHeader = arena.AsSpan(checked((int)scheduler), checked((int)WarpPortableSchedulerLayout.HeaderWords));
        uint[] immutable = [layoutHeader[0], layoutHeader[1], layoutHeader[2], layoutHeader[3], layoutHeader[20], layoutHeader[21], layoutHeader[22], layoutHeader[23], layoutHeader[24],
            layoutHeader[25], layoutHeader[26], layoutHeader[27], layoutHeader[28], layoutHeader[41], layoutHeader[42], layoutHeader[43], layoutHeader[44], layoutHeader[45], layoutHeader[46], layoutHeader[47],
            layoutHeader[48], layoutHeader[49], layoutHeader[50], layoutHeader[51], layoutHeader[52], layoutHeader[53], layoutHeader[54], layoutHeader[55], layoutHeader[56], layoutHeader[58], layoutHeader[59]];
        hash.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(immutable.AsSpan()));
        AppendCatalog(hash, arena, scheduler, layoutHeader);
        if (layoutHeader[42] != 0)
        {
            uint[] heap = [arena[0], arena[1], arena[2], arena[20], arena[21], arena[22], arena[23], arena[24], arena[25], arena[26], arena[27]];
            hash.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(heap.AsSpan()));
        }
        return hash.GetHashAndReset();
    }

    private static void AppendCatalog(IncrementalHash hash, uint[] arena, uint scheduler, ReadOnlySpan<uint> layoutHeader)
    {
        int start = checked((int)(scheduler + layoutHeader[27])), end = checked((int)(scheduler + layoutHeader[45]));
        if (start < 0 || end < start || end > arena.Length) { throw new InvalidDataException("The immutable scheduler catalog is out of range."); }
        hash.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(arena.AsSpan(start, end - start)));
        for (uint index = 0; index < layoutHeader[25]; index++)
        {
            int entry = checked((int)(scheduler + layoutHeader[26] + index * WarpPortableSchedulerLayout.BarrierWords));
            uint[] descriptor = [arena[entry + 1], arena[entry + 3], arena[entry + 5], arena[entry + 6]];
            hash.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(descriptor.AsSpan()));
        }
    }
}
