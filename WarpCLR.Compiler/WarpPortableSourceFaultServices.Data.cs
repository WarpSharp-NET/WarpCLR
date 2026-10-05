namespace WarpCLR.Compiler;

internal static partial class WarpPortableSourceFaultServices
{
    private static uint ValidatePreparedData(uint[] arena, uint pool, uint record, uint row, uint index, uint stage)
    {
        uint type = arena[row + WarpPortableSourceFaultFactoryLayout.RowExceptionType], owner = record + WarpPortableSourceFaultFactoryLayout.PreparedReference;
        uint slot = Object(arena, arena[owner], arena[owner + 1], arena[owner + 2]);
        if (type == 0 || type > arena[WarpPortableHeapLayout.TypeCount] || slot == 0 ||
            arena[slot + WarpPortableHeapLayout.SlotType] != type || arena[slot + WarpPortableHeapLayout.SlotKind] != WarpPortableHeapLayout.Class ||
            arena[slot + WarpPortableHeapLayout.SlotPayloadWords] < WarpPortableSourceExceptionLayout.PrefixWords)
        { return WarpPortableExceptionLayout.InvalidReference; }
        uint root = arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRoot];
        if (root != arena[pool + WarpPortableSourceFaultFactoryLayout.RootFirst] + arena[pool + WarpPortableSourceFaultFactoryLayout.ResourceCount] + index ||
            ValidRoot(arena, root, arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRootGeneration]) == 0 || RootMatches(arena, root, owner) == 0)
        { return WarpPortableExceptionLayout.InvalidOwnership; }
        uint memory = arena[WarpPortableSourceMemoryLayout.Descriptor];
        uint data = arena[memory + WarpPortableSourceMemoryLayout.ExceptionTypeStart];
        uint typeData = data + (type - 1) * WarpPortableSourceExceptionLayout.ExceptionTypeWords;
        uint initialization = data + arena[WarpPortableHeapLayout.TypeCount] * WarpPortableSourceExceptionLayout.ExceptionTypeWords +
            (arena[owner + 1] - 1) * WarpPortableSourceExceptionLayout.DataStateWords;
        uint payload = arena[slot + WarpPortableHeapLayout.SlotPayload];
        if (arena[typeData + WarpPortableSourceExceptionLayout.ExceptionTypeKind] == 0 ||
            arena[typeData + WarpPortableSourceExceptionLayout.ExceptionTypeKind] > WarpPortableSourceExceptionKind.TypeInitialization ||
            arena[typeData + WarpPortableSourceExceptionLayout.ExceptionTypeHResult] == 0 ||
            arena[initialization + WarpPortableSourceExceptionLayout.DataGeneration] != arena[owner + 2] ||
            arena[initialization + WarpPortableSourceExceptionLayout.DataInitialized] != 1 ||
            arena[payload + WarpPortableSourceExceptionLayout.HResultWord] != arena[typeData + WarpPortableSourceExceptionLayout.ExceptionTypeHResult])
        { return WarpPortableExceptionLayout.InvalidReference; }
        uint fault = ValidateResource(arena, pool, arena[row + WarpPortableSourceFaultFactoryLayout.RowMessage], payload, 0);
        if (fault != 0) { return fault; }
        fault = ValidateResource(arena, pool, arena[row + WarpPortableSourceFaultFactoryLayout.RowParamName], payload + WarpPortableSourceExceptionLayout.ParamNameWord, 1);
        if (fault != 0) { return fault; }
        // Finite factories supply no inner/actual/type-name data. Before raise
        // all trace words are null; after raise the exact EH publication validates
        // those trace words separately. HResult/message/param remain immutable.
        for (uint word = 3; word < WarpPortableSourceExceptionLayout.HResultWord; word++)
        {
            if (word >= WarpPortableSourceExceptionLayout.ParamNameWord && word < WarpPortableSourceExceptionLayout.ActualValueWord ||
                stage == 2 && word >= WarpPortableSourceExceptionLayout.TraceReferenceWord && word < WarpPortableSourceExceptionLayout.ParamNameWord) { continue; }
            if (arena[payload + word] != 0) { return WarpPortableExceptionLayout.InvalidReference; }
        }
        return 0;
    }

    private static uint ValidateResource(uint[] arena, uint pool, uint id, uint expectedOwner, uint nullable)
    {
        if (id == 0)
        {
            return nullable != 0 && (arena[expectedOwner] | arena[expectedOwner + 1] | arena[expectedOwner + 2]) == 0 ? 0 : WarpPortableExceptionLayout.InvalidReference;
        }
        if (id > arena[pool + WarpPortableSourceFaultFactoryLayout.ResourceCount]) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        uint row = arena[pool + WarpPortableSourceFaultFactoryLayout.ResourceStart] + (id - 1) * WarpPortableSourceFaultFactoryLayout.ResourceWords;
        uint owner = row + WarpPortableSourceFaultFactoryLayout.ResourceReference, root = arena[row + WarpPortableSourceFaultFactoryLayout.ResourceRoot];
        uint slot = Object(arena, arena[owner], arena[owner + 1], arena[owner + 2]);
        if (arena[row + WarpPortableSourceFaultFactoryLayout.ResourceState] != WarpPortableSourceFaultFactoryLayout.Ready || slot == 0 ||
            arena[slot + WarpPortableHeapLayout.SlotType] != arena[pool + WarpPortableSourceFaultFactoryLayout.StringType] ||
            arena[slot + WarpPortableHeapLayout.SlotKind] != WarpPortableHeapLayout.String ||
            arena[owner] != arena[expectedOwner] || arena[owner + 1] != arena[expectedOwner + 1] || arena[owner + 2] != arena[expectedOwner + 2] ||
            root != arena[pool + WarpPortableSourceFaultFactoryLayout.RootFirst] + id - 1 ||
            ValidRoot(arena, root, arena[row + WarpPortableSourceFaultFactoryLayout.ResourceRootGeneration]) == 0 || RootMatches(arena, root, owner) == 0)
        { return WarpPortableExceptionLayout.InvalidOwnership; }
        uint length = arena[row + WarpPortableSourceFaultFactoryLayout.ResourceLength], text = arena[row + WarpPortableSourceFaultFactoryLayout.ResourceText];
        uint end = pool + arena[pool + WarpPortableSourceFaultFactoryLayout.DescriptorWords];
        uint payload = arena[slot + WarpPortableHeapLayout.SlotPayload];
        if (text < arena[pool + WarpPortableSourceFaultFactoryLayout.TextStart] || text > end || length > end - text ||
            arena[slot + WarpPortableHeapLayout.SlotLength] != length || (length >> 1) + (length & 1u) > arena[slot + WarpPortableHeapLayout.SlotPayloadWords])
        { return WarpPortableExceptionLayout.InvalidDescriptor; }
        for (uint character = 0; character < length; character++)
        {
            if (((arena[payload + (character >> 1)] >> (int)((character & 1u) * 16)) & 0xFFFFu) != arena[text + character])
            { return WarpPortableExceptionLayout.InvalidReference; }
        }
        return 0;
    }
}
