using System.Collections.ObjectModel;
using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static class WarpPortableNumericKernels
{
    public static IReadOnlyList<WarpLogicalMachineLayout> CreateBinary64Arithmetic() =>
        Create(typeof(WarpPortableBinary64), 4, WarpPortableBinary64.Semantics,
        [
            nameof(WarpPortableBinary64.AddLow),
            nameof(WarpPortableBinary64.AddHigh),
            nameof(WarpPortableBinary64.SubtractLow),
            nameof(WarpPortableBinary64.SubtractHigh),
            nameof(WarpPortableBinary64.MultiplyLow),
            nameof(WarpPortableBinary64.MultiplyHigh),
            nameof(WarpPortableBinary64.DivideLow),
            nameof(WarpPortableBinary64.DivideHigh),
        ]);

    public static IReadOnlyList<WarpLogicalMachineLayout> CreateBinary32Arithmetic() =>
        Create(typeof(WarpPortableBinary32), 2, WarpPortableBinary32.Semantics,
        [
            nameof(WarpPortableBinary32.Add),
            nameof(WarpPortableBinary32.Subtract),
            nameof(WarpPortableBinary32.Multiply),
            nameof(WarpPortableBinary32.Divide),
        ]);

    private static ReadOnlyCollection<WarpLogicalMachineLayout> Create(Type implementation, int inputBufferCount,
        string semantics, string[] methods)
    {
        var layouts = new List<WarpLogicalMachineLayout>(methods.Length);
        foreach (string name in methods)
        {
            MethodInfo method = implementation.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("A portable numeric primitive is missing.");
            WarpIntegerMapKernel verified = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(method, inputBufferCount));
            WarpControlFlowKernel body = verified.ControlFlow;
            var identified = new WarpControlFlowKernel(body.Name + "/" + semantics,
                body.InputBufferCount, body.ScalarArgumentCount, body.Blocks, body.Reduction, body.Functions);
            layouts.Add(new WarpLogicalMachineLayout(identified));
        }

        return layouts.AsReadOnly();
    }
}
