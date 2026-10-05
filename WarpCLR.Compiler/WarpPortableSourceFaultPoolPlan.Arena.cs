using System.Buffers.Binary;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceFaultPoolPlan
{
    // This attaches immutable compiler data and reserves pristine runtime roots.
    // It allocates no managed source object and grants no source-operation ticket.
    internal uint[] AttachToEmptyHeap(uint[] heap, uint firstRoot)
    {
        ArgumentNullException.ThrowIfNull(heap);
        RequireEmptyHeap(heap); RequireRootReservation(heap, firstRoot);
        uint descriptor = heap[WarpPortableHeapLayout.DataStart];
        uint end = checked(descriptor + WordCount); uint[] arena = new uint[checked(heap.Length + (int)WordCount)];
        heap.AsSpan(0, (int)descriptor).CopyTo(arena); heap.AsSpan((int)descriptor).CopyTo(arena.AsSpan((int)end));
        arena[3] = (uint)arena.Length; arena[WarpPortableHeapLayout.DataStart] = end;
        arena[WarpPortableSourceFaultFactoryLayout.Descriptor] = descriptor;
        uint rows = descriptor + WarpPortableSourceFaultFactoryLayout.HeaderWords;
        uint resources = rows + (uint)Rows.Length * WarpPortableSourceFaultFactoryLayout.RowWords;
        uint prepared = resources + (uint)Resources.Length * WarpPortableSourceFaultFactoryLayout.ResourceWords;
        uint text = prepared + PreparedCount * WarpPortableSourceFaultFactoryLayout.PreparedWords;
        WriteHeader(arena, descriptor, rows, resources, prepared, text, firstRoot);
        WriteRows(arena, rows); WriteResources(arena, resources, text, firstRoot); WritePrepared(arena, prepared, firstRoot);
        for (uint index = 0; index < RootCount; index++)
        {
            uint root = arena[WarpPortableHeapLayout.RootStart] + (firstRoot + index - 1) * WarpPortableHeapLayout.RootWords;
            arena[root + WarpPortableHeapLayout.RootOwnership] = WarpPortableHeapLayout.RuntimeOwnedRoot;
        }
        return arena;
    }

    private void RequireEmptyHeap(uint[] heap)
    {
        if (Rows.IsEmpty || heap.Length < WarpPortableHeapLayout.HeaderWords || heap[0] != WarpPortableHeapLayout.Magic ||
            heap[1] != WarpPortableHeapLayout.Version || heap[3] != heap.Length || heap[WarpPortableHeapLayout.Context] == 0 ||
            heap[WarpPortableSourceFaultFactoryLayout.Descriptor] != 0 || heap[WarpPortableHeapLayout.UsedWords] != 0 ||
            heap[WarpPortableHeapLayout.LiveObjects] != 0 || heap[WarpPortableHeapLayout.LiveRoots] != 0 ||
            heap[WarpPortableHeapLayout.ActiveWorkers] != 0 || heap[WarpPortableHeapLayout.LeaseState] != 0 ||
            heap[WarpPortableHeapLayout.PendingResult] != 0 || heap[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Idle ||
            heap[WarpPortableHeapLayout.DataStart] < WarpPortableHeapLayout.HeaderWords || heap[WarpPortableHeapLayout.DataStart] > heap.Length)
        {
            throw Invalid("A finite fault pool can attach only once to an unused idle source heap with admitted rows.");
        }
        uint memory = heap[WarpPortableSourceMemoryLayout.Descriptor];
        if (memory < WarpPortableHeapLayout.HeaderWords || memory > heap.Length - WarpPortableSourceMemoryLayout.HeaderWords ||
            heap[memory] != WarpPortableSourceMemoryLayout.Magic || heap[memory + 1] != WarpPortableSourceMemoryLayout.Version)
        {
            throw Invalid("A finite fault pool requires the exact captured source memory descriptor.");
        }
        byte[] hash = Convert.FromHexString(Contract.TypeSchemaHash);
        for (int word = 0; word < 8; word++)
        {
            if (heap[memory + WarpPortableSourceMemoryLayout.Hash + (uint)word] != BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(word * 4)))
            {
                throw Invalid("The reserved exception pool belongs to another source type/data schema.");
            }
        }
    }

    private void RequireRootReservation(uint[] heap, uint firstRoot)
    {
        uint start = heap[WarpPortableHeapLayout.RootStart], count = heap[WarpPortableHeapLayout.RootCount];
        if (firstRoot == 0 || firstRoot > count || RootCount > count - firstRoot + 1 || start > heap.Length ||
            (long)count * WarpPortableHeapLayout.RootWords > heap.Length - start)
        {
            throw Invalid("A finite fault pool needs a bounded disjoint root reservation for every resource and prepared report.");
        }
        for (uint index = 0; index < RootCount; index++)
        {
            uint root = start + (firstRoot + index - 1) * WarpPortableHeapLayout.RootWords;
            if (heap[root + WarpPortableHeapLayout.RootState] != WarpPortableHeapLayout.Free ||
                heap[root + WarpPortableHeapLayout.RootGeneration] != 1 || heap[root + WarpPortableHeapLayout.RootOwnership] != WarpPortableHeapLayout.UnownedRoot)
            {
                throw Invalid("A finite fault pool cannot repurpose a host root or another runtime's reserved root.");
            }
        }
    }

    private void WriteHeader(uint[] arena, uint descriptor, uint rows, uint resources, uint prepared, uint text, uint firstRoot)
    {
        arena[descriptor] = WarpPortableSourceFaultFactoryLayout.Magic; arena[descriptor + 1] = WarpPortableSourceFaultFactoryLayout.Version;
        arena[descriptor + WarpPortableSourceFaultFactoryLayout.DescriptorWords] = WordCount;
        arena[descriptor + WarpPortableSourceFaultFactoryLayout.Context] = arena[WarpPortableHeapLayout.Context];
        arena[descriptor + WarpPortableSourceFaultFactoryLayout.RowCount] = (uint)Rows.Length;
        arena[descriptor + WarpPortableSourceFaultFactoryLayout.RowStart] = rows;
        arena[descriptor + WarpPortableSourceFaultFactoryLayout.ResourceCount] = (uint)Resources.Length;
        arena[descriptor + WarpPortableSourceFaultFactoryLayout.ResourceStart] = resources;
        arena[descriptor + WarpPortableSourceFaultFactoryLayout.PreparedCount] = PreparedCount;
        arena[descriptor + WarpPortableSourceFaultFactoryLayout.PreparedStart] = prepared;
        arena[descriptor + WarpPortableSourceFaultFactoryLayout.TextStart] = text;
        arena[descriptor + WarpPortableSourceFaultFactoryLayout.TextUnits] = WordCount - (text - descriptor);
        arena[descriptor + WarpPortableSourceFaultFactoryLayout.RootFirst] = firstRoot;
        arena[descriptor + WarpPortableSourceFaultFactoryLayout.RootCount] = RootCount;
        arena[descriptor + WarpPortableSourceFaultFactoryLayout.StringType] = StringType;
        arena[descriptor + WarpPortableSourceFaultFactoryLayout.ReportsPerRow] = ReportsPerRow;
        WriteHash(arena, descriptor + WarpPortableSourceFaultFactoryLayout.PlanHash, PlanHash);
        WriteHash(arena, descriptor + WarpPortableSourceFaultFactoryLayout.SchemaHash, Contract.TypeSchemaHash);
        WriteHash(arena, descriptor + WarpPortableSourceFaultFactoryLayout.ResourceHash, Contract.Resources.ContractHash);
        WriteHash(arena, descriptor + WarpPortableSourceFaultFactoryLayout.ContractHash, Contract.ContractHash);
    }

    private void WriteRows(uint[] arena, uint start)
    {
        foreach (WarpPortableSourceFaultPoolRow row in Rows)
        {
            uint offset = start + (row.Factory.Id - 1) * WarpPortableSourceFaultFactoryLayout.RowWords;
            arena[offset] = row.Factory.Id; arena[offset + WarpPortableSourceFaultFactoryLayout.RowFunction] = row.Function;
            arena[offset + WarpPortableSourceFaultFactoryLayout.RowOffset] = checked((uint)row.Factory.SourceOffset);
            arena[offset + WarpPortableSourceFaultFactoryLayout.RowOpcode] = unchecked((ushort)row.Factory.OpCode);
            arena[offset + WarpPortableSourceFaultFactoryLayout.RowEffect] = checked((uint)row.Factory.EffectIndex);
            arena[offset + WarpPortableSourceFaultFactoryLayout.RowExceptionType] = row.Factory.ExceptionType;
            arena[offset + WarpPortableSourceFaultFactoryLayout.RowFaultDescriptor] = row.Factory.FaultDescriptor;
            arena[offset + WarpPortableSourceFaultFactoryLayout.RowMessage] = row.MessageResource;
            arena[offset + WarpPortableSourceFaultFactoryLayout.RowParamName] = row.ParamNameResource;
            WriteHash(arena, offset + WarpPortableSourceFaultFactoryLayout.RowOperationHash,
                WarpPortableSourceUtf16Identity.Hash(row.Factory.OperationIdentity));
        }
    }

    private void WriteResources(uint[] arena, uint start, uint text, uint firstRoot)
    {
        foreach (WarpPortableSourceFaultPoolResource resource in Resources)
        {
            uint row = start + (resource.Id - 1) * WarpPortableSourceFaultFactoryLayout.ResourceWords;
            arena[row + WarpPortableSourceFaultFactoryLayout.ResourceText] = text;
            arena[row + WarpPortableSourceFaultFactoryLayout.ResourceLength] = (uint)resource.Text.Length;
            arena[row + WarpPortableSourceFaultFactoryLayout.ResourceRoot] = firstRoot + resource.Id - 1;
            arena[row + WarpPortableSourceFaultFactoryLayout.ResourceRootGeneration] = 1;
            foreach (char character in resource.Text) { arena[text++] = character; }
        }
    }

    private void WritePrepared(uint[] arena, uint start, uint firstRoot)
    {
        for (uint index = 0; index < PreparedCount; index++)
        {
            WarpPortableSourceFaultPoolRow row = Rows[(int)(index / ReportsPerRow)];
            uint record = start + index * WarpPortableSourceFaultFactoryLayout.PreparedWords;
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedFactoryRow] = row.Factory.Id;
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRoot] = firstRoot + (uint)Resources.Length + index;
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRootGeneration] = 1;
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedFunction] = row.Function;
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedOffset] = checked((uint)row.Factory.SourceOffset);
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedOpcode] = unchecked((ushort)row.Factory.OpCode);
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedEffect] = checked((uint)row.Factory.EffectIndex);
        }
    }

    private static void WriteHash(uint[] arena, uint start, string identity)
    {
        byte[] hash = Convert.FromHexString(identity);
        for (int word = 0; word < 8; word++) { arena[start + (uint)word] = BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(word * 4)); }
    }
}
