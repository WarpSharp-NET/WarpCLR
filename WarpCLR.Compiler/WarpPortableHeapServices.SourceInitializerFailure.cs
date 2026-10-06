using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    internal const string SourceInitializerFailureSemantics = "warp.source-initializer-failure-services/reserved-finite-wrapper-exact-original-inner-cached-owner-wrapper-only-trace-reset-consistency-not-source-grant/0.1";

    // Bootstrap under an already held admitted lease. No source or nonce grant.
    public static uint PrepareSourceInitializerFailure(uint[] arena, uint typeRecord)
    {
        if (Begin(arena, 73) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint descriptor = SourceInitializerFailureDescriptor(arena);
        if (descriptor == 0 || RequireSourceExceptionLease(arena) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint record = SourceInitializerFailureType(arena, descriptor, typeRecord);
        if (record == 0 || RequireEmptyInitializerFailure(arena, descriptor, record) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        arena[record + WarpPortableSourceInitializerFailureLayout.State] = WarpPortableSourceInitializerFailureLayout.Preparing;
        if (PrepareInitializerFailureString(arena, descriptor, record, 0) != 0 || PrepareInitializerFailureString(arena, descriptor, record, 1) != 0 ||
            Allocate(arena, arena[record + WarpPortableSourceInitializerFailureLayout.WrapperType], WarpPortableHeapLayout.Class, 0) != 0)
        {
            arena[record + WarpPortableSourceInitializerFailureLayout.State] = WarpPortableSourceInitializerFailureLayout.Failed;
            return arena[WarpPortableHeapLayout.Fault];
        }
        StoreFaultPoolReference(arena, record + WarpPortableSourceInitializerFailureLayout.WrapperReference,
            arena[record + WarpPortableSourceInitializerFailureLayout.FirstRoot] + 2);
        AcknowledgeServiceResult(arena, arena[WarpPortableHeapLayout.LeaseOwner]);
        arena[record + WarpPortableSourceInitializerFailureLayout.State] = WarpPortableSourceInitializerFailureLayout.Ready;
        return 0;
    }

    // Exact object/data and initializer-owner consistency only. The private
    // issuer must bind this origin/state/arena/activation before source Take/Raise.
    public static uint CacheSourceInitializerFailure(uint[] state, uint[] arena, uint originRow, uint worker,
        uint innerContext, uint innerSlot, uint innerGeneration)
    {
        if (Begin(arena, 74) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint descriptor = SourceInitializerFailureDescriptor(arena);
        if (descriptor == 0 || RequireSourceExceptionLease(arena) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint origin = SourceInitializerFailureOrigin(arena, descriptor, originRow);
        if (origin == 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint record = SourceInitializerFailureType(arena, descriptor, arena[origin + WarpPortableSourceInitializerFailureLayout.RowTypeRecord]);
        if (record == 0 || RequireInitializerFailureOwner(state, arena, record, worker) != 0 ||
            RequireReadyInitializerFailure(arena, descriptor, record, WarpPortableSourceInitializerFailureLayout.Ready) != 0 ||
            SourceExceptionRecord(arena, innerContext, innerSlot, innerGeneration) == 0 ||
            RequireSourceExceptionDataInitialized(arena, innerSlot, innerGeneration) != 0 ||
            RequireScratch(arena, 0, WarpPortableSourceExceptionLayout.InputWords) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint input = arena[WarpPortableHeapLayout.ScratchStart];
        for (uint word = 0; word < WarpPortableSourceExceptionLayout.InputWords; word++) { arena[input + word] = 0; }
        CopyInitializerFailureReference(arena, record + WarpPortableSourceInitializerFailureLayout.MessageReference, input);
        arena[input + 3] = innerContext; arena[input + 4] = innerSlot; arena[input + 5] = innerGeneration;
        CopyInitializerFailureReference(arena, record + WarpPortableSourceInitializerFailureLayout.NameReference, input + 12);
        uint wrapper = record + WarpPortableSourceInitializerFailureLayout.WrapperReference;
        if (InitializeSourceExceptionData(arena, arena[wrapper], arena[wrapper + 1], arena[wrapper + 2], 0) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint original = record + WarpPortableSourceInitializerFailureLayout.InnerReference;
        arena[original] = innerContext; arena[original + 1] = innerSlot; arena[original + 2] = innerGeneration;
        uint type = Type(arena, arena[record + WarpPortableSourceInitializerFailureLayout.TypeId]);
        CopyInitializerFailureReference(arena, wrapper, type + WarpPortableHeapLayout.InitializerException);
        arena[record + WarpPortableSourceInitializerFailureLayout.State] = WarpPortableSourceInitializerFailureLayout.Cached;
        arena[type + WarpPortableHeapLayout.InitializerState] = 3;
        // The reserved pool root remains protected. It is never retired here.
        return 0;
    }

    // Reuses the same cached owner and clears only its managed propagation
    // prefix. A later admitted EH raise must install its real new trace root.
    public static uint ReadCachedSourceInitializerFailure(uint[] arena, uint typeRecord)
    {
        if (Begin(arena, 75) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint descriptor = SourceInitializerFailureDescriptor(arena);
        if (descriptor == 0 || RequireSourceExceptionLease(arena) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint record = SourceInitializerFailureType(arena, descriptor, typeRecord);
        if (record == 0 || RequireReadyInitializerFailure(arena, descriptor, record, WarpPortableSourceInitializerFailureLayout.Cached) != 0 ||
            ValidateCachedInitializerFailure(arena, record) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint wrapper = record + WarpPortableSourceInitializerFailureLayout.WrapperReference;
        uint payload = arena[Slot(arena, arena[wrapper + 1]) + WarpPortableHeapLayout.SlotPayload];
        for (uint word = WarpPortableSourceExceptionLayout.TraceReferenceWord; word <= WarpPortableSourceExceptionLayout.ThrowOffsetWord; word++)
        {
            arena[payload + word] = 0;
        }
        return SetReferenceResult(arena, arena[wrapper], arena[wrapper + 1], arena[wrapper + 2]);
    }

    private static uint RequireInitializerFailureOwner(uint[] state, uint[] arena, uint record, uint worker)
    {
        if ((uint)state.Length < (uint)WarpLogicalMachineLayout.HeaderWords || state[WarpLogicalMachineLayout.OwnerContextOffset] == 0 ||
            state[WarpLogicalMachineLayout.DepthOffset] == 0 || worker >= arena[WarpPortableHeapLayout.WorkerCount])
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, worker, record);
        }
        uint type = Type(arena, arena[record + WarpPortableSourceInitializerFailureLayout.TypeId]);
        return arena[type + WarpPortableHeapLayout.InitializerState] == 1 && arena[type + WarpPortableHeapLayout.InitializerOwner] == worker &&
            arena[type + SourceInitializerContext] == state[WarpLogicalMachineLayout.OwnerContextOffset] ? 0 :
            Fail(arena, WarpPortableHeapLayout.InvalidOperation, worker, arena[type + SourceInitializerContext]);
    }

    private static uint CopyInitializerFailureReference(uint[] arena, uint source, uint destination)
    {
        for (uint word = 0; word < 3; word++) { arena[destination + word] = arena[source + word]; }
        return 0;
    }
}
