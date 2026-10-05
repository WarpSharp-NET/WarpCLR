namespace WarpCLR.Compiler;

internal static partial class WarpPortableCollectiveServices
{
    private static uint Valid(uint[] arena, uint descriptor)
    {
        if ((uint)arena.Length < 64 || descriptor > (uint)arena.Length - 64) { return 2; }
        if (arena[descriptor] != WarpPortableCollectiveLayout.Magic || arena[descriptor + 1] != 1) { return 2; }
        uint words = arena[descriptor + 2];
        if (words < 64 || words > WarpPortableCollectiveLayout.MaximumScratchWords || words > (uint)arena.Length - descriptor) { return 2; }
        uint nodes = arena[descriptor + 3];
        uint workers = arena[descriptor + 4];
        uint levels = arena[descriptor + 5];
        uint count = arena[descriptor + 6];
        uint outputs = arena[descriptor + 7];
        if (nodes == 0 || nodes > WarpPortableCollectiveLayout.MaximumNodes || workers == 0 || workers > 65536 ||
            levels > 64 || count > 65536 || outputs > 65536) { return 2; }
        if (arena[descriptor + 8] < 1 || arena[descriptor + 8] > 6 || arena[descriptor + 9] < 1 || arena[descriptor + 9] > 4 ||
            arena[descriptor + 10] > 1 || arena[descriptor + 11] < 1 || arena[descriptor + 11] > 3) { return 2; }
        if (arena[descriptor + 10] != 0 && (arena[descriptor + 8] >= 5 || arena[descriptor + 9] == 2 || arena[descriptor + 9] == 3)) { return 2; }
        if (outputs != (arena[descriptor + 11] == 1 ? 1u : count)) { return 2; }
        if (arena[descriptor + 12] != descriptor + 64 || arena[descriptor + 13] != arena[descriptor + 12] + nodes * 8 ||
            arena[descriptor + 14] != arena[descriptor + 13] + nodes * 12 || arena[descriptor + 15] != arena[descriptor + 14] + nodes - 1 ||
            arena[descriptor + 16] != arena[descriptor + 15] + levels * 2 || arena[descriptor + 17] != arena[descriptor + 16] + outputs ||
            arena[descriptor + 18] != arena[descriptor + 17] + workers || arena[descriptor + 19] != arena[descriptor + 18] + workers * 8 ||
            descriptor + words != arena[descriptor + 19] + count * 2 || arena[descriptor + 43] != count * 2) { return 2; }
        uint scheduler = arena[descriptor + 21];
        if (descriptor < 64 || scheduler > descriptor - 64 || arena[descriptor + 20] != scheduler + 16 ||
            arena[descriptor + 40] >= nodes || arena[descriptor + 24] > 4) { return 2; }
        return ValidParentBinding(arena, descriptor, scheduler) != 0 ? 0u : 2u;
    }

    private static uint ValidParentBinding(uint[] arena, uint descriptor, uint scheduler)
    {
        if (arena[descriptor + 44] != 2 || arena[descriptor + 45] != 2 || arena[descriptor + 46] != 2 || arena[descriptor + 47] != 1 ||
            arena[scheduler] != 0x57525343 || arena[scheduler + 1] != 2 || arena[scheduler + 3] != (uint)arena.Length) { return 0; }
        uint linked = arena[scheduler + 42];
        if (linked == 0) { return 1; }
        return linked == 1 && scheduler >= 64 && arena[0] == 0x57524850 && arena[1] == 2 &&
            arena[54] == scheduler && arena[36] >= descriptor + arena[descriptor + 2] && arena[36] <= (uint)arena.Length &&
            arena[2] != 0 && arena[scheduler + 2] == arena[2] ? 1u : 0u;
    }

    private static uint Controller(uint[] arena, uint descriptor, uint controller)
    {
        if (Valid(arena, descriptor) != 0) { return 2; }
        return controller == 0 || arena[arena[descriptor + 20]] != controller ? 1u : 0u;
    }

    private static uint DispatchMatches(uint[] arena, uint descriptor) =>
        arena[descriptor + 22] != 0 && arena[arena[descriptor + 21] + 60] == arena[descriptor + 22] ? 1u : 0u;

    private static uint CurrentJob(uint[] arena, uint descriptor, uint worker, uint planGeneration, uint jobGeneration)
    {
        if (Valid(arena, descriptor) != 0 || arena[descriptor + 24] != 1 || DispatchMatches(arena, descriptor) == 0 ||
            worker >= arena[descriptor + 4] || planGeneration == 0 || jobGeneration == 0 || arena[descriptor + 23] != planGeneration) { return 2; }
        uint entry = arena[descriptor + 18] + worker * 8;
        if (arena[entry] != 1 || arena[entry + 2] != planGeneration || arena[entry + 3] != jobGeneration ||
            arena[entry + 4] != arena[descriptor + 22] || arena[entry + 6] != arena[descriptor + 25]) { return 2; }
        uint node = arena[entry + 1];
        if (node == 0 || node >= arena[descriptor + 3]) { return 2; }
        uint state = arena[descriptor + 13] + node * 12;
        if (arena[state + 5] != planGeneration || arena[state + 6] != worker || arena[state + 7] != jobGeneration) { return 2; }
        return 0;
    }
}
