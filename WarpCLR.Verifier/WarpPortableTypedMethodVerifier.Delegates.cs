using System.Reflection;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private void ValidateDelegate(WarpPortableMethodGraphMethod constructor, WarpPortableTypedValue[] arguments, int offset)
    {
        Require(arguments.Length == 2 && arguments[1].Category == WarpPortableStackCategory.FunctionTarget && arguments[1].MethodTarget is not null,
            "Delegate construction requires a closed verified method target.", offset);
        WarpPortableMethodGraphMethod target = methods[arguments[1].MethodTarget!];
        MethodInfo invoke = constructor.SourceMethod.DeclaringType!.GetMethod("Invoke")!;
        ParameterInfo[] expected = invoke.GetParameters();
        Require(target.ParameterTypes.Length == expected.Length, "A delegate target changes parameter count.", offset);
        for (int index = 0; index < expected.Length; index++)
        {
            string identity = types.Get(expected[index].ParameterType).Identity;
            Require(string.Equals(identity, target.ParameterTypes[index], StringComparison.Ordinal), "Delegate target parameter type differs from its exact signature.", offset);
        }

        Require(string.Equals(types.Get(invoke.ReturnType).Identity, target.ReturnType, StringComparison.Ordinal), "Delegate target return type differs from its signature.", offset);
        if (target.SourceMethod.IsStatic) { Require(arguments[0].IsNull, "An open static delegate requires a null bound receiver.", offset); }
        else
        {
            Require(arguments[0].Category == WarpPortableStackCategory.Reference && !arguments[0].IsNull &&
                target.SourceMethod.DeclaringType!.IsAssignableFrom(types.Source(arguments[0].TypeIdentity)), "Delegate receiver has an incompatible managed class.", offset);
        }
    }

    private static void CallEffects(WarpPortableMethodGraphMethod target, Step step, bool construction)
    {
        if (construction && !target.SourceMethod.DeclaringType!.IsValueType)
        {
            step.Effects.Add(WarpPortableTypedEffect.Allocate);
        }

        string? intrinsic = target.Intrinsic;
        if (intrinsic?.Contains("memory.atomic.sc.", StringComparison.Ordinal) == true) { step.Effects.Add(WarpPortableTypedEffect.AtomicSequential); }
        if (intrinsic?.Contains("memory.volatile.acquire-release.Read", StringComparison.Ordinal) == true) { step.Effects.Add(WarpPortableTypedEffect.Acquire); }
        if (intrinsic?.Contains("memory.volatile.acquire-release.Write", StringComparison.Ordinal) == true) { step.Effects.Add(WarpPortableTypedEffect.Release); }
        step.Effects.Add(WarpPortableTypedEffect.Call); step.Effects.Add(WarpPortableTypedEffect.Safepoint);
    }
}
