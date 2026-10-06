using System.Buffers.Binary;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceInitializerFailurePlan
{
    // Immutable table attachment and disjoint pristine runtime root reservation.
    // No source object, source invocation ticket or nonce is created here.
    internal uint[] AttachToEmptyHeap(uint[] heap, uint firstRoot)
    {
        ArgumentNullException.ThrowIfNull(heap); ValidateEmptyHeap(heap); ValidateRoots(heap, firstRoot);
        uint descriptor = heap[WarpPortableHeapLayout.DataStart], end = checked(descriptor + WordCount);
        var arena = new uint[checked(heap.Length + (int)WordCount)];
        heap.AsSpan(0, (int)descriptor).CopyTo(arena); heap.AsSpan((int)descriptor).CopyTo(arena.AsSpan((int)end));
        arena[3] = checked((uint)arena.Length); arena[WarpPortableHeapLayout.DataStart] = end;
        arena[WarpPortableSourceInitializerFailureLayout.Descriptor] = descriptor;
        uint types = descriptor + WarpPortableSourceInitializerFailureLayout.HeaderWords;
        uint rows = types + (uint)Types.Length * WarpPortableSourceInitializerFailureLayout.TypeWords;
        uint text = rows + (uint)Rows.Length * WarpPortableSourceInitializerFailureLayout.RowWords;
        Header(arena, descriptor, types, rows, text, firstRoot);
        TypeRows(arena, types, text, firstRoot); OriginRows(arena, rows);
        for (uint index = 0; index < RootCount; index++)
        {
            uint root = arena[WarpPortableHeapLayout.RootStart] + (firstRoot + index - 1) * WarpPortableHeapLayout.RootWords;
            arena[root + WarpPortableHeapLayout.RootOwnership] = WarpPortableHeapLayout.RuntimeOwnedRoot;
        }
        return arena;
    }

    private void ValidateEmptyHeap(uint[] heap)
    {
        if (Rows.IsEmpty || heap.Length < WarpPortableHeapLayout.HeaderWords || heap[0] != WarpPortableHeapLayout.Magic ||
            heap[1] != WarpPortableHeapLayout.Version || heap[3] != heap.Length || heap[WarpPortableHeapLayout.Context] == 0 ||
            heap[WarpPortableSourceInitializerFailureLayout.Descriptor] != 0 || heap[WarpPortableHeapLayout.UsedWords] != 0 ||
            heap[WarpPortableHeapLayout.LiveObjects] != 0 || heap[WarpPortableHeapLayout.LiveRoots] != 0 ||
            heap[WarpPortableHeapLayout.ActiveWorkers] != 0 || heap[WarpPortableHeapLayout.LeaseState] != 0 ||
            heap[WarpPortableHeapLayout.PendingResult] != 0 || heap[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Idle ||
            heap[WarpPortableHeapLayout.DataStart] < WarpPortableHeapLayout.HeaderWords || heap[WarpPortableHeapLayout.DataStart] > heap.Length)
        {
            throw Invalid("Initializer failure data can attach only to one unused idle schema-bound heap.");
        }
        uint memory = heap[WarpPortableSourceMemoryLayout.Descriptor];
        if (memory < WarpPortableHeapLayout.HeaderWords || memory > heap.Length - WarpPortableSourceMemoryLayout.HeaderWords ||
            heap[memory] != WarpPortableSourceMemoryLayout.Magic || heap[memory + 1] != WarpPortableSourceMemoryLayout.Version)
        {
            throw Invalid("Initializer failure data requires the captured source memory descriptor.");
        }
        byte[] hash = Convert.FromHexString(TypeSchemaHash);
        for (int word = 0; word < 8; word++)
        {
            if (heap[memory + WarpPortableSourceMemoryLayout.Hash + (uint)word] != BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(word * 4)))
            {
                throw Invalid("Initializer failure data belongs to another exact type/schema graph.");
            }
        }
    }

    private void ValidateRoots(uint[] heap, uint first)
    {
        uint count = heap[WarpPortableHeapLayout.RootCount], start = heap[WarpPortableHeapLayout.RootStart];
        if (first == 0 || first > count || RootCount > count - first + 1 || start > heap.Length ||
            (long)count * WarpPortableHeapLayout.RootWords > heap.Length - start)
        {
            throw Invalid("Initializer data needs disjoint roots for every captured name, message and reserved wrapper.");
        }
        for (uint index = 0; index < RootCount; index++)
        {
            uint root = start + (first + index - 1) * WarpPortableHeapLayout.RootWords;
            if (heap[root + WarpPortableHeapLayout.RootState] != WarpPortableHeapLayout.Free ||
                heap[root + WarpPortableHeapLayout.RootGeneration] != 1 || heap[root + WarpPortableHeapLayout.RootOwnership] != WarpPortableHeapLayout.UnownedRoot)
            {
                throw Invalid("Initializer data cannot repurpose an existing host or runtime root.");
            }
        }
    }

    private void Header(uint[] arena, uint descriptor, uint types, uint rows, uint text, uint firstRoot)
    {
        arena[descriptor] = WarpPortableSourceInitializerFailureLayout.Magic; arena[descriptor + 1] = WarpPortableSourceInitializerFailureLayout.Version;
        arena[descriptor + WarpPortableSourceInitializerFailureLayout.DescriptorWords] = WordCount;
        arena[descriptor + WarpPortableSourceInitializerFailureLayout.Context] = arena[WarpPortableHeapLayout.Context];
        arena[descriptor + WarpPortableSourceInitializerFailureLayout.TypeCount] = (uint)Types.Length;
        arena[descriptor + WarpPortableSourceInitializerFailureLayout.TypeStart] = types;
        arena[descriptor + WarpPortableSourceInitializerFailureLayout.RowCount] = (uint)Rows.Length;
        arena[descriptor + WarpPortableSourceInitializerFailureLayout.RowStart] = rows;
        arena[descriptor + WarpPortableSourceInitializerFailureLayout.TextStart] = text;
        arena[descriptor + WarpPortableSourceInitializerFailureLayout.TextUnits] = WordCount - (text - descriptor);
        arena[descriptor + WarpPortableSourceInitializerFailureLayout.StringType] = StringType;
        arena[descriptor + WarpPortableSourceInitializerFailureLayout.RootFirst] = firstRoot;
        arena[descriptor + WarpPortableSourceInitializerFailureLayout.RootCount] = RootCount;
        Hash(arena, descriptor + WarpPortableSourceInitializerFailureLayout.PlanHash, PlanHash);
        Hash(arena, descriptor + WarpPortableSourceInitializerFailureLayout.SchemaHash, TypeSchemaHash);
        Hash(arena, descriptor + WarpPortableSourceInitializerFailureLayout.InitializerPlanHash, InitializerPlanHash);
        Hash(arena, descriptor + WarpPortableSourceInitializerFailureLayout.ResourceHash, Resources.ContractHash);
    }

    private void TypeRows(uint[] arena, uint start, uint text, uint firstRoot)
    {
        foreach (WarpPortableSourceInitializerFailureType type in Types)
        {
            uint record = start + (type.Id - 1) * WarpPortableSourceInitializerFailureLayout.TypeWords;
            arena[record + WarpPortableSourceInitializerFailureLayout.TypeId] = type.Type;
            arena[record + WarpPortableSourceInitializerFailureLayout.WrapperType] = type.WrapperType;
            arena[record + WarpPortableSourceInitializerFailureLayout.DefaultHResult] = type.DefaultHResult;
            arena[record + WarpPortableSourceInitializerFailureLayout.NameText] = text;
            arena[record + WarpPortableSourceInitializerFailureLayout.NameUnits] = (uint)type.TypeName.Length;
            foreach (ushort unit in type.TypeName) { arena[text++] = unit; }
            arena[record + WarpPortableSourceInitializerFailureLayout.MessageText] = text;
            arena[record + WarpPortableSourceInitializerFailureLayout.MessageUnits] = (uint)type.Message.Length;
            foreach (ushort unit in type.Message) { arena[text++] = unit; }
            arena[record + WarpPortableSourceInitializerFailureLayout.FirstRoot] = firstRoot + (type.Id - 1) * 3;
            arena[record + WarpPortableSourceInitializerFailureLayout.RootGeneration] = 1;
            Hash(arena, record + WarpPortableSourceInitializerFailureLayout.DataHash, type.DataHash);
        }
    }

    private void OriginRows(uint[] arena, uint start)
    {
        foreach (WarpPortableSourceInitializerFailureRow row in Rows)
        {
            uint offset = start + (row.Id - 1) * WarpPortableSourceInitializerFailureLayout.RowWords;
            arena[offset] = row.Id; arena[offset + WarpPortableSourceInitializerFailureLayout.RowTypeRecord] = row.TypeRecord;
            arena[offset + WarpPortableSourceInitializerFailureLayout.RowOriginKind] = (uint)row.Origin.Kind;
            arena[offset + WarpPortableSourceInitializerFailureLayout.RowCapturedMethodIndex] = row.CapturedMethodIndex;
            // Inactive fields are zero only under the explicit invocation tag;
            // zero never represents an invented original opcode or effect.
            arena[offset + WarpPortableSourceInitializerFailureLayout.RowOffset] = checked((uint)(row.Origin.SourceOffset ?? 0));
            arena[offset + WarpPortableSourceInitializerFailureLayout.RowOpcode] = row.Origin.SourceOpCode ?? 0;
            arena[offset + WarpPortableSourceInitializerFailureLayout.RowEffect] = checked((uint)(row.Origin.EffectIndex ?? 0));
            Hash(arena, offset + WarpPortableSourceInitializerFailureLayout.RowSourceHash, row.Origin.CapturedSourceHash);
            Hash(arena, offset + WarpPortableSourceInitializerFailureLayout.RowOriginHash, row.Origin.OriginHash);
        }
    }

    private static void Hash(uint[] arena, uint start, string hash)
    {
        byte[] bytes = Convert.FromHexString(hash);
        for (int word = 0; word < 8; word++) { arena[start + (uint)word] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(word * 4)); }
    }

    private static WarpVerificationException Invalid(string message) => new("WRPCLR2490", message, 0);
}
