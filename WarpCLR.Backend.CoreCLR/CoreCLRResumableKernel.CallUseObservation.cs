using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed partial class CoreCLRResumableKernel
{
    private sealed partial class QuantumEmitter
    {
        private static readonly MethodInfo RecordCallUse = typeof(CoreCLRCallUseObservation)
            .GetMethod(nameof(CoreCLRCallUseObservation.Record))!;

        private void EmitCallUseObservation(WarpLogicalMachineNode node)
        {
            // This token belongs to this actual emission and this exact call node.
            // A PC, callee number or equal-valued metadata object is not the token.
            il.Emit(OpCodes.Ldsfld, callUseSites);
            Constant(node.ProgramCounter);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldarg_S, (byte)7);
            il.Emit(OpCodes.Ldloc, frame);
            il.Emit(OpCodes.Call, RecordCallUse);
        }
    }
}
