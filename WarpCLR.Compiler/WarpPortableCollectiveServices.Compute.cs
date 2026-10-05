namespace WarpCLR.Compiler;

internal static partial class WarpPortableCollectiveServices
{
    public static uint ComputeNode(uint[] arena, uint descriptor, uint worker, uint planGeneration, uint jobGeneration)
    {
        if (CurrentJob(arena, descriptor, worker, planGeneration, jobGeneration) != 0) { return 2; }
        uint entry = arena[descriptor + 18] + worker * 8;
        uint node = arena[entry + 1];
        uint state = arena[descriptor + 13] + node * 12;
        if (arena[state] != 1) { return 2; }
        uint row = arena[descriptor + 12] + node * 8;
        uint kind = arena[row];
        if (kind == 1)
        {
            uint index = arena[row + 3];
            if (index >= arena[descriptor + 6]) { return 2; }
            uint input = arena[descriptor + 19] + index * 2;
            uint type = arena[descriptor + 8];
            if ((type == 1 || type == 2 || type == 5) && arena[input + 1] != 0) { return 2; }
            arena[state + 1] = arena[input];
            arena[state + 2] = arena[input + 1];
            arena[state + 3] = 0;
            arena[state + 4] = 0;
            arena[state + 8] = 0xFFFFFFFFu;
        }
        else
        {
            if (ComputeCombination(arena, descriptor, row, state, node, kind) != 0) { return 2; }
        }
        arena[state] = 2;
        return 0;
    }

    private static uint ComputeCombination(uint[] arena, uint descriptor, uint row, uint state, uint node, uint kind)
    {
        uint left = arena[row + 1];
        uint right = arena[row + 2];
        if (kind < 2 || kind > 4 || left >= arena[descriptor + 3] || right >= arena[descriptor + 3]) { return 2; }
        uint first = arena[descriptor + 13] + left * 12;
        uint second = arena[descriptor + 13] + right * 12;
        if (arena[first] != 2 || arena[first + 5] != arena[descriptor + 23] ||
            (kind != 3 && (arena[second] != 2 || arena[second + 5] != arena[descriptor + 23]))) { return 2; }
        if (kind == 3)
        {
            CopyValueAndFault(arena, first, state);
            arena[state + 8] = arena[first + 3] != 0 ? arena[row + 3] : 0xFFFFFFFFu;
            return 0;
        }
        if (arena[first + 3] != 0 || arena[second + 3] != 0)
        {
            CopyValueAndFault(arena, arena[first + 3] != 0 ? first : second, state);
            arena[state + 1] = 0;
            arena[state + 2] = 0;
            return 0;
        }
        arena[state + 3] = 0;
        arena[state + 4] = 0;
        arena[state + 8] = 0xFFFFFFFFu;
        if (kind == 4)
        {
            arena[state + 1] = 0;
            arena[state + 2] = 0;
            return 0;
        }
        ApplyArithmetic(arena, descriptor, first, second, state, node);
        return 0;
    }

    private static uint ApplyArithmetic(uint[] arena, uint descriptor, uint first, uint second, uint state, uint node)
    {
        uint type = arena[descriptor + 8];
        uint operation = arena[descriptor + 9];
        uint fault = WarpPortableCollectiveArithmetic.ApplyFault(type, operation, arena[descriptor + 10],
            arena[first + 1], arena[first + 2], arena[second + 1], arena[second + 2]);
        arena[state + 3] = fault;
        if (fault != 0)
        {
            arena[state + 1] = 0;
            arena[state + 2] = 0;
            arena[state + 4] = node;
            return 0;
        }
        arena[state + 1] = WarpPortableCollectiveArithmetic.ApplyLow(type, operation,
            arena[first + 1], arena[first + 2], arena[second + 1], arena[second + 2]);
        arena[state + 2] = WarpPortableCollectiveArithmetic.ApplyHigh(type, operation,
            arena[first + 1], arena[first + 2], arena[second + 1], arena[second + 2]);
        return 0;
    }

    private static uint CopyValueAndFault(uint[] arena, uint source, uint destination)
    {
        arena[destination + 1] = arena[source + 1];
        arena[destination + 2] = arena[source + 2];
        arena[destination + 3] = arena[source + 3];
        arena[destination + 4] = arena[source + 4];
        arena[destination + 8] = arena[source + 8];
        return 0;
    }
}
