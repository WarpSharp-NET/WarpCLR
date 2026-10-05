namespace WarpCLR.Compiler;

internal static partial class WarpPortableCollectiveServices
{
    public static uint BeginRun(uint[] arena, uint descriptor, uint controller, uint dispatch)
    {
        uint validation = Controller(arena, descriptor, controller);
        if (validation != 0) { return validation; }
        if (dispatch == 0 || arena[arena[descriptor + 21] + 60] != dispatch) { return 2; }
        if (arena[descriptor + 24] == 1 || arena[descriptor + 27] != 0 ||
            arena[descriptor + 41] != arena[descriptor + 42]) { return 1; }
        if (arena[descriptor + 23] == 0xFFFFFFFFu) { return 6; }
        arena[descriptor + 23]++;
        arena[descriptor + 22] = dispatch;
        arena[descriptor + 24] = 1;
        arena[descriptor + 25] = 0;
        arena[descriptor + 26] = 0;
        arena[descriptor + 28] = 0;
        arena[descriptor + 29] = 0;
        arena[descriptor + 30] = 0;
        arena[descriptor + 31] = 0xFFFFFFFFu;
        uint identity = arena[descriptor + 13];
        arena[identity + 1] = WarpPortableCollectiveArithmetic.IdentityLow(arena[descriptor + 8], arena[descriptor + 9]);
        arena[identity + 2] = WarpPortableCollectiveArithmetic.IdentityHigh(arena[descriptor + 8], arena[descriptor + 9]);
        arena[identity + 3] = 0;
        arena[identity + 4] = 0;
        arena[identity + 5] = arena[descriptor + 23];
        arena[identity + 8] = 0xFFFFFFFFu;
        arena[identity] = 2;
        return 0;
    }

    public static uint TryAcquireNode(uint[] arena, uint descriptor, uint controller, uint worker, uint logicalWorker, uint runGeneration)
    {
        uint validation = Controller(arena, descriptor, controller);
        if (validation != 0) { return validation; }
        if (arena[descriptor + 24] != 1 || worker >= arena[descriptor + 4] || runGeneration == 0 ||
            arena[arena[descriptor + 17] + worker] != logicalWorker || DispatchMatches(arena, descriptor) == 0) { return 2; }
        if (arena[descriptor + 28] != 0) { return 1; }
        uint entry = arena[descriptor + 18] + worker * 8;
        if (arena[entry] != 0) { return 1; }
        uint level = arena[descriptor + 25];
        if (level >= arena[descriptor + 5]) { return 3; }
        uint phase = arena[descriptor + 15] + level * 2;
        uint next = arena[descriptor + 26];
        if (next >= arena[phase + 1]) { return 1; }
        if (arena[entry + 3] == 0xFFFFFFFFu) { return 6; }
        uint node = arena[arena[phase] + next];
        if (node == 0 || node >= arena[descriptor + 3] || arena[arena[descriptor + 12] + node * 8 + 5] != level) { return 2; }
        uint state = arena[descriptor + 13] + node * 12;
        if (arena[state + 5] == arena[descriptor + 23]) { return 2; }
        BindJob(arena, descriptor, entry, state, worker, node, runGeneration);
        arena[descriptor + 26]++;
        arena[descriptor + 27]++;
        return 0;
    }

    public static uint RebindWorkerRun(uint[] arena, uint descriptor, uint controller, uint worker,
        uint planGeneration, uint jobGeneration, uint runGeneration)
    {
        uint validation = Controller(arena, descriptor, controller);
        if (validation != 0) { return validation; }
        if (runGeneration == 0 || CurrentJob(arena, descriptor, worker, planGeneration, jobGeneration) != 0) { return 2; }
        arena[arena[descriptor + 18] + worker * 8 + 5] = runGeneration;
        return 0;
    }

    public static uint CompleteNode(uint[] arena, uint descriptor, uint controller, uint worker,
        uint planGeneration, uint jobGeneration, uint runGeneration)
    {
        uint validation = Controller(arena, descriptor, controller);
        if (validation != 0) { return validation; }
        if (CurrentJob(arena, descriptor, worker, planGeneration, jobGeneration) != 0) { return 2; }
        uint entry = arena[descriptor + 18] + worker * 8;
        uint state = arena[descriptor + 13] + arena[entry + 1] * 12;
        if (arena[entry + 5] != runGeneration || arena[state] != 2 || arena[descriptor + 27] == 0) { return 2; }
        arena[entry] = 0;
        arena[descriptor + 27]--;
        return 0;
    }

    public static uint AdvancePhase(uint[] arena, uint descriptor, uint controller)
    {
        uint validation = Controller(arena, descriptor, controller);
        if (validation != 0) { return validation; }
        if (arena[descriptor + 24] != 1 || DispatchMatches(arena, descriptor) == 0) { return 2; }
        if (arena[descriptor + 27] != 0) { return 1; }
        if (arena[descriptor + 28] != 0) { return CancelRun(arena, descriptor, controller); }
        uint level = arena[descriptor + 25];
        if (level >= arena[descriptor + 5] || arena[descriptor + 26] != arena[arena[descriptor + 15] + level * 2 + 1]) { return 2; }
        arena[descriptor + 25]++;
        arena[descriptor + 26] = 0;
        return 0;
    }

    private static uint BindJob(uint[] arena, uint descriptor, uint entry, uint state, uint worker, uint node, uint runGeneration)
    {
        arena[entry + 3]++;
        arena[entry + 1] = node;
        arena[entry + 2] = arena[descriptor + 23];
        arena[entry + 4] = arena[descriptor + 22];
        arena[entry + 5] = runGeneration;
        arena[entry + 6] = arena[descriptor + 25];
        arena[state + 5] = arena[descriptor + 23];
        arena[state + 6] = worker;
        arena[state + 7] = arena[entry + 3];
        arena[state] = 1;
        arena[entry] = 1;
        return 0;
    }
}
