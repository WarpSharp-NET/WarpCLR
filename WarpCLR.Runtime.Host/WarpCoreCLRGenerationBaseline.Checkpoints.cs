using WarpCLR.Compiler;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRGenerationBaseline
{
    private uint previousDispatch;
    private uint previousEpoch;
    private uint previousReady;
    private uint previousAdvancedBarriers;
    private uint previousCollectionPhase;

    internal void ValidateIntermediate(uint[] arena)
    {
        if (!structure.AsSpan().SequenceEqual(StructuralIdentity(arena, Scheduler)) ||
            arena[Scheduler + WarpPortableSchedulerLayout.ControllerOwner] != Controller)
        { throw new InvalidDataException("A controller checkpoint changed its immutable arena domain or captured grant."); }
        uint dispatch = arena[Scheduler + WarpPortableSchedulerLayout.DispatchGeneration];
        uint epoch = arena[Scheduler + WarpPortableSchedulerLayout.GCEpoch];
        if (dispatch < previousDispatch || epoch < previousEpoch)
        { throw new InvalidDataException("A controller checkpoint rolled back a committed generation."); }
        if (string.Equals(Operation, WarpCoreCLRRecoveryCatalog.RequestCollection, StringComparison.Ordinal))
        { ValidateCollectionCheckpoint(arena, dispatch, epoch); }
        else { ValidateDispatchCheckpoint(arena, dispatch, epoch); }
        previousDispatch = dispatch; previousEpoch = epoch;
    }

    private void ValidateCollectionCheckpoint(uint[] arena, uint dispatch, uint epoch)
    {
        uint expected = header[WarpPortableSchedulerLayout.GCState] == WarpPortableSchedulerLayout.GCRequested ? Collection : checked(Collection + 1);
        if (dispatch != Dispatch || epoch != Collection && epoch != expected ||
            arena[Scheduler + WarpPortableSchedulerLayout.GCState] > WarpPortableSchedulerLayout.GCRequested)
        { throw new InvalidDataException("The collection checkpoint is outside the fixed catalog generation phases."); }
        uint phase = CollectionMutationPhase(arena, epoch, expected);
        if (phase < previousCollectionPhase) { throw new InvalidDataException("A collection checkpoint rolled back its committed mutation phase."); }
        previousCollectionPhase = phase;
    }

    private void ValidateDispatchCheckpoint(uint[] arena, uint dispatch, uint epoch)
    {
        if (epoch != Collection || dispatch != Dispatch && dispatch != checked(Dispatch + 1) ||
            arena[Scheduler + WarpPortableSchedulerLayout.RunningCount] != 0 ||
            arena[Scheduler + WarpPortableSchedulerLayout.GCState] != WarpPortableSchedulerLayout.GCIdle)
        { throw new InvalidDataException("The redispatch checkpoint changed a disallowed generation or active resident."); }
        uint ready = 0;
        for (uint worker = 0; worker < header[WarpPortableSchedulerLayout.WorkerCount]; worker++)
        {
            uint entry = Scheduler + header[WarpPortableSchedulerLayout.WorkerStart] + worker * WarpPortableSchedulerLayout.WorkerWords;
            uint state = arena[entry + WarpPortableSchedulerLayout.WorkerState];
            if (state == WarpPortableSchedulerLayout.Ready) { ready++; }
            else if (state < WarpPortableSchedulerLayout.Completed || state > WarpPortableSchedulerLayout.Disposed)
            { throw new InvalidDataException("A redispatch checkpoint introduced an unrelated worker state."); }
        }
        uint advanced = ValidatePartialBarriers(arena);
        uint context = arena[Scheduler + WarpPortableSchedulerLayout.ContextState];
        uint terminal = arena[Scheduler + WarpPortableSchedulerLayout.TerminalCount];
        if (ready < previousReady || advanced < previousAdvancedBarriers || ready != 0 && advanced != initialBarrierGenerations.Length ||
            dispatch != Dispatch && ready != header[WarpPortableSchedulerLayout.WorkerCount] ||
            context != WarpPortableSchedulerLayout.CompletedContext && context != WarpPortableSchedulerLayout.Active ||
            context == WarpPortableSchedulerLayout.Active && dispatch == Dispatch ||
            terminal != header[WarpPortableSchedulerLayout.WorkerCount] && terminal != 0 ||
            terminal == 0 && context != WarpPortableSchedulerLayout.Active)
        { throw new InvalidDataException("A redispatch checkpoint rolled back a partial reset."); }
        previousReady = ready; previousAdvancedBarriers = advanced;
    }

    private uint CollectionMutationPhase(uint[] arena, uint epoch, uint expected)
    {
        uint state = arena[Scheduler + WarpPortableSchedulerLayout.GCState];
        bool requested = header[WarpPortableSchedulerLayout.GCState] == WarpPortableSchedulerLayout.GCRequested;
        uint phase = state == WarpPortableSchedulerLayout.GCRequested ? epoch == expected ? 4u : 3u : 0u;
        if (state == WarpPortableSchedulerLayout.GCIdle && epoch != Collection || requested && state != WarpPortableSchedulerLayout.GCRequested)
        { throw new InvalidDataException("The scheduler collection mutation order changed."); }
        if (header[WarpPortableSchedulerLayout.HeapLinked] == 0) { return phase; }
        uint heapEpoch = arena[WarpPortableHeapLayout.CollectionEpoch];
        uint heapState = arena[WarpPortableHeapLayout.CollectionState];
        if (heapEpoch != Collection && heapEpoch != expected || heapState > WarpPortableHeapLayout.Requested ||
            requested && (heapEpoch != Collection || heapState != WarpPortableHeapLayout.Requested) ||
            !requested && heapEpoch == Collection && (heapState != WarpPortableHeapLayout.Idle || state != WarpPortableSchedulerLayout.GCIdle) ||
            heapState == WarpPortableHeapLayout.Idle && state != WarpPortableSchedulerLayout.GCIdle)
        { throw new InvalidDataException("The linked heap collection mutation order changed."); }
        if (requested || phase >= 3) { return phase; }
        return heapEpoch == Collection ? 0u : heapState == WarpPortableHeapLayout.Idle ? 1u : 2u;
    }

    private uint ValidatePartialBarriers(uint[] arena)
    {
        uint[] current = ReadBarrierGenerations(arena);
        uint advanced = 0;
        bool pending = false;
        for (int index = 0; index < current.Length; index++)
        {
            if (current[index] == initialBarrierGenerations[index]) { pending = true; }
            else if (pending || current[index] != checked(initialBarrierGenerations[index] + 1))
            { throw new InvalidDataException("A barrier checkpoint changed the fixed adjacent reset order."); }
            else { advanced++; }
        }
        return advanced;
    }

    private uint[] ReadBarrierGenerations(uint[] arena)
    {
        uint count = header[WarpPortableSchedulerLayout.BarrierCount];
        uint start = checked(Scheduler + header[WarpPortableSchedulerLayout.BarrierStart]);
        uint[] values = new uint[checked((int)count)];
        for (uint index = 0; index < count; index++) { values[index] = arena[start + index * WarpPortableSchedulerLayout.BarrierWords]; }
        return values;
    }
}
