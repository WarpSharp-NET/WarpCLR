using System.Reflection;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

internal sealed record WarpSourceEventFixture(WarpPortableMethodGraph Graph, WarpPortableSourceHeapSchema Schema,
    WarpPortableWordLoweredProgram Program, WarpPortableSourceSegmentMap Map)
{
    internal const int MaximumDepth = 32;
    internal static readonly Guid CandidateModule = new("7f0e31a5-dead-4e84-a7df-91041f5b2d06");

    internal static WarpSourceEventFixture Capture(string method = nameof(WarpSourceEventKernels.Invoke))
    {
        MethodInfo source = typeof(WarpSourceEventKernels).GetMethod(method)!;
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableClosedFrameSourcePlan plan = WarpPortableClosedFrameSourcePlan.Capture(graph, typed, schema);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed, schema, new WarpPortableClosedFrameSourceBinding(plan));
        return new(graph, schema, program, WarpPortableSourceSegmentMap.Capture(graph, schema, program));
    }

    // This fabricated bank is only a malformed-data test baseline. No test
    // treats these words, module GUID or sequence as runtime authority.
    internal WarpSourceSegmentSnapshot CandidateOrigin()
    {
        WarpLogicalMachineLayout layout = Map.Layout;
        WarpPortableSourceSegment entry = Map.Segments.First(segment => segment.Function == 1);
        uint[] state = layout.CreateInitialState(MaximumDepth, 1000);
        int frame = WarpLogicalMachineLayout.HeaderWords + layout.FrameWords;
        WarpLogicalMachineNode gateway = layout.Nodes.First(node => node.Function == 0 && node.Call is not null);
        WarpIrInstruction gatewayCall = gateway.Call ?? throw new InvalidOperationException("The compiler gateway requires its exact source call.");
        state[WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameProgramCounterOffset] = (uint)gateway.Continuation;
        state[WarpLogicalMachineLayout.DepthOffset] = 2;
        state[WarpLogicalMachineLayout.NextActivationOffset] = 2;
        state[frame + WarpLogicalMachineLayout.FrameFunctionOffset] = 1;
        state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset] = (uint)entry.EntryProgramCounter;
        state[frame + WarpLogicalMachineLayout.FramePrivateWordsOffset] = (uint)layout.GetPrivateWordCount(1);
        state[frame + WarpLogicalMachineLayout.FrameActivationOffset] = 2;
        state[frame + WarpLogicalMachineLayout.FrameReturnValueOffset] = (uint)gatewayCall.Result;
        state[frame + WarpLogicalMachineLayout.FrameReturnWordCountOffset] = (uint)gatewayCall.ResultWordCount;
        state[WarpLogicalMachineLayout.LogicalDepthOffset] = 1;
        layout.SetSourceBoundaryMode(state, true);
        state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] = WarpLogicalMachineLayout.BeforeSourceBoundary;
        return new(1, 1, 123, CandidateModule, Map.IrHash, state, [0xAABBCCDD, 0x11223344]);
    }

    internal static WarpSourceSegmentSnapshot Copy(WarpSourceSegmentSnapshot baseline, Action<uint[]>? mutate = null,
        ulong ordinal = 2, ulong sequence = 2, int? process = null, Guid? module = null, string? irHash = null)
    {
        uint[] state = baseline.State.ToArray(); mutate?.Invoke(state);
        return new(ordinal, sequence, process ?? baseline.ProcessId, module ?? baseline.Module, irHash ?? baseline.IrHash, state, baseline.Arena.AsSpan());
    }
}
