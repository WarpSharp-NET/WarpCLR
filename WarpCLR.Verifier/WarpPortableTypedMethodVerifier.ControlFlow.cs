using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private void PrepareControlFlow()
    {
        ValidateRegions();
        PreparePrefixes();
        for (int index = 0; index < method.Instructions.Length; index++)
        {
            WarpPortableMethodGraphInstruction instruction = method.Instructions[index];
            if (!Known(instruction.OpCode)) { throw Error($"Unsupported typed opcode '{instruction.OpCode.Name}'.", instruction.Offset); }
            memberships[index] = Memberships(instruction.Offset);
            unwind[index] = [];
        }

        for (int index = 0; index < method.Instructions.Length; index++) { successors[index] = Successors(index); }
        for (int index = 0; index < method.Instructions.Length; index++)
        {
            if (method.Instructions[index].OpCode == OpCodes.Leave || method.Instructions[index].OpCode == OpCodes.Leave_S)
            {
                PrepareLeave(index);
            }
        }

        for (int index = 0; index < method.Instructions.Length; index++)
        {
            if (method.Instructions[index].OpCode == OpCodes.Endfinally)
            {
                successors[index] = memberships[index].Where(member => member.Role is WarpPortableExceptionRole.Finally or WarpPortableExceptionRole.Fault)
                    .OrderBy(member => method.ExceptionRegions[member.Region].HandlerLength).Take(1).SelectMany(member => finallyContinuations.GetValueOrDefault(member.Region) ?? []).Distinct().Order().ToImmutableArray();
            }

            ValidateEdges(index);
        }
    }

    private ImmutableArray<int> Successors(int index)
    {
        WarpPortableMethodGraphInstruction instruction = method.Instructions[index];
        OpCode operation = instruction.OpCode;
        if (operation == OpCodes.Endfinally || operation == OpCodes.Endfilter || operation == OpCodes.Ret ||
            operation == OpCodes.Throw || operation == OpCodes.Rethrow) { return []; }
        ImmutableArray<int> branches = instruction.BranchTargets.Select(target => indices[target]).ToImmutableArray();
        if (operation.FlowControl == FlowControl.Branch) { return branches; }
        if (index + 1 == method.Instructions.Length)
        {
            throw Error("An instruction falls through the end of its CIL method.", instruction.Offset);
        }

        return branches.Add(index + 1).Distinct().ToImmutableArray();
    }

    private ImmutableArray<WarpPortableTypedExceptionMembership> Memberships(int offset)
    {
        var result = ImmutableArray.CreateBuilder<WarpPortableTypedExceptionMembership>();
        for (int region = 0; region < method.ExceptionRegions.Length; region++)
        {
            WarpPortableMethodGraphExceptionRegion clause = method.ExceptionRegions[region];
            if (Inside(offset, clause.TryOffset, clause.TryLength)) { result.Add(new(region, WarpPortableExceptionRole.Try)); }
            if (Inside(offset, clause.HandlerOffset, clause.HandlerLength))
            {
                WarpPortableExceptionRole role = clause.Kind == (int)ExceptionHandlingClauseOptions.Finally ? WarpPortableExceptionRole.Finally :
                    clause.Kind == (int)ExceptionHandlingClauseOptions.Fault ? WarpPortableExceptionRole.Fault : WarpPortableExceptionRole.Catch;
                result.Add(new(region, role));
            }

            if (clause.FilterOffset >= 0 && Inside(offset, clause.FilterOffset, clause.HandlerOffset - clause.FilterOffset))
            {
                result.Add(new(region, WarpPortableExceptionRole.Filter));
            }
        }

        return result.ToImmutable();
    }

    private void PrepareLeave(int index)
    {
        WarpPortableMethodGraphInstruction source = method.Instructions[index];
        int target = indices[source.BranchTargets[0]];
        if (memberships[index].Any(member => member.Role is WarpPortableExceptionRole.Filter or WarpPortableExceptionRole.Finally or WarpPortableExceptionRole.Fault) ||
            memberships[target].Any(member => !memberships[index].Contains(member)))
        {
            throw Error("Leave cannot originate in a filter/finally/fault or enter a protected region.", source.Offset);
        }

        ImmutableArray<int> regions = memberships[index].Where(member => member.Role == WarpPortableExceptionRole.Try && !memberships[target].Contains(member) &&
            method.ExceptionRegions[member.Region].Kind == (int)ExceptionHandlingClauseOptions.Finally)
            .OrderBy(member => method.ExceptionRegions[member.Region].TryLength).Select(member => member.Region).ToImmutableArray();
        unwind[index] = regions;
        if (regions.IsEmpty) { successors[index] = [target]; return; }
        successors[index] = [indices[method.ExceptionRegions[regions[0]].HandlerOffset]];
        for (int part = 0; part < regions.Length; part++)
        {
            int continuation = part + 1 < regions.Length ? indices[method.ExceptionRegions[regions[part + 1]].HandlerOffset] : target;
            if (!finallyContinuations.TryGetValue(regions[part], out HashSet<int>? continuations))
            {
                continuations = []; finallyContinuations.Add(regions[part], continuations);
            }

            continuations.Add(continuation);
        }
    }

    private void ValidateEdges(int index)
    {
        WarpPortableMethodGraphInstruction source = method.Instructions[index];
        if (source.OpCode == OpCodes.Leave || source.OpCode == OpCodes.Leave_S || source.OpCode == OpCodes.Endfinally) { return; }
        if (source.OpCode == OpCodes.Ret && !memberships[index].IsEmpty)
        {
            throw Error("Return cannot bypass protected-region unwinding.", source.Offset);
        }

        foreach (int target in successors[index])
        {
            bool fallthrough = target == index + 1 && source.OpCode.FlowControl != FlowControl.Branch;
            if (memberships[index].Any(member => !memberships[target].Contains(member)) || memberships[target].Any(member =>
                !memberships[index].Contains(member) && (!fallthrough || member.Role != WarpPortableExceptionRole.Try ||
                    method.ExceptionRegions[member.Region].TryOffset != method.Instructions[target].Offset)))
            {
                throw Error("An ordinary branch/fallthrough crosses an exception-region boundary.", source.Offset);
            }
        }
    }

    private static bool Inside(int offset, int start, int length) => offset >= start && (long)offset < (long)start + length;
}
