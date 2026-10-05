using System.Collections.Immutable;
using System.Reflection;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceFaultFactoryContract
{
    private static void AddFiniteMath(WarpPortableMethodGraph graph, WarpPortableTypedProgram program, WarpPortableSourceHeapSchema schema,
        ImmutableArray<WarpPortableSourceFaultFactoryRow>.Builder rows)
    {
        Dictionary<string, WarpPortableMethodGraphMethod> methods = graph.Methods.ToDictionary(method => method.Identity, StringComparer.Ordinal);
        foreach (WarpPortableTypedMethod method in program.Methods)
        {
            WarpPortableMethodGraphMethod original = methods[method.Identity];
            foreach (WarpPortableTypedInstruction instruction in method.Instructions.Where(instruction => instruction.Reachable &&
                         instruction.RequiredIntrinsic?.Contains("math.strict.", StringComparison.Ordinal) == true))
            {
                WarpPortableMethodGraphInstruction source = original.Instructions.First(item => item.Offset == instruction.Offset);
                if (source.Method is null || methods[source.Method].SourceMethod is not MethodInfo target || WarpPortableWordMathCatalog.Resolve(target) is not { } binding)
                {
                    throw new WarpVerificationException("WRPCLR2420", "The exact source Math signature has no portable operation/fault catalog.", instruction.Offset);
                }
                WarpPortableTypedFault? effect = instruction.Faults.FirstOrDefault(fault => fault.Kind == WarpPortableTypedFaultKind.CalledException);
                if (!binding.Faults.IsEmpty && effect is null)
                {
                    throw new WarpVerificationException("WRPCLR2420", "The source Math fault has no ordered captured call effect.", instruction.Offset);
                }
                foreach (WarpPortableWordMathFault fault in binding.Faults)
                {
                    (string? key, string? param) = MathResource(target, fault.Descriptor);
                    if (key is null) { continue; }
                    Type exception = MathExceptionType(fault.ExceptionType, instruction.Offset);
                    rows.Add(new((uint)rows.Count + 1, method.Identity, instruction.Offset, instruction.OpCode, effect!.EffectIndex,
                        WarpPortableTypedFaultKind.CalledException, schema.TypeId(WarpPortableMethodGraphIdentity.Type(exception)), binding.Identity, fault.Descriptor, key, param));
                }
            }
        }
    }

    private static Type MathExceptionType(string name, int offset) => name switch
    {
        "System.OverflowException" => typeof(OverflowException),
        "System.ArithmeticException" => typeof(ArithmeticException),
        "System.ArgumentException" => typeof(ArgumentException),
        "System.ArgumentOutOfRangeException" => typeof(ArgumentOutOfRangeException),
        _ => throw new WarpVerificationException("WRPCLR2420", "A portable Math fault requires its exact builtin dense exception schema type.", offset),
    };

    private static (string? Key, string? ParamName) MathResource(MethodInfo method, uint descriptor) => method.Name switch
    {
        "Abs" when descriptor == 2 => ("Overflow_NegateTwosCompNum", null),
        "Sign" when descriptor == 1 => ("Arithmetic_NaN", null),
        "Round" when descriptor == 2 => (method.DeclaringType == typeof(MathF) ? "ArgumentOutOfRange_RoundingDigits_MathF" : "ArgumentOutOfRange_RoundingDigits", "digits"),
        _ => (null, null),
    };
}
