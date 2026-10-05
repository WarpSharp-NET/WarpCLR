namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public const string ExceptionAccessorSemantics = "warp.exception-data-accessors/exact-declaring-type-initialized-owner-lease-base-inner-chain-raw-hresult-parameter-actual-typename/0.1";

    // This is the compiled data algorithm, not a fault factory or invocation grant.
    // Caller admission must prove the nonnull initialized input domain separately.
    public static uint ReadSourceExceptionAccessor(uint[] arena, uint context, uint slot, uint generation,
        uint declaringType, uint accessor)
    {
        if (Begin(arena, 70) != 0 || RequireType(arena, declaringType) != 0 ||
            SourceExceptionRecord(arena, context, slot, generation) == 0 || RequireSourceExceptionLease(arena) != 0 ||
            RequireSourceExceptionDataInitialized(arena, slot, generation) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint entry = Slot(arena, slot);
        if (Assignable(arena, arena[entry + WarpPortableHeapLayout.SlotType], declaringType) == 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidCast, slot, declaringType);
        }
        if (accessor == 5) { return SourceBaseException(arena, context, slot, generation); }
        uint field = accessor == 0 ? WarpPortableSourceExceptionLayout.InnerExceptionWord :
            accessor == 1 ? WarpPortableSourceExceptionLayout.HResultWord :
            accessor == 2 ? WarpPortableSourceExceptionLayout.ParamNameWord :
            accessor == 3 ? WarpPortableSourceExceptionLayout.ActualValueWord :
            accessor == 4 ? WarpPortableSourceExceptionLayout.TypeNameWord : uint.MaxValue;
        if (field == uint.MaxValue) { return Fail(arena, WarpPortableHeapLayout.InvalidOperation, accessor, declaringType); }
        uint record = SourceExceptionRecord(arena, context, slot, generation);
        uint kind = arena[record + WarpPortableSourceExceptionLayout.ExceptionTypeKind];
        if (SourceExceptionFieldAllowed(kind, field) == 0) { return Fail(arena, WarpPortableHeapLayout.InvalidOperation, field, kind); }
        uint payload = arena[entry + WarpPortableHeapLayout.SlotPayload];
        if (accessor == 1) { arena[WarpPortableHeapLayout.Result] = arena[payload + field]; return 0; }
        return SetReferenceResult(arena, arena[payload + field], arena[payload + field + 1], arena[payload + field + 2]);
    }

    private static uint SourceBaseException(uint[] arena, uint context, uint slot, uint generation)
    {
        uint remaining = arena[WarpPortableHeapLayout.SlotCount];
        while (remaining != 0)
        {
            remaining--;
            if (SourceExceptionRecord(arena, context, slot, generation) == 0 ||
                RequireSourceExceptionDataInitialized(arena, slot, generation) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
            uint payload = arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload];
            uint inner = payload + WarpPortableSourceExceptionLayout.InnerExceptionWord;
            if (arena[inner + 1] == 0)
            {
                if (arena[inner] != 0 || arena[inner + 2] != 0) { return Fail(arena, WarpPortableHeapLayout.InvalidReference, arena[inner], arena[inner + 2]); }
                return SetReferenceResult(arena, context, slot, generation);
            }
            context = arena[inner]; slot = arena[inner + 1]; generation = arena[inner + 2];
        }
        return Fail(arena, WarpPortableHeapLayout.InvalidReference, slot, generation);
    }
}
