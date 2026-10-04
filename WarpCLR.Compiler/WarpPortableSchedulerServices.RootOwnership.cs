namespace WarpCLR.Compiler;

internal static partial class WarpPortableSchedulerServices
{
    private static uint ValidateReservedRootOwnership(uint[] arena, uint scheduler)
    {
        uint count = arena[scheduler + WarpPortableSchedulerLayout.WorkerCount] * arena[scheduler + WarpPortableSchedulerLayout.RootSlotsPerWorker];
        uint start = arena[WarpPortableHeapLayout.RootStart];
        for (uint root = 0; root < count; root++)
        {
            uint entry = start + root * WarpPortableHeapLayout.RootWords;
            if (arena[entry + WarpPortableHeapLayout.RootState] != WarpPortableHeapLayout.Allocated ||
                arena[entry + WarpPortableHeapLayout.RootOwnership] != WarpPortableHeapLayout.RuntimeOwnedRoot ||
                arena[entry + WarpPortableHeapLayout.RootKind] != WarpPortableHeapLayout.StrongRoot)
            {
                return 0;
            }
        }
        return 1;
    }
}
