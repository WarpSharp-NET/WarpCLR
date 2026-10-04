using System.Collections.ObjectModel;
using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static class WarpPortableTranscendentalKernels
{
    public static IReadOnlyList<WarpLogicalMachineLayout> CreateBinary32Transcendentals() =>
        Create(typeof(WarpPortableBinary32Transcendentals), WarpPortableBinary32Transcendentals.Semantics);

    public static IReadOnlyList<WarpLogicalMachineLayout> CreateBinary64Transcendentals() =>
        Create(typeof(WarpPortableBinary64Transcendentals), WarpPortableBinary64Transcendentals.Semantics);

    public static IReadOnlyList<WarpLogicalMachineLayout> CreateClamp() =>
        Create(typeof(WarpPortableNumericClamp), WarpPortableNumericClamp.Semantics);

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
