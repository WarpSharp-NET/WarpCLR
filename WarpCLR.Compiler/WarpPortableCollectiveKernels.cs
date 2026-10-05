using System.Collections.ObjectModel;
using System.Reflection;
using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpPortableCollectiveKernels
{
    internal static ReadOnlyCollection<WarpLogicalMachineLayout> CreateServices()
    {
        MethodInfo[] methods = typeof(WarpPortableCollectiveServices).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Array.Sort(methods, static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return Array.AsReadOnly(methods.Select(Lower).ToArray());
    }

    internal static WarpLogicalMachineLayout Lower(MethodInfo method)
    {
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(method);
        WarpControlFlowKernel body = layout.Kernel;
        var identified = new WarpControlFlowKernel(body.Name + "/" + WarpPortableCollectiveLayout.Semantics + "/" +
            WarpPortableCollectiveLayout.BindingSemantics + "/" + WarpPortableCollectiveLayout.SchedulerSemantics + "/" +
            WarpPortableCollectiveLayout.HeapSemantics + "/" +
            WarpPortableCollectiveArithmetic.Semantics + "/" + WarpPortableBinary32.Semantics + "/" +
            WarpPortableBinary64.Semantics + "/" + WarpPortableNumericComparisons.Semantics + "/" +
            WarpPortableInteger32.Semantics + "/" + WarpPortableInteger64.Semantics,
            body.InputBufferCount, body.ScalarArgumentCount, body.Blocks, body.Reduction, body.Functions);
        return new WarpLogicalMachineLayout(identified);
    }
}
