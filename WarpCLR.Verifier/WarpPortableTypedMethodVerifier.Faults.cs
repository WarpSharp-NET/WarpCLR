using System.Collections.Immutable;
using System.Reflection.Emit;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    internal static ImmutableArray<WarpPortableTypedProvenance> SortOrigins(IEnumerable<WarpPortableTypedProvenance> origins) => origins.Distinct()
        .OrderBy(origin => origin.Kind).ThenBy(origin => origin.OwnerMethod, StringComparer.Ordinal).ThenBy(origin => origin.OwnerIndex)
        .ThenBy(origin => origin.OwnerType, StringComparer.Ordinal).ThenBy(origin => origin.ByteOffset).ThenBy(origin => origin.ByteLength).ToImmutableArray();

    private static ImmutableArray<WarpPortableTypedFault> Faults(WarpPortableMethodGraphInstruction instruction, Step step)
    {
        var result = ImmutableArray.CreateBuilder<WarpPortableTypedFault>();
        for (int index = 0; index < step.Effects.Count; index++)
        {
            WarpPortableTypedFaultKind? kind = step.Effects[index] switch
            {
                WarpPortableTypedEffect.NullCheck => WarpPortableTypedFaultKind.NullReference,
                WarpPortableTypedEffect.BoundsCheck => WarpPortableTypedFaultKind.IndexOutOfRange,
                WarpPortableTypedEffect.OverflowCheck => WarpPortableTypedFaultKind.Overflow,
                WarpPortableTypedEffect.DivideByZeroCheck => WarpPortableTypedFaultKind.DivideByZero,
                WarpPortableTypedEffect.TypeCheck => instruction.OpCode == OpCodes.Stelem_Ref || instruction.OpCode == OpCodes.Ldelema ||
                    instruction.OpCode == OpCodes.Stelem && step.MemoryType is not null ? WarpPortableTypedFaultKind.ArrayTypeMismatch : WarpPortableTypedFaultKind.InvalidCast,
                WarpPortableTypedEffect.TypeInitialize => WarpPortableTypedFaultKind.TypeInitialization,
                WarpPortableTypedEffect.Allocate => WarpPortableTypedFaultKind.AllocationQuota,
                WarpPortableTypedEffect.Call => WarpPortableTypedFaultKind.CalledException,
                _ => null,
            };
            if (kind is { } value) { result.Add(new(value, instruction.Offset, index)); }
        }

        return result.ToImmutable();
    }
}
