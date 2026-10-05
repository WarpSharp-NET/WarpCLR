using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static class WarpPortableAtomicWordKernels
{
    internal const string Semantics = "warp.source-byte-atomics/controller-sc-single-event-width4-8/0.1";

    internal static WarpLogicalMachineLayout Arena() => Lower(useState: false);

    internal static WarpLogicalMachineLayout State() => Lower(useState: true);

    private static WarpLogicalMachineLayout Lower(bool useState)
    {
        var bindings = new Dictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>>();
        foreach (Type type in new[] { typeof(WarpPortableAtomicWordServices), typeof(WarpPortableSchedulerServices) })
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            bindings[method] = method.GetParameters().Select(parameter => parameter.ParameterType != typeof(uint[]) ? WarpRuntimeWordBank.Word :
                useState && !string.Equals(parameter.Name, "arena", StringComparison.Ordinal) ? WarpRuntimeWordBank.State : WarpRuntimeWordBank.Arena).ToArray();
        }
        MethodInfo entry = Method(useState ? nameof(WarpPortableAtomicWordServices.OperateState) : nameof(WarpPortableAtomicWordServices.OperateArena));
        var owned = new WarpWordBankBindings(bindings);
        int inputs = entry.GetParameters().Count(parameter => parameter.ParameterType == typeof(uint));
        WarpControlFlowKernel body = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(entry, inputs, wordArena: true, owned)).ControlFlow;
        return new(new WarpControlFlowKernel(body.Name + "/" + Semantics + "/" + owned.Identity, body.InputBufferCount,
            body.ScalarArgumentCount, body.Blocks, body.Reduction, body.Functions, body.Execution));
    }

    private static MethodInfo Method(string name) => typeof(WarpPortableAtomicWordServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
}
