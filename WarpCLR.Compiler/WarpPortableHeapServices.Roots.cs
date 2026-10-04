namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint AcquireRoot(uint[] arena, uint context, uint slot, uint generation, uint kind, uint offset, uint words, uint typeId)
    {
        if (Begin(arena, 16) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireReference(arena, context, slot, generation, 1) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (kind != WarpPortableHeapLayout.StrongRoot && kind != WarpPortableHeapLayout.InteriorRoot)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, kind, 0);
        }
        if (kind == WarpPortableHeapLayout.InteriorRoot &&
            RequireInterior(arena, context, slot, generation, offset, words, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint root = 1;
        while (root <= arena[WarpPortableHeapLayout.RootCount] && arena[Root(arena, root) + WarpPortableHeapLayout.RootState] != WarpPortableHeapLayout.Free)
        {
            root++;
        }
        if (root > arena[WarpPortableHeapLayout.RootCount])
        {
            return Fail(arena, WarpPortableHeapLayout.Quota, arena[WarpPortableHeapLayout.RootCount], 0);
        }
        uint entry = Root(arena, root);
        arena[entry + WarpPortableHeapLayout.RootState] = WarpPortableHeapLayout.Allocated;
        arena[entry + WarpPortableHeapLayout.RootReference] = context;
        arena[entry + WarpPortableHeapLayout.RootReference + 1] = slot;
        arena[entry + WarpPortableHeapLayout.RootReference + 2] = generation;
        arena[entry + WarpPortableHeapLayout.RootInteriorOffset] = offset;
        arena[entry + WarpPortableHeapLayout.RootInteriorWords] = words;
        arena[entry + WarpPortableHeapLayout.RootInteriorType] = typeId;
        arena[entry + WarpPortableHeapLayout.RootKind] = kind;
        arena[WarpPortableHeapLayout.LiveRoots]++;
        arena[WarpPortableHeapLayout.Result] = root;
        arena[WarpPortableHeapLayout.Result + 1] = arena[entry + WarpPortableHeapLayout.RootGeneration];
        return 0;
    }

    public static uint ReleaseRoot(uint[] arena, uint root, uint generation)
    {
        if (Begin(arena, 17) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireRoot(arena, root, generation) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint entry = Root(arena, root);
        arena[entry + WarpPortableHeapLayout.RootState] = generation == 0xFFFFFFFFu ? WarpPortableHeapLayout.Retired : WarpPortableHeapLayout.Free;
        if (generation != 0xFFFFFFFFu)
        {
            arena[entry + WarpPortableHeapLayout.RootGeneration]++;
        }
        arena[WarpPortableHeapLayout.LiveRoots]--;
        return 0;
    }

    public static uint ReadRoot(uint[] arena, uint root, uint generation)
    {
        if (Begin(arena, 18) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireRoot(arena, root, generation) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint entry = Root(arena, root);
        for (uint word = 0; word < 6; word++)
        {
            arena[WarpPortableHeapLayout.Result + word] = arena[entry + WarpPortableHeapLayout.RootReference + word];
        }
        if (arena[entry + WarpPortableHeapLayout.RootReference + 1] != 0 && arena[WarpPortableHeapLayout.LeaseState] != 0)
        {
            arena[WarpPortableHeapLayout.PendingResult] = 1;
        }
        return 0;
    }

    private static uint RequireRoot(uint[] arena, uint root, uint generation)
    {
        if (root == 0 || root > arena[WarpPortableHeapLayout.RootCount])
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidReference, root, generation);
        }
        uint entry = Root(arena, root);
        return arena[entry + WarpPortableHeapLayout.RootState] == WarpPortableHeapLayout.Allocated &&
            arena[entry + WarpPortableHeapLayout.RootGeneration] == generation ? 0 :
            Fail(arena, WarpPortableHeapLayout.InvalidReference, root, generation);
    }
}
