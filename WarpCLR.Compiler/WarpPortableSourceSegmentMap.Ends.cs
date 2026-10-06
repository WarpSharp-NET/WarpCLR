using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceSegmentMap
{
    internal void RequireExactInvocation(WarpPortableSourceInvocation invocation)
    {
        if (!ReferenceEquals(Invocation, invocation))
        { throw new InvalidOperationException("Only the exact separately captured before-body invocation belongs to this map."); }
    }

    internal void RequireExactGuardedFrontier(WarpPortableSourceSegment segment, WarpPortableSourceGuardedFrontier frontier)
    {
        RequireExactSegment(segment);
        if (!segment.GuardedFrontiers.Any(bound => ReferenceEquals(bound, frontier)))
        { throw new InvalidOperationException("A guarded end requires the exact immutable dispatch-site and target candidate."); }
    }

    private static WarpPortableSourceInvocation CaptureInvocation(WarpPortableMethodGraph graph,
        WarpPortableWordLoweredProgram program, WarpLogicalMachineLayout layout)
    {
        WarpPortableWordBody root = program.Bodies.First(body => string.Equals(body.MethodIdentity, graph.EntryIdentity, StringComparison.Ordinal));
        WarpPortableTypedMethod source = program.VerifiedProgram.Methods.First(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal));
        var blocks = root.InvocationPrelude?.GeneratedBlocks ?? [];
        WarpLogicalMachineNode[] local = layout.Nodes.Where(node => node.Function == 0 ||
            node.Function == root.Function && blocks.Contains(node.Block)).ToArray();
        if (local.Length == 0 || local.Any(node => node.SourceCost != 0 || node.Function != 0 && layout.IsRuntimeHelper(node.Function)))
        { throw new InvalidOperationException("An invocation contains only exact zero-charge wrapper/prelude setup."); }
        var ends = new HashSet<WarpPortableSourceSegmentFrontier>();
        AddFirstBoundaries(ends, layout, layout.GetBlockEntry(0, 0), WarpPortableSourceSegmentEndKind.BeforeGuestCall);
        int captured = program.VerifiedProgram.Methods.Select((method, index) => (method, index)).First(item =>
            string.Equals(item.method.Identity, graph.EntryIdentity, StringComparison.Ordinal)).index + 1;
        string signature = WarpPortableSnapshotIdentity.Hash(new
        {
            WarpPortableSourceInvocation.Version, source.Identity, source.ArgumentTypes, source.LocalTypes,
            source.ReturnType, graph.EntryIdentity, program.VerifiedProgram.EntryInitializerTrigger,
        });
        return new(graph.EntryIdentity, captured, root.Function, layout.GetBlockEntry(0, 0), signature,
            local.Select(node => node.ProgramCounter).ToImmutableArray(), Helpers(layout, local),
            ends.OrderBy(end => end.Function).ThenBy(end => end.ProgramCounter).ToImmutableArray(), root.Arguments,
            program.EntryProjection.ResultType, root.InvocationPrelude?.Roots ?? root.SourceBlocks[0].Roots,
            program.EntryProjection, root.InvocationPrelude);
    }

    private static ImmutableArray<WarpPortableSourceGuardedFrontier> GuardedFrontiers(WarpLogicalMachineLayout layout,
        IEnumerable<WarpLogicalMachineNode> local, ImmutableArray<int> helpers)
    {
        var ends = new HashSet<WarpPortableSourceGuardedFrontier>();
        foreach (WarpLogicalMachineNode node in local.Concat(layout.Nodes.Where(node => helpers.Contains(node.Function))))
        {
            if (node.Call is not null || node.Terminator is not WarpStateDispatchTerminator dispatch) { continue; }
            foreach (WarpStateDispatchTarget target in dispatch.Destinations)
            {
                var boundaries = new HashSet<WarpPortableSourceSegmentFrontier>();
                AddFirstBoundaries(boundaries, layout, layout.GetBlockEntry(target.Function, target.Block), WarpPortableSourceSegmentEndKind.NonlocalDispatch);
                foreach (WarpPortableSourceSegmentFrontier end in boundaries)
                {
                    ends.Add(new(node.Function, node.ProgramCounter, target.Function, target.Block,
                        end.Function, end.ProgramCounter, layout.GetAliasOwnerFunction(end.Function), layout.GetAliasPrefixWords(end.Function)));
                }
            }
        }
        WarpCompilationAdmission.Require(layout.Kernel.Name, WarpCompilationResourceKind.VerifierWorkspaceSlots,
            ends.Count, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
        return ends.OrderBy(end => end.DispatchProgramCounter).ThenBy(end => end.TargetFunction)
            .ThenBy(end => end.TargetBlock).ThenBy(end => end.EndProgramCounter).ToImmutableArray();
    }
}
