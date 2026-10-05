namespace WarpCLR.Compiler;

internal static partial class WarpPortableHostPublicationServices
{
    public static uint MirrorSourceState(uint[] arena, uint scheduler, uint controller, uint worker, uint generation,
        uint remainingLow, uint remainingHigh, uint spentLow, uint spentHigh, uint depth, uint function, uint cilOffset, uint instruction)
    {
        uint status = WarpPortableSchedulerServices.SetUserLocation(arena, scheduler, controller, worker, generation,
            function, cilOffset, instruction);
        if (status != 0)
        {
            return status;
        }
        uint entry = scheduler + arena[scheduler + WarpPortableSchedulerLayout.WorkerStart] +
            worker * WarpPortableSchedulerLayout.WorkerWords;
        uint priorLow = arena[entry + WarpPortableSchedulerLayout.StepRemainingLow];
        uint priorHigh = arena[entry + WarpPortableSchedulerLayout.StepRemainingHigh];
        uint priorSpentLow = arena[entry + WarpPortableSchedulerLayout.StepSpentLow];
        uint priorSpentHigh = arena[entry + WarpPortableSchedulerLayout.StepSpentHigh];
        uint sumLow = remainingLow + spentLow;
        uint sumHigh = remainingHigh + spentHigh;
        uint carry = sumLow < remainingLow ? 1u : 0u;
        if (sumHigh < remainingHigh || (sumHigh == 0xFFFFFFFFu && carry != 0) ||
            remainingHigh > priorHigh || (remainingHigh == priorHigh && remainingLow > priorLow) ||
            spentHigh < priorSpentHigh || (spentHigh == priorSpentHigh && spentLow < priorSpentLow) ||
            sumLow != arena[entry + WarpPortableSchedulerLayout.InitialStepLow] ||
            sumHigh + carry != arena[entry + WarpPortableSchedulerLayout.InitialStepHigh] ||
            depth > arena[entry + WarpPortableSchedulerLayout.UserStackLimit])
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        arena[entry + WarpPortableSchedulerLayout.StepRemainingLow] = remainingLow;
        arena[entry + WarpPortableSchedulerLayout.StepRemainingHigh] = remainingHigh;
        arena[entry + WarpPortableSchedulerLayout.StepSpentLow] = spentLow;
        arena[entry + WarpPortableSchedulerLayout.StepSpentHigh] = spentHigh;
        arena[entry + WarpPortableSchedulerLayout.UserStackDepth] = depth;
        return 0;
    }
}
