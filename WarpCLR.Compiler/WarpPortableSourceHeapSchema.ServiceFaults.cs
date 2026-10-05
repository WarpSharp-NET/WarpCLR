using System.Collections.Immutable;
using System.Reflection;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceHeapSchema
{
    private sealed partial class Builder
    {
        private ImmutableArray<WarpPortableSourceHeapServiceFault> ServiceFaultMaps()
        {
            var result = ImmutableArray.CreateBuilder<WarpPortableSourceHeapServiceFault>();
            Dictionary<string, WarpPortableMethodGraphMethod> methods = graph.Methods.ToDictionary(method => method.Identity, StringComparer.Ordinal);
            foreach (WarpPortableTypedMethod method in program.Methods)
            {
                WarpPortableMethodGraphMethod source = methods[method.Identity];
                foreach (WarpPortableTypedInstruction instruction in method.Instructions.Where(instruction => instruction.Reachable))
                {
                    WarpPortableMethodGraphInstruction captured = source.Instructions.First(item => item.Offset == instruction.Offset);
                    if (captured.Method is not { } target || methods[target].SourceMethod is not MethodInfo call ||
                        WarpPortableWordMathCatalog.Resolve(call) is not { } binding || binding.Faults.IsEmpty) { continue; }
                    int effect = instruction.Effects.IndexOf(WarpPortableTypedEffect.Call);
                    if (effect < 0) { throw new WarpVerificationException("WRPCLR2400", "A faulting Math binding has no ordered source call effect.", instruction.Offset); }
                    foreach (WarpPortableWordMathFault fault in binding.Faults)
                    {
                        Type exception = FaultTypes.First(type => string.Equals(type.FullName, fault.ExceptionType, StringComparison.Ordinal));
                        result.Add(new(method.Identity, instruction.Offset, instruction.OpCode, effect, binding.Identity,
                            fault.Descriptor, Id(exception), binding.FaultOperandWords));
                    }
                }
            }
            return result.OrderBy(fault => fault.MethodIdentity, StringComparer.Ordinal).ThenBy(fault => fault.SourceOffset).ThenBy(fault => fault.Descriptor).ToImmutableArray();
        }
    }
}
