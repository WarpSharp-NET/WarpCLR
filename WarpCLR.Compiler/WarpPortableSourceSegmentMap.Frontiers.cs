using System.Collections.Frozen;
using System.Collections.Immutable;
using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceSegmentMap
{
    private static ImmutableArray<int> Helpers(WarpLogicalMachineLayout layout, IEnumerable<WarpLogicalMachineNode> local)
    {
        var pending = new Queue<int>(local.SelectMany(HelperTargets));
        var visited = new HashSet<int>();
        while (pending.TryDequeue(out int function))
        {
            if (!layout.IsRuntimeHelper(function) || !visited.Add(function)) { continue; }
            foreach (int target in layout.Nodes.Where(node => node.Function == function).SelectMany(HelperTargets))
            { pending.Enqueue(target); }
        }
        return visited.Order().ToImmutableArray();
    }

    private static ImmutableArray<WarpPortableSourceSegmentFrontier> Frontiers(WarpLogicalMachineLayout layout, int function,
        FrozenSet<int> owned, IEnumerable<WarpLogicalMachineNode> local, ImmutableArray<int> helpers)
    {
        var result = new HashSet<WarpPortableSourceSegmentFrontier>();
        foreach (WarpLogicalMachineNode node in local.Concat(layout.Nodes.Where(node => helpers.Contains(node.Function))))
        {
            if (node.Call is { } call)
            {
                int callee = call.Callee + 1;
                if (!layout.IsRuntimeHelper(callee))
                { AddFirstBoundaries(result, layout, layout.GetBlockEntry(callee, 0), WarpPortableSourceSegmentEndKind.BeforeGuestCall); }
                continue;
            }
            if (node.Terminator is WarpStateDispatchTerminator dispatch)
            {
                foreach (WarpStateDispatchTarget target in dispatch.Destinations)
                {
                    if (!layout.IsRuntimeHelper(target.Function))
                    { AddFirstBoundaries(result, layout, layout.GetBlockEntry(target.Function, target.Block), WarpPortableSourceSegmentEndKind.NonlocalDispatch); }
                }
                continue;
            }
            if (node.Terminator is WarpManagedExceptionTerminator)
            { result.Add(new(node.ProgramCounter, node.Function, WarpPortableSourceSegmentEndKind.ManagedExceptionTerminal)); continue; }
            if (node.Function != function) { continue; }
            if (node.Terminator is WarpReturnTerminator or WarpTupleReturnTerminator)
            {
                result.Add(new(node.ProgramCounter, function, WarpPortableSourceSegmentEndKind.SourceReturn));
                foreach (WarpLogicalMachineNode caller in layout.Nodes.Where(caller => caller.Call?.Callee + 1 == function))
                { AddFirstBoundaries(result, layout, caller.Continuation, WarpPortableSourceSegmentEndKind.SourceReturn); }
                continue;
            }
            foreach (WarpBranchTarget target in Targets(node.Terminator))
            {
                int pc = layout.GetBlockEntry(function, target.Block);
                // A loop back to the same charged original block starts a NEW operation.
                if (!owned.Contains(target.Block) || layout.Nodes[pc].SourceCost != 0)
                { AddFirstBoundaries(result, layout, pc, WarpPortableSourceSegmentEndKind.OriginalInstructionBoundary); }
            }
        }
        return result.OrderBy(item => item.Function).ThenBy(item => item.ProgramCounter).ThenBy(item => item.Kind).ToImmutableArray();
    }

    private static IEnumerable<int> HelperTargets(WarpLogicalMachineNode node)
    {
        if (node.Call is { } call) { yield return call.Callee + 1; }
        if (node.Terminator is WarpStateDispatchTerminator dispatch)
        { foreach (WarpStateDispatchTarget target in dispatch.Destinations) { yield return target.Function; } }
    }

    private static IEnumerable<WarpBranchTarget> Targets(WarpBlockTerminator terminator) => terminator switch
    {
        WarpBranchTerminator branch => [branch.Target],
        WarpConditionalBranchTerminator branch => [branch.WhenNonZero, branch.WhenZero],
        _ => [],
    };
}
