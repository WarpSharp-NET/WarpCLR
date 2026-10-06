using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public sealed partial class WarpLogicalMachineLayout
{
    // Physical consistency only. Root must authenticate the complete admitted
    // State/command/checkpoint; none of these writable words grants authority.
    internal const string PrivateHelperScopeVersion = "warp.logical-machine/private-helper-scope/header28-call-pc-plus1-header29-caller-depth-header30-caller-activation-header31-helper-activation/0.1";
    internal const int PrivateHelperScopeCallOffset = 28;
    internal const int PrivateHelperScopeCallerDepthOffset = 29;
    internal const int PrivateHelperScopeCallerActivationOffset = 30;
    internal const int PrivateHelperScopeActivationOffset = 31;

    internal IEnumerable<WarpLogicalMachineNode> GetPrivateScopeNodes(WarpPrivateHelperReturnSite site) =>
        Nodes.Where(node => site.HelperFunctions.Contains(node.Function));

    internal IEnumerable<WarpLogicalMachineNode> GetPrivateScopeCalls(WarpPrivateHelperReturnSite site) =>
        GetPrivateScopeNodes(site).Where(node => node.Call is not null);

    private ReadOnlyCollection<int> CapturePrivateHelperClosure(int helper)
    {
        var functions = new HashSet<int>(); var pending = new Stack<int>(); pending.Push(helper);
        while (pending.TryPop(out int function))
        {
            if (!functions.Add(function)) { continue; }
            foreach (WarpLogicalMachineNode node in Nodes.Where(node => node.Function == function && node.Call is not null))
            { pending.Push(node.Call!.Value.Callee + 1); }
        }
        return Array.AsReadOnly(functions.Order().ToArray());
    }

    private bool HasValidPrivateHelperScope(ReadOnlySpan<uint> state)
    {
        if (!HasPrivateHelperReturnFences) { return true; }
        if (state.Length < HeaderWords + FrameWords + ResultTailWords) { return false; }
        uint tag = state[PrivateHelperScopeCallOffset];
        if (tag == 0)
        {
            return state[PrivateHelperScopeCallerDepthOffset] == 0 && state[PrivateHelperScopeCallerActivationOffset] == 0 &&
                state[PrivateHelperScopeActivationOffset] == 0 && state[SourceBoundaryStateOffset] < AwaitingRootRelease &&
                !HasUnrecordedPrivateHelperScope(state);
        }
        WarpPrivateHelperReturnSite? site = PrivateHelperReturnSites.FirstOrDefault(item => (uint)item.CallProgramCounter + 1 == tag);
        uint callerDepth = state[PrivateHelperScopeCallerDepthOffset], depth = state[DepthOffset];
        uint capacity = (uint)((state.Length - HeaderWords - ResultTailWords) / FrameWords);
        uint phase = state[SourceBoundaryStateOffset];
        if (site is null || state[SourceBoundaryModeOffset] != 1 || state[StatusOffset] != Runnable ||
            callerDepth == 0 || callerDepth >= capacity || depth == 0 || depth > capacity ||
            phase != 0 && phase != AwaitingRootRelease && phase != AcknowledgedRootRelease) { return false; }
        bool restored = phase >= AwaitingRootRelease;
        if (restored ? depth != callerDepth : depth <= callerDepth) { return false; }
        int caller = checked(HeaderWords + (int)(callerDepth - 1) * FrameWords), helper = caller + FrameWords;
        uint parent = callerDepth == 1 ? 0 : state[caller - FrameWords + FrameActivationOffset];
        if (state[caller + FrameFunctionOffset] != site.CallerFunction || state[caller + FrameProgramCounterOffset] != site.Continuation ||
            state[caller + FrameActivationOffset] != state[PrivateHelperScopeCallerActivationOffset] ||
            !HasValidFrameIdentity(state, caller, site.CallerFunction, parent) ||
            state[helper + FrameFunctionOffset] != site.HelperFunction ||
            state[helper + FrameActivationOffset] != state[PrivateHelperScopeActivationOffset] ||
            state[helper + FrameReturnValueOffset] != site.ResultValue || state[helper + FrameReturnWordCountOffset] != site.ResultWordCount ||
            !HasValidFrameIdentity(state, helper, site.HelperFunction, state[caller + FrameActivationOffset])) { return false; }
        if (restored) { return HasPrivateScopeNode(state, helper, site, returning: true); }
        return HasValidActivePrivateFrames(state, site, callerDepth + 1, depth);
    }

    private bool HasValidActivePrivateFrames(ReadOnlySpan<uint> state, WarpPrivateHelperReturnSite site, uint outerDepth, uint depth)
    {
        for (uint index = outerDepth; index <= depth; index++)
        {
            int frame = checked(HeaderWords + (int)(index - 1) * FrameWords);
            if (!HasPrivateScopeNode(state, frame, site, returning: false)) { return false; }
            int function = checked((int)state[frame + FrameFunctionOffset]);
            if (!HasValidFrameIdentity(state, frame, function, state[frame - FrameWords + FrameActivationOffset])) { return false; }
            if (index != outerDepth && !HasPrivateScopeReturnLocation(state, frame, site)) { return false; }
        }
        return true;
    }

    private bool HasPrivateScopeNode(ReadOnlySpan<uint> state, int frame, WarpPrivateHelperReturnSite site, bool returning)
    {
        uint pc = state[frame + FrameProgramCounterOffset];
        return pc < Nodes.Count && site.HelperFunctions.Contains(Nodes[(int)pc].Function) &&
            state[frame + FrameFunctionOffset] == Nodes[(int)pc].Function &&
            (!returning || Nodes[(int)pc].Call is null && (Nodes[(int)pc].Terminator is WarpReturnTerminator or WarpTupleReturnTerminator));
    }

    private bool HasPrivateScopeReturnLocation(ReadOnlySpan<uint> state, int frame, WarpPrivateHelperReturnSite site)
    {
        int parent = frame - FrameWords;
        foreach (WarpLogicalMachineNode node in GetPrivateScopeCalls(site))
        {
            WarpIrInstruction call = node.Call!.Value;
            if (state[parent + FrameFunctionOffset] == node.Function && state[parent + FrameProgramCounterOffset] == node.Continuation &&
                state[frame + FrameFunctionOffset] == call.Callee + 1 && state[frame + FrameReturnValueOffset] == (call.ResultWordCount == 0 ? 0 : call.Result) &&
                state[frame + FrameReturnWordCountOffset] == call.ResultWordCount) { return true; }
        }
        return false;
    }

    private bool HasUnrecordedPrivateHelperScope(ReadOnlySpan<uint> state)
    {
        if (state[SourceBoundaryModeOffset] != 1) { return false; }
        uint depth = state[DepthOffset];
        if (depth == 0 || depth > (uint)((state.Length - HeaderWords - ResultTailWords) / FrameWords)) { return true; }
        for (uint index = 1; index < depth; index++)
        {
            int caller = checked(HeaderWords + (int)(index - 1) * FrameWords);
            foreach (WarpPrivateHelperReturnSite site in PrivateHelperReturnSites)
            {
                if (state[caller + FrameFunctionOffset] == site.CallerFunction && state[caller + FrameProgramCounterOffset] == site.Continuation &&
                    state[caller + FrameWords + FrameFunctionOffset] == site.HelperFunction) { return true; }
            }
        }
        return false;
    }
}
