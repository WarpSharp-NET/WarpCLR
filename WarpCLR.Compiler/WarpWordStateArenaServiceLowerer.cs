using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static class WarpWordStateArenaServiceLowerer
{
    internal const string Semantics = "warp.portable-runtime.word-state-arena-cil/0.1";

    internal static WarpLogicalMachineLayout Lower(MethodInfo method,
        IReadOnlyDictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>> bindings)
    {
        ArgumentNullException.ThrowIfNull(method);
        var owned = new WarpWordBankBindings(bindings);
        ParameterInfo[] parameters = method.GetParameters();
        System.Collections.Immutable.ImmutableArray<bool> state = owned.GetStateParameters(method);
        if (!state.Any(value => value) || !parameters.Where((parameter, index) =>
            parameter.ParameterType == typeof(uint[]) && !state[index]).Any())
        {
            throw new ArgumentException("A mixed runtime service must bind its executing state and persistent arena explicitly.", nameof(bindings));
        }
        int inputs = Math.Max(1, parameters.Count(parameter => parameter.ParameterType != typeof(uint[])));
        WarpControlFlowKernel body = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(
            method, inputs, wordArena: true, owned)).ControlFlow;
        return new WarpLogicalMachineLayout(new WarpControlFlowKernel(body.Name + "/" + Semantics + "/" + owned.Identity,
            body.InputBufferCount, body.ScalarArgumentCount, body.Blocks, body.Reduction, body.Functions, body.Execution));
    }
}
