using System.Collections.ObjectModel;
using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static class WarpPortableIntrinsicKernels
{
    public static IReadOnlyList<WarpLogicalMachineLayout> CreateBinary32Intrinsics() =>
        Create(typeof(WarpPortableBinary32Intrinsics), WarpPortableBinary32Intrinsics.Semantics);

    public static IReadOnlyList<WarpLogicalMachineLayout> CreateBinary64Intrinsics() =>
        Create(typeof(WarpPortableBinary64Intrinsics), WarpPortableBinary64Intrinsics.Semantics);

    private static ReadOnlyCollection<WarpLogicalMachineLayout> Create(Type implementation, string semantics)
    {
        var layouts = new List<WarpLogicalMachineLayout>();
        foreach (MethodInfo method in implementation.GetMethods(BindingFlags.Public | BindingFlags.Static).OrderBy(value => value.Name, StringComparer.Ordinal))
        {
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
