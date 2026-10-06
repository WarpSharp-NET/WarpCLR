using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public sealed partial class WarpLogicalMachineLayout
{
    internal const uint AwaitingRootRelease = 5;
    internal const uint AcknowledgedRootRelease = 6;
    private readonly ReadOnlyCollection<WarpPrivateHelperReturnSite> privateHelperReturnSites;

    internal bool HasPrivateHelperReturnFences => Kernel.Execution?.PrivateControllerProjection?.RequiresHelperReturnFences == true;
    internal uint MaximumSourceBoundaryPhase => HasPrivateHelperReturnFences ? AcknowledgedRootRelease :
        HasPrivateHelperBoundaries ? AcknowledgedPrivateHelper : AcknowledgedSourceBoundary;
    internal ReadOnlyCollection<WarpPrivateHelperReturnSite> PrivateHelperReturnSites => privateHelperReturnSites;

    internal IEnumerable<WarpLogicalMachineNode> PrivateHelperDispatchNodes => Nodes.Where(node =>
        IsPrivateHelperBoundary(node) || PrivateHelperReturnSites.Any(site => site.Continuation == node.ProgramCounter));

    // Consistency only: the production Host must require its opaque, exact
    // committed publication and normal-release receipts before calling this.
    internal void AcknowledgePrivateHelperRelease(Span<uint> state)
    {
        if (!HasValidRuntimeHeader(state) || state[SourceBoundaryStateOffset] != AwaitingRootRelease)
        { throw new InvalidOperationException("Only the exact restored caller publication fence may be acknowledged."); }
        state[SourceBoundaryStateOffset] = AcknowledgedRootRelease;
    }

    internal void RequireAcknowledgedPrivateHelperRelease(ReadOnlySpan<uint> state, uint controller)
    {
        if (controller != 0 || !HasValidRuntimeHeader(state) || state[SourceBoundaryStateOffset] != AcknowledgedRootRelease)
        { throw new InvalidOperationException("The exact acknowledged caller successor requires a zero controller word."); }
    }

    private ReadOnlyCollection<WarpPrivateHelperReturnSite> CapturePrivateHelperReturnSites(WarpControlFlowKernel kernel)
    {
        if (!HasPrivateHelperReturnFences) { return Array.AsReadOnly(Array.Empty<WarpPrivateHelperReturnSite>()); }
        var sites = new List<WarpPrivateHelperReturnSite>();
        foreach (WarpPrivateControllerUse use in kernel.Execution!.PrivateControllerProjection!.Uses)
        {
            WarpLogicalMachineNode[] calls = Nodes.Where(node => node.Function == use.Function && node.Block == use.Block &&
                node.Call is { } candidateCall && candidateCall.Callee == use.Callee && candidateCall.Result == use.CallValue).ToArray();
            if (calls.Length != 1 || !IsPrivateHelperBoundary(calls[0]))
            { throw new ArgumentException("A private return fence needs exactly one admitted bridge Call.", nameof(kernel)); }
            WarpLogicalMachineNode selectedNode = calls[0];
            WarpIrInstruction selectedCall = selectedNode.Call ?? throw new ArgumentException("The private return Call is absent.", nameof(kernel));
            if ((uint)selectedNode.Continuation >= (uint)Nodes.Count || Nodes[selectedNode.Continuation].Function != use.Function ||
                Nodes[selectedNode.Continuation].Block != use.Block || Nodes[selectedNode.Continuation].StartsBlock)
            { throw new ArgumentException("A private return fence needs its exact uncharged caller continuation.", nameof(kernel)); }
            sites.Add(new(use.Function, use.Callee + 1, selectedNode.ProgramCounter, selectedNode.Continuation,
                selectedCall.ResultWordCount == 0 ? 0 : selectedCall.Result, selectedCall.ResultWordCount,
                CapturePrivateHelperClosure(use.Callee + 1)));
        }
        if (sites.Select(site => site.Continuation).Distinct().Count() != sites.Count)
        { throw new ArgumentException("Private return continuations must be unique.", nameof(kernel)); }
        return sites.AsReadOnly();
    }

    private bool HasValidPrivateHelperReturnFrame(ReadOnlySpan<uint> state, uint depth, uint pc)
    {
        if (!HasPrivateHelperReturnFences || !HasValidPrivateHelperScope(state) || state[StatusOffset] != Runnable || state[SourceBoundaryModeOffset] != 1 ||
            pc >= Nodes.Count) { return false; }
        WarpPrivateHelperReturnSite? site = PrivateHelperReturnSites.FirstOrDefault(item => item.Continuation == pc);
        if (site is null) { return false; }
        int frame = checked(HeaderWords + (int)(depth - 1) * FrameWords);
        uint parent = depth == 1 ? 0 : state[frame - FrameWords + FrameActivationOffset];
        return state[frame + FrameFunctionOffset] == site.CallerFunction &&
            HasValidFrameIdentity(state, frame, site.CallerFunction, parent);
    }
}
