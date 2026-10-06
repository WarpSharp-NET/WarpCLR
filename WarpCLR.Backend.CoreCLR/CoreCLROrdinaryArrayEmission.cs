using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

// Emits a denial/lifetime guard. Possessing an admission never grants Source execution.
internal sealed class CoreCLROrdinaryArrayEmission
{
    private static readonly MethodInfo AcquireMethod = typeof(WarpOrdinaryArrayAdmission)
        .GetMethod(nameof(WarpOrdinaryArrayAdmission.Acquire))!;
    private static readonly MethodInfo InputMethod = typeof(WarpOrdinaryArrayAdmission)
        .GetMethod(nameof(WarpOrdinaryArrayAdmission.GetInput))!;
    private static readonly MethodInfo DisposeMethod = typeof(WarpOrdinaryArrayAdmission)
        .GetMethod(nameof(WarpOrdinaryArrayAdmission.Dispose))!;
    private static readonly MethodInfo EmptyWordsMethod = typeof(Array)
        .GetMethod(nameof(Array.Empty))!.MakeGenericMethod(typeof(uint));
    private readonly ILGenerator il;
    private readonly LocalBuilder admission;
    private Label exit;

    internal CoreCLROrdinaryArrayEmission(ILGenerator il)
    {
        this.il = il;
        admission = il.DeclareLocal(typeof(WarpOrdinaryArrayAdmission));
    }

    internal static uint[][] CaptureInputReferences(WarpOrdinaryArrayAdmission admission, int count)
    {
        var captured = new uint[count][];
        for (int index = 0; index < count; index++) { captured[index] = admission.GetInput(index); }
        return captured;
    }

    internal void Begin(int scalarArgument, int? stateArgument = null, int? arenaArgument = null)
    {
        il.Emit(OpCodes.Ldarg_0);
        LoadArgument(scalarArgument);
        LoadBank(stateArgument);
        LoadBank(arenaArgument);
        il.Emit(OpCodes.Call, AcquireMethod);
        il.Emit(OpCodes.Stloc, admission);
        // Acquire owns nothing on failure. Every path after success enters this protected region.
        exit = il.BeginExceptionBlock();
    }

    internal void LoadInput(int index)
    {
        il.Emit(OpCodes.Ldloc, admission);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Callvirt, InputMethod);
    }

    internal void Return() => il.Emit(OpCodes.Leave, exit);

    internal void End()
    {
        Return();
        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldloc, admission);
        il.Emit(OpCodes.Callvirt, DisposeMethod);
        il.EndExceptionBlock();
    }

    private void LoadBank(int? argument)
    {
        if (argument is int index) { LoadArgument(index); }
        else { il.Emit(OpCodes.Call, EmptyWordsMethod); }
    }

    private void LoadArgument(int index) => il.Emit(OpCodes.Ldarg, checked((short)index));
}
