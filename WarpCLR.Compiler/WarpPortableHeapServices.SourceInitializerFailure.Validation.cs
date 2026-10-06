namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    private static uint RequireEmptyInitializerFailure(uint[] arena, uint descriptor, uint record)
    {
        uint type = Type(arena, arena[record + WarpPortableSourceInitializerFailureLayout.TypeId]);
        if (arena[record + WarpPortableSourceInitializerFailureLayout.State] != WarpPortableSourceInitializerFailureLayout.Empty ||
            arena[type + WarpPortableHeapLayout.InitializerState] != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, record, arena[record + WarpPortableSourceInitializerFailureLayout.State]);
        }
        for (uint word = WarpPortableSourceInitializerFailureLayout.WrapperReference; word < WarpPortableSourceInitializerFailureLayout.FirstRoot; word++)
        {
            if (arena[record + word] != 0) { return Fail(arena, WarpPortableHeapLayout.InvalidOperation, record, word); }
        }
        for (uint root = 0; root < 3; root++)
        {
            if (RequireFaultPoolRoot(arena, descriptor, arena[record + WarpPortableSourceInitializerFailureLayout.FirstRoot] + root,
                arena[record + WarpPortableSourceInitializerFailureLayout.RootGeneration], 0, 0, 0, 1) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        }
        return InitializerFailureText(arena, descriptor, record, 0) == 0 || InitializerFailureText(arena, descriptor, record, 1) == 0 ? arena[WarpPortableHeapLayout.Fault] : 0;
    }

    private static uint RequireReadyInitializerFailure(uint[] arena, uint descriptor, uint record, uint expectedState)
    {
        if (arena[record + WarpPortableSourceInitializerFailureLayout.State] != expectedState)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, record, expectedState);
        }
        if (RequireInitializerFailureString(arena, descriptor, record, 0) != 0 || RequireInitializerFailureString(arena, descriptor, record, 1) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint reference = record + WarpPortableSourceInitializerFailureLayout.WrapperReference;
        uint context = arena[reference], slot = arena[reference + 1], generation = arena[reference + 2];
        if (SourceExceptionRecord(arena, context, slot, generation) == 0 ||
            arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotType] != arena[record + WarpPortableSourceInitializerFailureLayout.WrapperType] ||
            RequireFaultPoolRoot(arena, descriptor, arena[record + WarpPortableSourceInitializerFailureLayout.FirstRoot] + 2,
                arena[record + WarpPortableSourceInitializerFailureLayout.RootGeneration], context, slot, generation, 0) != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidReference, slot, generation);
        }
        return 0;
    }

    private static uint ValidateCachedInitializerFailure(uint[] arena, uint record)
    {
        uint type = Type(arena, arena[record + WarpPortableSourceInitializerFailureLayout.TypeId]);
        uint wrapper = record + WarpPortableSourceInitializerFailureLayout.WrapperReference;
        if (arena[type + WarpPortableHeapLayout.InitializerState] != 3 ||
            InitializerFailureReferenceMatches(arena, wrapper, type + WarpPortableHeapLayout.InitializerException) == 0 ||
            RequireSourceExceptionDataInitialized(arena, arena[wrapper + 1], arena[wrapper + 2]) != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, record, arena[type + WarpPortableHeapLayout.InitializerState]);
        }
        uint inner = record + WarpPortableSourceInitializerFailureLayout.InnerReference;
        uint payload = arena[Slot(arena, arena[wrapper + 1]) + WarpPortableHeapLayout.SlotPayload];
        if (SourceExceptionRecord(arena, arena[inner], arena[inner + 1], arena[inner + 2]) == 0 ||
            InitializerFailureReferenceMatches(arena, inner, payload + WarpPortableSourceExceptionLayout.InnerExceptionWord) == 0 ||
            InitializerFailureReferenceMatches(arena, record + WarpPortableSourceInitializerFailureLayout.NameReference, payload + WarpPortableSourceExceptionLayout.TypeNameWord) == 0 ||
            InitializerFailureReferenceMatches(arena, record + WarpPortableSourceInitializerFailureLayout.MessageReference, payload + WarpPortableSourceExceptionLayout.MessageWord) == 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidReference, arena[wrapper + 1], arena[inner + 1]);
        }
        // HResult may legitimately have been changed through the source setter.
        // The original Inner owner and every raw name/message unit remain exact.
        return 0;
    }

    private static uint InitializerFailureReferenceMatches(uint[] arena, uint source, uint destination) =>
        arena[source] == arena[destination] && arena[source + 1] == arena[destination + 1] && arena[source + 2] == arena[destination + 2] ? 1u : 0u;
}
