namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint AllocateObject(uint[] arena, uint typeId)
    {
        if (Begin(arena, 1) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireType(arena, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (arena[Type(arena, typeId) + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.Class)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidType, typeId, 0);
        }
        return Allocate(arena, typeId, WarpPortableHeapLayout.Class, 0);
    }

    public static uint AllocateArray(uint[] arena, uint typeId, uint length)
    {
        if (Begin(arena, 2) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireType(arena, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint kind = arena[Type(arena, typeId) + WarpPortableHeapLayout.TypeKind];
        if (kind != WarpPortableHeapLayout.ReferenceArray && kind != WarpPortableHeapLayout.ValueArray && kind != WarpPortableHeapLayout.String)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidType, typeId, kind);
        }
        return Allocate(arena, typeId, kind, length);
    }

    public static uint GetType(uint[] arena, uint context, uint slot, uint generation)
    {
        if (Begin(arena, 3) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireReference(arena, context, slot, generation, 0) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        arena[WarpPortableHeapLayout.Result] = arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotType];
        return 0;
    }

    public static uint IsInstance(uint[] arena, uint context, uint slot, uint generation, uint typeId)
    {
        if (Begin(arena, 4) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireType(arena, typeId) != 0 || RequireReference(arena, context, slot, generation, 1) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        arena[WarpPortableHeapLayout.Result] = slot == 0 ? 0 : Assignable(arena, arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotType], typeId);
        return 0;
    }

    public static uint Cast(uint[] arena, uint context, uint slot, uint generation, uint typeId, uint failOnMismatch)
    {
        if (Begin(arena, 5) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireType(arena, typeId) != 0 || RequireReference(arena, context, slot, generation, 1) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (slot != 0 && Assignable(arena, arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotType], typeId) == 0)
        {
            return failOnMismatch == 0 ? 0 : Fail(arena, WarpPortableHeapLayout.InvalidCast, typeId, slot);
        }
        SetReferenceResult(arena, context, slot, generation);
        return 0;
    }

    private static uint Begin(uint[] arena, uint operation)
    {
        if (arena[WarpPortableHeapLayout.PendingResult] != 0 && operation != 16)
        {
            return Fail(arena, WarpPortableHeapLayout.Busy, arena[WarpPortableHeapLayout.LeaseOwner], arena[WarpPortableHeapLayout.LeaseEpoch]);
        }
        arena[WarpPortableHeapLayout.Fault] = 0;
        arena[WarpPortableHeapLayout.Operation] = operation;
        arena[WarpPortableHeapLayout.Argument0] = 0;
        arena[WarpPortableHeapLayout.Argument1] = 0;
        for (uint word = 0; word < 8; word++)
        {
            arena[WarpPortableHeapLayout.Result + word] = 0;
        }
        return 0;
    }

    private static uint Fail(uint[] arena, uint fault, uint argument0, uint argument1)
    {
        arena[WarpPortableHeapLayout.Fault] = fault;
        arena[WarpPortableHeapLayout.Argument0] = argument0;
        arena[WarpPortableHeapLayout.Argument1] = argument1;
        return fault;
    }

    private static uint Type(uint[] arena, uint typeId) => arena[WarpPortableHeapLayout.TypeStart] + (typeId - 1) * WarpPortableHeapLayout.TypeWords;

    private static uint Slot(uint[] arena, uint slot) => arena[WarpPortableHeapLayout.SlotStart] + (slot - 1) * WarpPortableHeapLayout.SlotWords;

    private static uint Root(uint[] arena, uint root) => arena[WarpPortableHeapLayout.RootStart] + (root - 1) * WarpPortableHeapLayout.RootWords;

    private static uint RequireType(uint[] arena, uint typeId) =>
        typeId == 0 || typeId > arena[WarpPortableHeapLayout.TypeCount] ? Fail(arena, WarpPortableHeapLayout.InvalidType, typeId, 0) : 0;

    private static uint RequireReference(uint[] arena, uint context, uint slot, uint generation, uint allowNull)
    {
        if ((context | slot | generation) == 0)
        {
            return allowNull != 0 ? 0 : Fail(arena, WarpPortableHeapLayout.NullReference, 0, 0);
        }
        if (context != arena[WarpPortableHeapLayout.Context])
        {
            return Fail(arena, WarpPortableHeapLayout.WrongContext, context, arena[WarpPortableHeapLayout.Context]);
        }
        if (slot == 0 || slot > arena[WarpPortableHeapLayout.SlotCount] || generation == 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidReference, slot, generation);
        }
        uint entry = Slot(arena, slot);
        return arena[entry + WarpPortableHeapLayout.SlotState] == WarpPortableHeapLayout.Allocated &&
            arena[entry + WarpPortableHeapLayout.SlotGeneration] == generation ? 0 :
            Fail(arena, WarpPortableHeapLayout.InvalidReference, slot, generation);
    }

    private static uint Assignable(uint[] arena, uint source, uint destination) =>
        arena[arena[WarpPortableHeapLayout.ClosureStart] + (source - 1) * arena[WarpPortableHeapLayout.TypeCount] + destination - 1];

    private static uint SetReferenceResult(uint[] arena, uint context, uint slot, uint generation)
    {
        arena[WarpPortableHeapLayout.Result] = context;
        arena[WarpPortableHeapLayout.Result + 1] = slot;
        arena[WarpPortableHeapLayout.Result + 2] = generation;
        if (slot != 0 && arena[WarpPortableHeapLayout.LeaseState] != 0)
        {
            arena[WarpPortableHeapLayout.PendingResult] = 1;
        }
        return 0;
    }
}
