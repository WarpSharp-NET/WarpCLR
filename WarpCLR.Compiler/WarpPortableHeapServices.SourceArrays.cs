namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    // Scratch contains exactly rank (signed lower bound, signed length) pairs.
    public static uint AllocateSourceArray(uint[] arena, uint typeId, uint rank, uint scratchOffset)
    {
        if (Begin(arena, 46) != 0 || RequireType(arena, typeId) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint descriptor = SourceViewDescriptor(arena);
        if (descriptor == 0 || rank == 0 || rank > WarpPortableSourceArrayLayout.MaximumRank)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidType, typeId, rank);
        }
        uint sourceType = SourceMemoryType(arena, descriptor, typeId);
        uint kind = arena[Type(arena, typeId) + WarpPortableHeapLayout.TypeKind];
        if (arena[sourceType + WarpPortableSourceArrayLayout.TypeRank] != rank ||
            (kind != WarpPortableHeapLayout.ValueArray && kind != WarpPortableHeapLayout.ReferenceArray))
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidType, typeId, rank);
        }
        if (RequireScratch(arena, scratchOffset, rank * 2) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint shape = arena[WarpPortableHeapLayout.ScratchStart] + scratchOffset;
        uint total = SourceArrayLength(arena, sourceType, shape, rank);
        if (arena[WarpPortableHeapLayout.Fault] != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        if (rank == 1 && arena[shape] == 0)
        {
            typeId = arena[sourceType + WarpPortableSourceArrayLayout.TypeVectorIdentity];
            if (RequireType(arena, typeId) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
            sourceType = SourceMemoryType(arena, descriptor, typeId);
        }
        if (arena[WarpPortableHeapLayout.LeaseState] == 0) { return Fail(arena, WarpPortableHeapLayout.Busy, 46, 0); }
        if (Allocate(arena, typeId, kind, total) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint slot = arena[WarpPortableHeapLayout.Result + 1];
        uint record = arena[descriptor + WarpPortableSourceArrayLayout.ShapeStart] + (slot - 1) * WarpPortableSourceArrayLayout.ShapeWords;
        for (uint word = 0; word < WarpPortableSourceArrayLayout.ShapeWords; word++) { arena[record + word] = 0; }
        arena[record + WarpPortableSourceArrayLayout.Generation] = arena[WarpPortableHeapLayout.Result + 2];
        arena[record + WarpPortableSourceArrayLayout.Type] = typeId;
        arena[record + WarpPortableSourceArrayLayout.Rank] = rank;
        arena[record + WarpPortableSourceArrayLayout.Vector] = arena[sourceType + WarpPortableSourceArrayLayout.TypeVector];
        for (uint dimension = 0; dimension < rank * 2; dimension++) { arena[record + WarpPortableSourceArrayLayout.Dimensions + dimension] = arena[shape + dimension]; }
        return 0;
    }

    private static uint SourceArrayLength(uint[] arena, uint type, uint shape, uint rank)
    {
        uint dimensionsExceeded = 0; uint total = 1;
        for (uint dimension = 0; dimension < rank; dimension++)
        {
            uint lower = arena[shape + dimension * 2]; uint length = arena[shape + dimension * 2 + 1];
            if (arena[type + WarpPortableSourceArrayLayout.TypeVector] != 0 && lower != 0)
            {
                return Fail(arena, WarpPortableHeapLayout.InvalidType, lower, length);
            }
            if (length > 0x7FFFFFFFu)
            {
                return Fail(arena, WarpPortableSourceArrayLayout.ArithmeticOverflow, lower, length);
            }
            if (length != 0 && (lower & 0x80000000u) == 0 && length - 1 > 0x7FFFFFFFu - lower)
            {
                return Fail(arena, WarpPortableSourceArrayLayout.ArgumentOutOfRange, lower, length);
            }
            if (length > WarpPortableSourceArrayLayout.MaximumDimensionLength) { dimensionsExceeded = 1; }
            uint high = WarpPortableWordMath.MultiplyHigh(total, length); uint low = total * length;
            if (high != 0) { return Fail(arena, WarpPortableSourceArrayLayout.ManagedDimensionsExceeded, total, length); }
            total = low;
        }
        if (dimensionsExceeded != 0) { return Fail(arena, WarpPortableSourceArrayLayout.ManagedDimensionsExceeded, total, rank); }
        return total;
    }

    public static uint SourceArrayDimension(uint[] arena, uint context, uint slot, uint generation, uint dimension, uint operation)
    {
        if (Begin(arena, 47) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint record = SourceArrayShape(arena, context, slot, generation);
        if (record == 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint rank = arena[record + WarpPortableSourceArrayLayout.Rank];
        if (operation == 3) { arena[WarpPortableHeapLayout.Result] = rank; return 0; }
        if (dimension >= rank) { return Fail(arena, WarpPortableHeapLayout.Bounds, dimension, rank); }
        if (operation > 2) { return Fail(arena, WarpPortableHeapLayout.InvalidOperation, operation, dimension); }
        uint row = record + WarpPortableSourceArrayLayout.Dimensions + dimension * 2;
        uint lower = arena[row]; uint length = arena[row + 1];
        arena[WarpPortableHeapLayout.Result] = operation == 0 ? length : operation == 1 ? lower : lower + length - 1;
        return 0;
    }

    // Scratch contains rank raw Int32 indices. The result is a byte address,
    // preserving element packing; it is never a public numeric array reference.
    public static uint SourceArrayElementOffset(uint[] arena, uint context, uint slot, uint generation, uint rank, uint scratchOffset)
    {
        if (Begin(arena, 48) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint record = SourceArrayShape(arena, context, slot, generation);
        if (record == 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint actualRank = arena[record + WarpPortableSourceArrayLayout.Rank];
        if (rank != actualRank) { return Fail(arena, WarpPortableHeapLayout.InvalidType, rank, actualRank); }
        if (RequireScratch(arena, scratchOffset, rank) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint indices = arena[WarpPortableHeapLayout.ScratchStart] + scratchOffset; uint flat = 0;
        for (uint dimension = 0; dimension < rank; dimension++)
        {
            uint row = record + WarpPortableSourceArrayLayout.Dimensions + dimension * 2;
            uint relative = arena[indices + dimension] - arena[row]; uint length = arena[row + 1];
            if (relative >= length) { return Fail(arena, WarpPortableHeapLayout.Bounds, arena[indices + dimension], dimension); }
            flat = flat * length + relative;
        }
        uint typeId = arena[record + WarpPortableSourceArrayLayout.Type];
        uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
        uint stride = arena[SourceMemoryType(arena, descriptor, typeId) + WarpPortableSourceMemoryLayout.StrideBytes];
        if (WarpPortableWordMath.MultiplyHigh(flat, stride) != 0) { return Fail(arena, WarpPortableHeapLayout.Bounds, flat, stride); }
        arena[WarpPortableHeapLayout.Result] = flat * stride;
        return 0;
    }

    private static uint SourceArrayShape(uint[] arena, uint context, uint slot, uint generation)
    {
        if (RequireReference(arena, context, slot, generation, 0) != 0) { return 0; }
        uint descriptor = SourceViewDescriptor(arena);
        if (descriptor == 0) { Fail(arena, WarpPortableHeapLayout.InvalidType, slot, 0); return 0; }
        uint entry = Slot(arena, slot); uint kind = arena[entry + WarpPortableHeapLayout.SlotKind];
        if (kind != WarpPortableHeapLayout.ValueArray && kind != WarpPortableHeapLayout.ReferenceArray)
        {
            Fail(arena, WarpPortableHeapLayout.InvalidType, slot, kind); return 0;
        }
        uint record = arena[descriptor + WarpPortableSourceArrayLayout.ShapeStart] + (slot - 1) * WarpPortableSourceArrayLayout.ShapeWords;
        uint type = arena[entry + WarpPortableHeapLayout.SlotType]; uint captured = SourceMemoryType(arena, descriptor, type);
        uint rank = arena[record + WarpPortableSourceArrayLayout.Rank];
        if (arena[record + WarpPortableSourceArrayLayout.Generation] != generation || arena[record + WarpPortableSourceArrayLayout.Type] != type ||
            rank == 0 || rank > WarpPortableSourceArrayLayout.MaximumRank || rank != arena[captured + WarpPortableSourceArrayLayout.TypeRank] ||
            arena[record + WarpPortableSourceArrayLayout.Vector] != arena[captured + WarpPortableSourceArrayLayout.TypeVector])
        {
            Fail(arena, WarpPortableHeapLayout.InvalidType, type, rank); return 0;
        }
        return record;
    }
}
