namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    private static uint SourceInitializerFailureDescriptor(uint[] arena)
    {
        uint descriptor = arena[WarpPortableSourceInitializerFailureLayout.Descriptor], end = arena[WarpPortableHeapLayout.DataStart];
        if (end > (uint)arena.Length || descriptor < WarpPortableHeapLayout.HeaderWords || descriptor > end ||
            WarpPortableSourceInitializerFailureLayout.HeaderWords > end - descriptor ||
            arena[descriptor] != WarpPortableSourceInitializerFailureLayout.Magic || arena[descriptor + 1] != WarpPortableSourceInitializerFailureLayout.Version ||
            arena[descriptor + WarpPortableSourceInitializerFailureLayout.Context] != arena[WarpPortableHeapLayout.Context])
        {
            Fail(arena, WarpPortableHeapLayout.InvalidOperation, descriptor, end); return 0;
        }
        uint words = arena[descriptor + WarpPortableSourceInitializerFailureLayout.DescriptorWords];
        uint types = arena[descriptor + WarpPortableSourceInitializerFailureLayout.TypeCount], rows = arena[descriptor + WarpPortableSourceInitializerFailureLayout.RowCount];
        uint typeStart = arena[descriptor + WarpPortableSourceInitializerFailureLayout.TypeStart];
        uint rowStart = arena[descriptor + WarpPortableSourceInitializerFailureLayout.RowStart];
        uint text = arena[descriptor + WarpPortableSourceInitializerFailureLayout.TextStart], textUnits = arena[descriptor + WarpPortableSourceInitializerFailureLayout.TextUnits];
        if (words < WarpPortableSourceInitializerFailureLayout.HeaderWords || words > end - descriptor || types == 0 || types > 4096 || rows == 0 || rows > 1048576 ||
            typeStart != descriptor + WarpPortableSourceInitializerFailureLayout.HeaderWords || typeStart > descriptor + words ||
            types * WarpPortableSourceInitializerFailureLayout.TypeWords > descriptor + words - typeStart ||
            rowStart != typeStart + types * WarpPortableSourceInitializerFailureLayout.TypeWords || rowStart > descriptor + words ||
            rows * WarpPortableSourceInitializerFailureLayout.RowWords > descriptor + words - rowStart ||
            text != rowStart + rows * WarpPortableSourceInitializerFailureLayout.RowWords || text > descriptor + words || textUnits != descriptor + words - text)
        {
            Fail(arena, WarpPortableHeapLayout.Bounds, descriptor, words); return 0;
        }
        uint first = arena[descriptor + WarpPortableSourceInitializerFailureLayout.RootFirst], count = arena[descriptor + WarpPortableSourceInitializerFailureLayout.RootCount];
        if (count != types * 3 || first == 0 || first > arena[WarpPortableHeapLayout.RootCount] || count > arena[WarpPortableHeapLayout.RootCount] - first + 1)
        {
            Fail(arena, WarpPortableHeapLayout.InvalidReference, first, count); return 0;
        }
        uint memory = SourceViewDescriptor(arena);
        if (memory == 0) { Fail(arena, WarpPortableHeapLayout.InvalidType, descriptor, memory); return 0; }
        for (uint word = 0; word < 8; word++)
        {
            if (arena[descriptor + WarpPortableSourceInitializerFailureLayout.SchemaHash + word] != arena[memory + WarpPortableSourceMemoryLayout.Hash + word])
            {
                Fail(arena, WarpPortableHeapLayout.InvalidType, descriptor, word); return 0;
            }
        }
        return descriptor;
    }

    private static uint SourceInitializerFailureType(uint[] arena, uint descriptor, uint id)
    {
        if (id == 0 || id > arena[descriptor + WarpPortableSourceInitializerFailureLayout.TypeCount])
        {
            Fail(arena, WarpPortableHeapLayout.Bounds, id, 0); return 0;
        }
        uint record = arena[descriptor + WarpPortableSourceInitializerFailureLayout.TypeStart] + (id - 1) * WarpPortableSourceInitializerFailureLayout.TypeWords;
        uint type = arena[record + WarpPortableSourceInitializerFailureLayout.TypeId], wrapper = arena[record + WarpPortableSourceInitializerFailureLayout.WrapperType];
        if (RequireType(arena, type) != 0 || RequireType(arena, wrapper) != 0 ||
            arena[record + WarpPortableSourceInitializerFailureLayout.FirstRoot] != arena[descriptor + WarpPortableSourceInitializerFailureLayout.RootFirst] + (id - 1) * 3 ||
            arena[record + WarpPortableSourceInitializerFailureLayout.RootGeneration] == 0)
        {
            Fail(arena, WarpPortableHeapLayout.InvalidType, id, type); return 0;
        }
        uint memory = arena[WarpPortableSourceMemoryLayout.Descriptor];
        uint exception = arena[memory + WarpPortableSourceMemoryLayout.ExceptionTypeStart] + (wrapper - 1) * WarpPortableSourceExceptionLayout.ExceptionTypeWords;
        if (arena[exception + WarpPortableSourceExceptionLayout.ExceptionTypeKind] != WarpPortableSourceExceptionKind.TypeInitialization ||
            arena[exception + WarpPortableSourceExceptionLayout.ExceptionTypeHResult] != arena[record + WarpPortableSourceInitializerFailureLayout.DefaultHResult] ||
            arena[Type(arena, wrapper) + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.Class ||
            arena[Type(arena, wrapper) + WarpPortableHeapLayout.TypePayloadWords] < WarpPortableSourceExceptionLayout.PrefixWords)
        {
            Fail(arena, WarpPortableHeapLayout.InvalidType, id, wrapper); return 0;
        }
        return record;
    }

    private static uint SourceInitializerFailureOrigin(uint[] arena, uint descriptor, uint id)
    {
        if (id == 0 || id > arena[descriptor + WarpPortableSourceInitializerFailureLayout.RowCount])
        {
            Fail(arena, WarpPortableHeapLayout.Bounds, id, 0); return 0;
        }
        uint row = arena[descriptor + WarpPortableSourceInitializerFailureLayout.RowStart] + (id - 1) * WarpPortableSourceInitializerFailureLayout.RowWords;
        uint kind = arena[row + WarpPortableSourceInitializerFailureLayout.RowOriginKind];
        if (arena[row] != id || arena[row + WarpPortableSourceInitializerFailureLayout.RowCapturedMethodIndex] == 0 ||
            kind != (uint)WarpPortableSourceOperationOriginKind.Instruction && kind != (uint)WarpPortableSourceOperationOriginKind.EntryInvocation ||
            kind == (uint)WarpPortableSourceOperationOriginKind.Instruction && arena[row + WarpPortableSourceInitializerFailureLayout.RowOpcode] > 0xFFFFu ||
            kind == (uint)WarpPortableSourceOperationOriginKind.EntryInvocation && (arena[row + WarpPortableSourceInitializerFailureLayout.RowOffset] |
                arena[row + WarpPortableSourceInitializerFailureLayout.RowOpcode] | arena[row + WarpPortableSourceInitializerFailureLayout.RowEffect]) != 0)
        {
            Fail(arena, WarpPortableHeapLayout.InvalidOperation, id, kind); return 0;
        }
        return row;
    }
}
