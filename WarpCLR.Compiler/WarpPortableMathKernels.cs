using System.Collections.ObjectModel;
using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static class WarpPortableMathKernels
{
    public static IReadOnlyList<WarpLogicalMachineLayout> CreateBinary32Math() =>
        Create(typeof(WarpPortableBinary32Math), WarpPortableBinary32Math.Semantics,
        [
            nameof(WarpPortableBinary32Math.Sqrt),
            nameof(WarpPortableBinary32Math.Remainder),
            nameof(WarpPortableBinary32Math.IeeeRemainder),
            nameof(WarpPortableBinary32Math.FusedMultiplyAdd),
        ]);

    public static IReadOnlyList<WarpLogicalMachineLayout> CreateBinary64Math() =>
        Create(typeof(WarpPortableBinary64Math), WarpPortableBinary64Math.Semantics,
        [
            nameof(WarpPortableBinary64Math.SqrtLow),
            nameof(WarpPortableBinary64Math.SqrtHigh),
            nameof(WarpPortableBinary64Math.RemainderLow),
            nameof(WarpPortableBinary64Math.RemainderHigh),
            nameof(WarpPortableBinary64Math.IeeeRemainderLow),
            nameof(WarpPortableBinary64Math.IeeeRemainderHigh),
            nameof(WarpPortableBinary64Math.FusedMultiplyAddLow),
            nameof(WarpPortableBinary64Math.FusedMultiplyAddHigh),
        ]);

    private static ReadOnlyCollection<WarpLogicalMachineLayout> Create(Type implementation, string semantics, string[] methods)
    {
        var layouts = new List<WarpLogicalMachineLayout>(methods.Length);
        foreach (string name in methods)
        {
            MethodInfo method = implementation.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("A portable math primitive is missing.");
            int inputs = method.GetParameters().Length;
            WarpIntegerMapKernel verified = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(method, inputs));
            WarpControlFlowKernel body = verified.ControlFlow;
            var identified = new WarpControlFlowKernel(body.Name + "/" + semantics,
                body.InputBufferCount, body.ScalarArgumentCount, body.Blocks, body.Reduction, body.Functions);
            layouts.Add(new WarpLogicalMachineLayout(identified));
        }

        return layouts.AsReadOnly();
    }
}
