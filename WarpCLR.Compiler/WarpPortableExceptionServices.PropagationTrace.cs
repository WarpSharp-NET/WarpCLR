namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    // The complete throw context is immutable. A catch/escape publishes a
    // separate propagation window; neither operation removes census roots.
    private static uint ProjectManagedTrace(uint[] arena, uint descriptor, uint record, uint physical, uint activation)
    {
        uint payload = TracePayload(arena, descriptor, record);
        if (payload == 0) { return WarpPortableExceptionLayout.InvalidReference; }
        uint count = arena[payload + WarpPortableExceptionTraceLayout.RawCount];
        uint first = 0;
        if (physical != 0)
        {
            first = count;
            for (uint frame = 0; frame < count; frame++)
            {
                uint source = payload + arena[payload + WarpPortableExceptionTraceLayout.RawStart] + frame * WarpPortableExceptionTraceLayout.FrameWords;
                if (arena[source + WarpPortableExceptionTraceLayout.Physical] == physical &&
                    arena[source + WarpPortableExceptionTraceLayout.Activation] == activation) { first = frame; break; }
            }
            if (first == count) { return WarpPortableExceptionLayout.InvalidContinuation; }
        }
        else if (activation != 0) { return WarpPortableExceptionLayout.InvalidContinuation; }
        CopyPropagationFrames(arena, payload, first, count);
        arena[payload + WarpPortableExceptionTraceLayout.FirstRawFrame] = first;
        arena[payload + WarpPortableExceptionTraceLayout.Count] = count - first;
        arena[payload + WarpPortableExceptionTraceLayout.ProjectionState] = WarpPortableExceptionTraceLayout.Projected;
        arena[record + WarpPortableExceptionLayout.LogicalTraceCount] = count - first;
        return 0;
    }

    private static uint CopyPropagationFrames(uint[] arena, uint payload, uint first, uint count)
    {
        uint capacity = arena[payload + WarpPortableExceptionTraceLayout.Capacity];
        for (uint frame = 0; frame < capacity; frame++)
        {
            uint destination = payload + arena[payload + WarpPortableExceptionTraceLayout.ManagedStart] + frame * WarpPortableExceptionTraceLayout.FrameWords;
            for (uint word = 0; word < WarpPortableExceptionTraceLayout.FrameWords; word++)
            {
                arena[destination + word] = frame < count - first ? arena[payload + arena[payload + WarpPortableExceptionTraceLayout.RawStart] +
                    (frame + first) * WarpPortableExceptionTraceLayout.FrameWords + word] : 0;
            }
        }
        return 0;
    }

    private static uint TracePayload(uint[] arena, uint descriptor, uint record)
    {
        uint owner = record + WarpPortableExceptionLayout.TraceReference;
        uint trace = RequireObject(arena, arena[owner], arena[owner + 1], arena[owner + 2]);
        if (trace == 0 || arena[trace + WarpPortableHeapLayout.SlotType] != arena[descriptor + WarpPortableExceptionLayout.TraceArrayType] ||
            arena[trace + WarpPortableHeapLayout.SlotKind] != WarpPortableHeapLayout.ValueArray ||
            arena[trace + WarpPortableHeapLayout.SlotLength] < WarpPortableExceptionTraceLayout.HeaderWords ||
            arena[trace + WarpPortableHeapLayout.SlotPayloadWords] < arena[trace + WarpPortableHeapLayout.SlotLength]) { return 0; }
        uint payload = arena[trace + WarpPortableHeapLayout.SlotPayload];
        uint capacity = arena[payload + WarpPortableExceptionTraceLayout.Capacity];
        if (capacity != arena[descriptor + WarpPortableExceptionLayout.MaximumFrames] ||
            Fits(capacity, WarpPortableExceptionTraceLayout.FrameWords * 2,
                arena[trace + WarpPortableHeapLayout.SlotLength] - WarpPortableExceptionTraceLayout.HeaderWords) == 0 ||
            arena[payload + WarpPortableExceptionTraceLayout.FormatVersion] != WarpPortableExceptionTraceLayout.Version ||
            arena[payload + WarpPortableExceptionTraceLayout.Identity] != arena[record + WarpPortableExceptionLayout.TraceIdentity] ||
            arena[payload + WarpPortableExceptionTraceLayout.RawStart] != WarpPortableExceptionTraceLayout.HeaderWords ||
            arena[payload + WarpPortableExceptionTraceLayout.ManagedStart] != WarpPortableExceptionTraceLayout.HeaderWords + capacity * WarpPortableExceptionTraceLayout.FrameWords ||
            arena[payload + WarpPortableExceptionTraceLayout.RawCount] == 0 || arena[payload + WarpPortableExceptionTraceLayout.RawCount] > capacity ||
            arena[payload + WarpPortableExceptionTraceLayout.Count] > arena[payload + WarpPortableExceptionTraceLayout.RawCount]) { return 0; }
        return payload;
    }
}
