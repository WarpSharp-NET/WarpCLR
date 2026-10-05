namespace WarpCLR.Compiler;

internal static partial class WarpPortableCollectiveServices
{
    public static uint FinishRun(uint[] arena, uint descriptor, uint controller)
    {
        uint validation = Controller(arena, descriptor, controller);
        if (validation != 0) { return validation; }
        if (arena[descriptor + 24] != 1 || DispatchMatches(arena, descriptor) == 0) { return 2; }
        if (arena[descriptor + 27] != 0) { return 1; }
        if (arena[descriptor + 28] != 0) { return CancelRun(arena, descriptor, controller); }
        if (arena[descriptor + 25] != arena[descriptor + 5]) { return 1; }
        uint root = arena[descriptor + 13] + arena[descriptor + 40] * 12;
        if (arena[root] != 2 || arena[root + 5] != arena[descriptor + 23]) { return 2; }
        arena[descriptor + 29] = arena[root + 3];
        arena[descriptor + 30] = arena[root + 4];
        arena[descriptor + 31] = arena[root + 8];
        if (arena[root + 3] != 0)
        {
            arena[descriptor + 24] = 3;
            return 4;
        }
        arena[descriptor + 41] = arena[descriptor + 23];
        arena[descriptor + 24] = 2;
        return 3;
    }

    public static uint AcknowledgeResults(uint[] arena, uint descriptor, uint controller, uint planGeneration)
    {
        uint validation = Controller(arena, descriptor, controller);
        if (validation != 0) { return validation; }
        if (planGeneration == 0 || arena[descriptor + 24] != 2 || arena[descriptor + 41] != planGeneration ||
            arena[descriptor + 23] != planGeneration || DispatchMatches(arena, descriptor) == 0) { return 2; }
        arena[descriptor + 42] = planGeneration;
        return 0;
    }

    public static uint RequestCancellation(uint[] arena, uint descriptor, uint controller)
    {
        uint validation = Controller(arena, descriptor, controller);
        if (validation != 0) { return validation; }
        if (arena[descriptor + 24] != 1 || DispatchMatches(arena, descriptor) == 0) { return 2; }
        arena[descriptor + 28] = 1;
        return 0;
    }

    public static uint CancelRun(uint[] arena, uint descriptor, uint controller)
    {
        uint validation = Controller(arena, descriptor, controller);
        if (validation != 0) { return validation; }
        if (arena[descriptor + 24] != 1 || arena[descriptor + 28] == 0 || DispatchMatches(arena, descriptor) == 0) { return 2; }
        if (arena[descriptor + 27] != 0) { return 1; }
        arena[descriptor + 24] = 4;
        return 5;
    }

    // The parent must first stop this exact generated continuation. Cancellation intent
    // alone is insufficient: an active continuation may still own and write its row.
    public static uint AbandonStoppedNode(uint[] arena, uint descriptor, uint controller, uint worker,
        uint planGeneration, uint jobGeneration)
    {
        uint validation = Controller(arena, descriptor, controller);
        if (validation != 0) { return validation; }
        if (arena[descriptor + 28] == 0 || arena[descriptor + 27] == 0 ||
            CurrentJob(arena, descriptor, worker, planGeneration, jobGeneration) != 0) { return 2; }
        uint entry = arena[descriptor + 18] + worker * 8;
        uint state = arena[descriptor + 13] + arena[entry + 1] * 12;
        arena[state + 5] = 0;
        arena[state] = 0;
        arena[entry] = 0;
        arena[descriptor + 27]--;
        return 0;
    }
}
