using System.Reflection;
using System.Reflection.Emit;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private void ValidateRegions()
    {
        WarpCLR.IR.WarpCompilationAdmission.Require(method.Identity, WarpCLR.IR.WarpCompilationResourceKind.Blocks,
            method.ExceptionRegions.Length, 256);
        var segments = new List<(int Start, int End)>();
        foreach (WarpPortableMethodGraphExceptionRegion region in method.ExceptionRegions)
        {
            if (region.Kind is not (0 or 1 or 2 or 4) || Inside(region.HandlerOffset, region.TryOffset, region.TryLength) ||
                Inside(region.TryOffset, region.HandlerOffset, region.HandlerLength))
            {
                throw Error("An exception clause has an invalid kind or overlapping try and handler.", region.TryOffset);
            }

            if (region.CatchType is { } catchType && !typeof(Exception).IsAssignableFrom(types.Source(catchType)))
            {
                throw Error("A catch type must derive from the portable Exception type.", region.HandlerOffset);
            }

            segments.Add((region.TryOffset, checked(region.TryOffset + region.TryLength)));
            segments.Add((region.HandlerOffset, checked(region.HandlerOffset + region.HandlerLength)));
            if (region.FilterOffset >= 0) { segments.Add((region.FilterOffset, region.HandlerOffset)); }
        }

        for (int first = 0; first < segments.Count; first++)
        {
            for (int second = first + 1; second < segments.Count; second++)
            {
                (int start, int end) = segments[first];
                (int otherStart, int otherEnd) = segments[second];
                if (start < otherStart && otherStart < end && end < otherEnd || otherStart < start && start < otherEnd && otherEnd < end)
                {
                    throw Error("Exception regions cross instead of nesting or remaining disjoint.", Math.Max(start, otherStart));
                }
            }
        }
    }

    private void PreparePrefixes()
    {
        var branchTargets = new HashSet<int>(method.Instructions.SelectMany(instruction => instruction.BranchTargets));
        for (int index = 0; index < method.Instructions.Length; index++)
        {
            if (method.Instructions[index].OpCode.OpCodeType != OpCodeType.Prefix) { continue; }
            int start = index;
            var prefix = new Prefix();
            while (index < method.Instructions.Length && method.Instructions[index].OpCode.OpCodeType == OpCodeType.Prefix)
            {
                WarpPortableMethodGraphInstruction instruction = method.Instructions[index];
                if (index != start && branchTargets.Contains(instruction.Offset)) { throw Error("A branch enters a prefix sequence.", instruction.Offset); }
                prefix = AddPrefix(prefix, instruction);
                index++;
            }

            if (index == method.Instructions.Length || branchTargets.Contains(method.Instructions[index].Offset))
            {
                throw Error("A prefix is unconsumed or bypassed by a branch.", method.Instructions[start].Offset);
            }

            ValidatePrefixTarget(prefix, method.Instructions[index]);
            prefixes.Add(index, prefix);
        }
    }

    private Prefix AddPrefix(Prefix prefix, WarpPortableMethodGraphInstruction instruction)
    {
        OpCode operation = instruction.OpCode;
        if (operation == OpCodes.Constrained && prefix.Constrained is null) { return prefix with { Constrained = instruction.Type }; }
        if (operation == OpCodes.Readonly && !prefix.ReadOnly) { return prefix with { ReadOnly = true }; }
        if (operation == OpCodes.Volatile && !prefix.Volatile) { return prefix with { Volatile = true }; }
        if (operation == OpCodes.Unaligned && prefix.Alignment == 0 && instruction.Operand is 1 or 2 or 4)
        {
            return prefix with { Alignment = (int)instruction.Operand };
        }

        throw Error("An unsupported or repeated CIL prefix cannot be silently ignored.", instruction.Offset);
    }

    private void ValidatePrefixTarget(Prefix prefix, WarpPortableMethodGraphInstruction target)
    {
        bool arrayAddress = target.OpCode == OpCodes.Call && target.Method is { } identity && methods[identity].SourceMethod.DeclaringType!.IsArray &&
            string.Equals(methods[identity].SourceMethod.Name, "Address", StringComparison.Ordinal);
        if (prefix.Constrained is not null && target.OpCode != OpCodes.Callvirt || prefix.ReadOnly && target.OpCode != OpCodes.Ldelema && !arrayAddress ||
            (prefix.Volatile || prefix.Alignment != 0) && !MemoryOperation(target.OpCode))
        {
            throw Error("A CIL prefix targets an incompatible operation.", target.Offset);
        }
    }

    private static bool MemoryOperation(OpCode operation) => operation == OpCodes.Ldfld || operation == OpCodes.Stfld ||
        operation == OpCodes.Ldsfld || operation == OpCodes.Stsfld || operation == OpCodes.Ldobj || operation == OpCodes.Stobj ||
        operation.Name?.StartsWith("ldind.", StringComparison.Ordinal) == true || operation.Name?.StartsWith("stind.", StringComparison.Ordinal) == true;
}
