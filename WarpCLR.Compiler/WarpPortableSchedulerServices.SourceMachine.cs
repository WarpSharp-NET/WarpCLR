namespace WarpCLR.Compiler;

internal static partial class WarpPortableSchedulerServices
{
    public static uint RecordSourceMachineFault(uint[] arena, uint scheduler, uint controller, uint worker, uint generation,
        uint sourceKind, uint function, uint cilOffset, uint instruction, uint depth)
    {
        uint status = Begin(arena, scheduler, controller, 30);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0 || sourceKind == 0 ||
            depth > arena[Worker(arena, scheduler, worker) + WarpPortableSchedulerLayout.UserStackLimit])
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        uint kind = sourceKind == 1 ? WarpPortableSchedulerLayout.StepsExhausted :
            sourceKind == 2 ? WarpPortableSchedulerLayout.StackExhausted : WarpPortableSchedulerLayout.RuntimeInvariantFault;
        StoreFault(arena, scheduler, worker, kind, 0, function, cilOffset, instruction, depth, 0, 0, 0);
        return QuarantineFault(arena, scheduler, worker);
    }
}
