namespace WarpCLR.Compiler;

internal static partial class WarpPortableSchedulerServices
{
    // Read-only admission probe: it must not clear scheduler/heap result scratch during another lease.
    public static uint ValidateAtomicGrant(uint[] arena, uint scheduler, uint controller, uint dispatch, uint epoch)
    {
        uint length = (uint)arena.Length;
        if (scheduler > length || length - scheduler < WarpPortableSchedulerLayout.HeaderWords)
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        if (controller == 0 || arena[scheduler + WarpPortableSchedulerLayout.ControllerOwner] != controller)
        {
            return WarpPortableSchedulerLayout.Yield;
        }
        uint words = arena[scheduler + WarpPortableSchedulerLayout.DescriptorWords];
        if (arena[scheduler] != WarpPortableSchedulerLayout.Magic || arena[scheduler + 1] != WarpPortableSchedulerLayout.Version ||
            arena[scheduler + WarpPortableSchedulerLayout.ArenaWords] != length ||
            words < WarpPortableSchedulerLayout.HeaderWords || words > length - scheduler ||
            ValidateSchemaTables(arena, scheduler) == 0 || ValidateHeapBinding(arena, scheduler) == 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration] != dispatch || dispatch == 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.GCEpoch] != epoch)
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Active ||
            arena[scheduler + WarpPortableSchedulerLayout.CancelRequested] != 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.DisposeRequested] != 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.GCState] == WarpPortableSchedulerLayout.GCCollecting ||
            arena[scheduler + WarpPortableSchedulerLayout.ServicePending] != 0)
        {
            return WarpPortableSchedulerLayout.Yield;
        }
        return 0;
    }
}
