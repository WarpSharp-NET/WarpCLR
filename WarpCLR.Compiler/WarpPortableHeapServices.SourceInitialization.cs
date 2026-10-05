using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    internal const string SourceInitializationSemantics = "warp.source-type-initialization/original-state-owner-context-logical-worker-no-abandoned-reset-reentry-no-controller-grant/0.1";
    internal const uint SourceInitializerContext = 15;

    public static uint BeginSourceTypeInitialization(uint[] state, uint[] arena, uint typeId, uint worker)
    {
        if (Begin(arena, 71) != 0 || RequireSourceInitializerState(state, arena, worker) != 0 || RequireType(arena, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint type = Type(arena, typeId), initialized = arena[type + WarpPortableHeapLayout.InitializerState];
        if (initialized == 0)
        {
            arena[type + WarpPortableHeapLayout.InitializerState] = 1;
            arena[type + WarpPortableHeapLayout.InitializerOwner] = worker;
            arena[type + SourceInitializerContext] = state[WarpLogicalMachineLayout.OwnerContextOffset];
            arena[WarpPortableHeapLayout.Result] = 1;
            return 0;
        }
        if (initialized == 1)
        {
            if (arena[type + WarpPortableHeapLayout.InitializerOwner] != worker)
            {
                return Fail(arena, WarpPortableHeapLayout.Busy, typeId, arena[type + WarpPortableHeapLayout.InitializerOwner]);
            }
            if (arena[type + SourceInitializerContext] == 0 ||
                arena[type + SourceInitializerContext] != state[WarpLogicalMachineLayout.OwnerContextOffset])
            {
                // A reset or abandoned source run never inherits reentrant authority.
                return Fail(arena, WarpPortableHeapLayout.InvalidOperation, typeId, arena[type + SourceInitializerContext]);
            }
            arena[WarpPortableHeapLayout.Result] = 2;
            return 0;
        }
        if (initialized == 2) { arena[WarpPortableHeapLayout.Result] = 3; return 0; }
        // Actual cached failure needs the operation-specific wrapper/EH factory path.
        return Fail(arena, WarpPortableHeapLayout.TypeInitializationFailed, typeId, initialized);
    }

    public static uint CompleteSourceTypeInitialization(uint[] state, uint[] arena, uint typeId, uint worker)
    {
        if (Begin(arena, 72) != 0 || RequireSourceInitializerState(state, arena, worker) != 0 || RequireType(arena, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint type = Type(arena, typeId);
        if (arena[type + WarpPortableHeapLayout.InitializerState] != 1 ||
            arena[type + WarpPortableHeapLayout.InitializerOwner] != worker ||
            arena[type + SourceInitializerContext] != state[WarpLogicalMachineLayout.OwnerContextOffset])
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, typeId, worker);
        }
        arena[type + WarpPortableHeapLayout.InitializerState] = 2;
        return 0;
    }

    private static uint RequireSourceInitializerState(uint[] state, uint[] arena, uint worker)
    {
        if ((uint)state.Length < (uint)WarpLogicalMachineLayout.HeaderWords ||
            state[WarpLogicalMachineLayout.OwnerContextOffset] == 0 || state[WarpLogicalMachineLayout.DepthOffset] == 0 ||
            worker >= arena[WarpPortableHeapLayout.WorkerCount] || arena[WarpPortableHeapLayout.LeaseState] != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, worker, 0);
        }
        return 0;
    }
}
