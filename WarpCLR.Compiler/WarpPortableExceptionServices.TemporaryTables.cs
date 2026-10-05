namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    private static uint ValidateTemporarySlice(uint[] arena, uint descriptor, uint captured, uint site)
    {
        uint count = arena[site + WarpPortableExceptionLayout.SiteTemporaryCount];
        if (count == 0) { return 0; }
        uint start = arena[site + WarpPortableExceptionLayout.SiteTemporaryStart];
        uint first = arena[descriptor + WarpPortableExceptionLayout.TemporaryOwnerStart];
        uint available = arena[descriptor + WarpPortableExceptionLayout.TemporaryOwnerCount];
        if (arena[site + WarpPortableExceptionLayout.SiteOpCode] != 0x73 || start < first ||
            ((start - first) & 7) != 0 || (start - first) >> 3 > available ||
            count > available - ((start - first) >> 3))
        { return WarpPortableExceptionLayout.InvalidDescriptor; }
        for (uint field = 0; field < count; field++)
        {
            uint row = descriptor + start + field * WarpPortableExceptionLayout.TemporaryOwnerWords;
            if (ValidTemporaryOwner(arena, descriptor, captured, site, row) == 0) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        }
        return 0;
    }

    private static uint ValidTemporaryOwner(uint[] arena, uint descriptor, uint captured, uint site, uint owner)
    {
        uint offset = arena[owner + WarpPortableExceptionLayout.TemporaryPrivateOffset];
        uint start = arena[owner + WarpPortableExceptionLayout.TemporaryStart];
        uint span = arena[owner + WarpPortableExceptionLayout.TemporarySpan];
        uint relative = arena[owner + WarpPortableExceptionLayout.TemporaryRelativeByteOffset];
        uint words = arena[captured + WarpPortableExceptionLayout.FramePrivateWords];
        uint type = arena[owner + WarpPortableExceptionLayout.TemporaryType];
        uint body = LookupBody(arena, descriptor, arena[captured + WarpPortableExceptionLayout.FrameFunction]);
        return body != 0 && words == arena[body + WarpPortableExceptionLayout.BodyPrivateWords] &&
            arena[descriptor + WarpPortableExceptionLayout.PrivateOffset] <= arena[descriptor + WarpPortableExceptionLayout.StateStride] &&
            words <= arena[descriptor + WarpPortableExceptionLayout.StateStride] - arena[descriptor + WarpPortableExceptionLayout.PrivateOffset] &&
            start >= arena[body + WarpPortableExceptionLayout.BodyEvaluationOffset] && start <= words && span <= words - start &&
            offset >= start && offset - start <= span && span - (offset - start) >= 3 &&
            (relative & 3) == 0 && relative >> 2 == offset - start && type != 0 && type <= arena[WarpPortableHeapLayout.TypeCount] &&
            arena[owner + WarpPortableExceptionLayout.TemporaryFunction] == arena[captured + WarpPortableExceptionLayout.FrameFunction] &&
            arena[owner + WarpPortableExceptionLayout.TemporarySite] == arena[captured + WarpPortableExceptionLayout.FrameSite] &&
            arena[site + WarpPortableExceptionLayout.SiteFunction] == arena[owner + WarpPortableExceptionLayout.TemporaryFunction] ? 1u : 0u;
    }
}
