namespace WarpCLR.IR;

public sealed partial class WarpLogicalMachineLayout
{
    internal const uint NeedsPrivateHelper = 3;
    internal const uint AcknowledgedPrivateHelper = 4;

    internal bool HasPrivateHelperBoundaries => Kernel.Execution?.PrivateControllerProjection?.RequiresHelperBoundaries == true;

    internal bool IsPrivateHelperBoundary(WarpLogicalMachineNode node) => node.StartsBlock && node.SourceCost == 0 &&
        Kernel.Execution?.PrivateControllerProjection?.IsHelperBoundary(node.Function, node.Block) == true;

    // This changes consistency words only. It is not an invocation permit or a
    // controller grant; ordinary private-program invocation remains denied.
    internal void AcknowledgePrivateHelperBoundary(Span<uint> state)
    {
        if (!HasValidRuntimeHeader(state) || state[SourceBoundaryModeOffset] != 1 ||
            state[SourceBoundaryStateOffset] != NeedsPrivateHelper)
        {
            throw new InvalidOperationException("Only the exact published private-helper bridge phase may be acknowledged.");
        }
        state[SourceBoundaryStateOffset] = AcknowledgedPrivateHelper;
    }

    // Pure consistency preflight; it does not authenticate the supplied word.
    internal void RequireAcknowledgedPrivateHelperInvocation(ReadOnlySpan<uint> state, uint controller)
    {
        if (controller == 0 || !HasValidRuntimeHeader(state) || state[SourceBoundaryStateOffset] != AcknowledgedPrivateHelper)
        { throw new InvalidOperationException("An exact acknowledged private bridge requires a fresh nonzero runtime word."); }
    }

    private bool HasValidPrivateHelperFrame(ReadOnlySpan<uint> state, uint depth, uint pc)
    {
        if (!HasPrivateHelperBoundaries || state[StatusOffset] != Runnable || state[SourceBoundaryModeOffset] != 1 ||
            pc >= Nodes.Count || !IsPrivateHelperBoundary(Nodes[(int)pc])) { return false; }
        int frame = checked(HeaderWords + (int)(depth - 1) * FrameWords);
        uint parent = depth == 1 ? 0 : state[frame - FrameWords + FrameActivationOffset];
        int function = Nodes[(int)pc].Function;
        return state[frame + FrameFunctionOffset] == function && HasValidFrameIdentity(state, frame, function, parent);
    }
}
