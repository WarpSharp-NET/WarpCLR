using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static class WarpWordArenaServiceLowerer
{
    internal const string Semantics = "warp.portable-runtime.word-arena-cil/0.1";

    internal static WarpLogicalMachineLayout Lower(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Count(parameter => parameter.ParameterType == typeof(uint[])) != 1)
        {
            throw new ArgumentException("An ordinary portable word service binds exactly one context arena.", nameof(method));
        }

        int inputs = Math.Max(1, parameters.Length - 1);
        WarpIntegerMapKernel verified = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(method, inputs, wordArena: true));
        WarpControlFlowKernel body = verified.ControlFlow;
        var identified = new WarpControlFlowKernel(body.Name + "/" + Semantics,
            body.InputBufferCount, body.ScalarArgumentCount, body.Blocks, body.Reduction, body.Functions);
        return new WarpLogicalMachineLayout(identified);
    }
}
