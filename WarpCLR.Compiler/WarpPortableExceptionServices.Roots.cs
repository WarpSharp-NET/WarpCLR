namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    private static uint Root(uint[] arena, uint root) => arena[WarpPortableHeapLayout.RootStart] + (root - 1) * WarpPortableHeapLayout.RootWords;

    private static uint OwnedRoot(uint[] arena, uint descriptor, uint root)
    {
        uint first = arena[descriptor + WarpPortableExceptionLayout.RootFirst];
        return root >= first && root - first < arena[descriptor + WarpPortableExceptionLayout.RootCount] &&
            arena[Root(arena, root) + WarpPortableHeapLayout.RootOwnership] == WarpPortableHeapLayout.RuntimeOwnedRoot &&
            arena[Root(arena, root) + WarpPortableHeapLayout.RootState] == WarpPortableHeapLayout.Allocated ? 1u : 0u;
    }

    private static uint PublishRoot(uint[] arena, uint descriptor, uint root, uint context, uint slot, uint generation)
    {
        if (OwnedRoot(arena, descriptor, root) == 0 || ((context | slot | generation) != 0 && RequireObject(arena, context, slot, generation) == 0))
        {
            return WarpPortableExceptionLayout.InvalidOwnership;
        }
        uint row = Root(arena, root) + WarpPortableHeapLayout.RootReference;
        arena[row] = context; arena[row + 1] = slot; arena[row + 2] = generation;
        return 0;
    }

    private static uint ClearRecord(uint[] arena, uint descriptor, uint record)
    {
        uint root = arena[record + WarpPortableExceptionLayout.RecordRoot];
        if (OwnedRoot(arena, descriptor, root) == 0 || OwnedRoot(arena, descriptor, root + 1) == 0) { return WarpPortableExceptionLayout.InvalidOwnership; }
        PublishRoot(arena, descriptor, root, 0, 0, 0); PublishRoot(arena, descriptor, root + 1, 0, 0, 0);
        for (uint word = 0; word < WarpPortableExceptionLayout.RecordWords; word++) { arena[record + word] = 0; }
        arena[record + WarpPortableExceptionLayout.RecordRoot] = root;
        return 0;
    }

    private static uint CopyOwner(uint[] arena, uint destination, uint source)
    {
        arena[destination] = arena[source]; arena[destination + 1] = arena[source + 1]; arena[destination + 2] = arena[source + 2];
        return 0;
    }
}
