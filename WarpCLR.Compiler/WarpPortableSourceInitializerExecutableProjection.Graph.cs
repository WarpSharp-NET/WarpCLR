using System.Collections.Immutable;
using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceInitializerExecutableProjection
{
    private WarpPortableSourceInitializerCandidate Walk(int entry)
    {
        // Interned return stacks make branch/loop traversal finite without host recursion.
        var stacks = new List<(int Continuation, int Parent)> { (-1, -1) };
        var stackIds = new Dictionary<(int Continuation, int Parent), int>();
        var pending = new Queue<(int Pc, int Stack, bool Initial)>(); pending.Enqueue((entry, 0, true));
        var examined = new HashSet<(int Pc, int Stack, bool Initial)>();
        var visited = new SortedSet<int>(); var helpers = new SortedSet<int>();
        var frontiers = new HashSet<WarpPortableSourceInitializerFrontier>();
        while (pending.TryDequeue(out (int Pc, int Stack, bool Initial) state))
        {
            if (!examined.Add(state)) { continue; }
            WarpCompilationAdmission.Require(program.Kernel.Name, WarpCompilationResourceKind.ValueSlots,
                examined.Count + (long)stacks.Count, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
            WarpLogicalMachineNode node = Layout.Nodes[state.Pc];
            WarpPortableSourceInitializerExecutablePoint point = Points[state.Pc];
            if (!state.Initial && point.ChargedSourceSteps != 0)
            {
                frontiers.Add(Frontier(GuestEntryKind(point), node, node.ProgramCounter, node.Function, null));
                continue;
            }
            visited.Add(node.ProgramCounter);
            if (point.RuntimeHelper) { helpers.Add(node.Function); }
            if (node.Call is { } call)
            {
                int callee = call.Callee + 1;
                int target = Layout.GetBlockEntry(callee, 0);
                if (!Layout.IsRuntimeHelper(callee))
                {
                    WarpPortableWordBody body = program.Bodies.First(body => body.Function == callee);
                    MethodBase method = graph.Methods.First(method => string.Equals(method.Identity, body.MethodIdentity, StringComparison.Ordinal)).SourceMethod;
                    WarpPortableSourceInitializerFrontierKind kind = method is ConstructorInfo constructor
                        ? constructor.IsStatic ? WarpPortableSourceInitializerFrontierKind.InitializerCall : WarpPortableSourceInitializerFrontierKind.ConstructorCall
                        : WarpPortableSourceInitializerFrontierKind.SourceCall;
                    frontiers.Add(Frontier(kind, node, target, callee, node.Continuation));
                    continue;
                }
                if (!stackIds.TryGetValue((node.Continuation, state.Stack), out int id))
                {
                    id = stacks.Count; stacks.Add((node.Continuation, state.Stack)); stackIds.Add((node.Continuation, state.Stack), id);
                }
                pending.Enqueue((target, id, false));
                // The copied terminator belongs to the block's final node, not this call node.
                continue;
            }
            ExpandTerminator(node, state.Stack, pending, stacks, frontiers);
        }
        return new(ProjectionHash, entry, visited.ToImmutableArray(), helpers.ToImmutableArray(),
            frontiers.OrderBy(frontier => frontier.ProgramCounter).ThenBy(frontier => frontier.Kind)
                .ThenBy(frontier => frontier.TargetFunction).ThenBy(frontier => frontier.TargetProgramCounter).ToImmutableArray(), examined.Count);
    }

    private void ExpandTerminator(WarpLogicalMachineNode node, int returnStack,
        Queue<(int Pc, int Stack, bool Initial)> pending, List<(int Continuation, int Parent)> stacks,
        HashSet<WarpPortableSourceInitializerFrontier> frontiers)
    {
        switch (node.Terminator)
            {
                case WarpBranchTerminator branch:
                    pending.Enqueue((Layout.GetBlockEntry(node.Function, branch.Target.Block), returnStack, false));
                    break;
                case WarpConditionalBranchTerminator branch:
                    pending.Enqueue((Layout.GetBlockEntry(node.Function, branch.WhenNonZero.Block), returnStack, false));
                    pending.Enqueue((Layout.GetBlockEntry(node.Function, branch.WhenZero.Block), returnStack, false));
                    break;
                case WarpReturnTerminator or WarpTupleReturnTerminator:
                    if (returnStack == 0)
                    {
                        frontiers.Add(Frontier(WarpPortableSourceInitializerFrontierKind.SourceReturn, node, null, null, null));
                    }
                    else
                    {
                        (int continuation, int parent) = stacks[returnStack]; pending.Enqueue((continuation, parent, false));
                    }
                    break;
                case WarpStateDispatchTerminator dispatch:
                    foreach (WarpStateDispatchTarget destination in dispatch.Destinations)
                    {
                        frontiers.Add(Frontier(WarpPortableSourceInitializerFrontierKind.GuardedStateDispatch, node,
                            Layout.GetBlockEntry(destination.Function, destination.Block), destination.Function, null));
                    }
                    break;
                case WarpManagedExceptionTerminator:
                    frontiers.Add(Frontier(WarpPortableSourceInitializerFrontierKind.GuardedManagedTermination, node, null, null, null));
                    break;
                default:
                    throw Invalid("The final executable node has no admitted structural continuation.", Points[node.ProgramCounter].SourceOffset ?? 0);
            }
    }

    private WarpPortableSourceInitializerFrontier Frontier(WarpPortableSourceInitializerFrontierKind kind,
        WarpLogicalMachineNode node, int? targetPc, int? targetFunction, int? continuation)
    {
        WarpPortableWordBody? body = targetFunction is { } function ? program.Bodies.FirstOrDefault(body => body.Function == function) : null;
        WarpPortableSourceInitializerExecutablePoint? target = targetPc is { } pc ? Points[pc] : null;
        int? firstGuest = target?.ChargedSourceSteps == 1 ? target.ProgramCounter : body is null || body.SourceBlocks.IsEmpty ? null :
            Layout.GetBlockEntry(body.Function, body.SourceBlocks[0].Block);
        return new(kind, node.ProgramCounter, node.Function, targetPc, targetFunction, continuation,
            firstGuest, body?.MethodIdentity, target?.SourceOffset, RequiresRuntimeReceipt: true);
    }

    private WarpPortableSourceInitializerFrontierKind GuestEntryKind(WarpPortableSourceInitializerExecutablePoint point)
    {
        WarpPortableMethodGraphMethod method = graph.Methods.First(method => string.Equals(method.Identity, point.MethodIdentity, StringComparison.Ordinal));
        if (method.ExceptionRegions.Any(region => region.Kind == 1 && region.FilterOffset == point.SourceOffset))
        {
            return WarpPortableSourceInitializerFrontierKind.FilterEntry;
        }
        return method.ExceptionRegions.Any(region => region.HandlerOffset == point.SourceOffset)
            ? WarpPortableSourceInitializerFrontierKind.HandlerEntry : WarpPortableSourceInitializerFrontierKind.OriginalInstruction;
    }
}
