using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceSegmentMap
{
    // Traverse finite zero-charge setup only. First guest instructions are
    // candidates after argument/prefix copies, never permission to run them.
    private static void AddFirstBoundaries(HashSet<WarpPortableSourceSegmentFrontier> result, WarpLogicalMachineLayout layout,
        int entry, WarpPortableSourceSegmentEndKind kind)
    {
        var pending = new Queue<int>(); pending.Enqueue(entry);
        var visited = new HashSet<int>();
        while (pending.TryDequeue(out int pc))
        {
            if (!visited.Add(pc)) { continue; }
            WarpLogicalMachineNode node = layout.Nodes[pc];
            if (node.StartsBlock && node.SourceCost != 0 && !layout.IsRuntimeHelper(node.Function))
            { result.Add(new(pc, node.Function, kind)); continue; }
            if (node.Call is { } call)
            {
                pending.Enqueue(layout.GetBlockEntry(call.Callee + 1, 0));
                pending.Enqueue(node.Continuation);
                continue;
            }
            if (node.Terminator is WarpStateDispatchTerminator dispatch)
            {
                foreach (WarpStateDispatchTarget target in dispatch.Destinations)
                { pending.Enqueue(layout.GetBlockEntry(target.Function, target.Block)); }
                continue;
            }
            foreach (WarpBranchTarget target in Targets(node.Terminator))
            { pending.Enqueue(layout.GetBlockEntry(node.Function, target.Block)); }
        }
    }
}
