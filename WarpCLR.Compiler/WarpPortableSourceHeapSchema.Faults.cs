using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceHeapSchema
{
    private sealed partial class Builder
    {
        private ImmutableArray<WarpPortableSourceHeapFault> FaultMaps()
        {
            var result = ImmutableArray.CreateBuilder<WarpPortableSourceHeapFault>();
            foreach (WarpPortableTypedMethod method in program.Methods)
            {
                foreach (WarpPortableTypedInstruction instruction in method.Instructions.Where(instruction => instruction.Reachable))
                {
                    foreach (WarpPortableTypedFault fault in instruction.Faults)
                    {
                        Type? exception = FaultType(fault.Kind);
                        result.Add(new(method.Identity, fault.SourceOffset, instruction.OpCode, fault.EffectIndex, fault.Kind,
                            exception is null ? 0 : Id(exception), fault.Kind == WarpPortableTypedFaultKind.AllocationQuota));
                    }
                }
            }
            return result.OrderBy(fault => fault.MethodIdentity, StringComparer.Ordinal).ThenBy(fault => fault.SourceOffset).ThenBy(fault => fault.EffectIndex).ToImmutableArray();
        }

        private static Type? FaultType(WarpPortableTypedFaultKind kind) => kind switch
        {
            WarpPortableTypedFaultKind.NullReference => typeof(NullReferenceException),
            WarpPortableTypedFaultKind.IndexOutOfRange => typeof(IndexOutOfRangeException),
            WarpPortableTypedFaultKind.Overflow => typeof(OverflowException),
            WarpPortableTypedFaultKind.DivideByZero => typeof(DivideByZeroException),
            WarpPortableTypedFaultKind.ArrayTypeMismatch => typeof(ArrayTypeMismatchException),
            WarpPortableTypedFaultKind.InvalidCast => typeof(InvalidCastException),
            WarpPortableTypedFaultKind.TypeInitialization => typeof(TypeInitializationException),
            WarpPortableTypedFaultKind.AllocationQuota => typeof(OutOfMemoryException),
            WarpPortableTypedFaultKind.CalledException => null,
            _ => throw new InvalidOperationException("The typed source fault kind has no schema binding."),
        };
    }
}
