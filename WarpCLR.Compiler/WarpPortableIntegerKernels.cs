using System.Collections.ObjectModel;
using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static class WarpPortableIntegerKernels
{
    public static IReadOnlyList<WarpLogicalMachineLayout> Create32() => Create(typeof(WarpPortableInteger32), WarpPortableInteger32.Semantics);

    public static IReadOnlyList<WarpLogicalMachineLayout> Create64() => Create(typeof(WarpPortableInteger64), WarpPortableInteger64.Semantics);

    private static ReadOnlyCollection<WarpLogicalMachineLayout> Create(Type implementation, string semantics)
    {
        MethodInfo[] methods = implementation.GetMethods(BindingFlags.Public | BindingFlags.Static);
        Array.Sort(methods, (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        var layouts = new List<WarpLogicalMachineLayout>(methods.Length);
        foreach (MethodInfo method in methods)
        {
            WarpIntegerMapKernel verified = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(method, method.GetParameters().Length));
            WarpControlFlowKernel body = verified.ControlFlow;
            var identified = new WarpControlFlowKernel(body.Name + "/" + semantics,
                body.InputBufferCount, body.ScalarArgumentCount, body.Blocks, body.Reduction, body.Functions);
            layouts.Add(new WarpLogicalMachineLayout(identified));
        }

        return layouts.AsReadOnly();
    }
}
