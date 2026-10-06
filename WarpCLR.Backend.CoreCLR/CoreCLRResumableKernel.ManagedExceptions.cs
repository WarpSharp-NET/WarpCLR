using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed partial class CoreCLRResumableKernel
{
    private sealed partial class QuantumEmitter
    {
        private void EmitManagedException(WarpLogicalMachineNode node, WarpManagedExceptionTerminator managed)
        {
            Label invalid = il.DefineLabel();
            foreach (int value in new[] { managed.Context, managed.ObjectId, managed.Generation })
            {
                LoadValue(value);
                il.Emit(OpCodes.Brfalse, invalid);
            }
            StoreState(WarpLogicalMachineLayout.EscapedExceptionContextOffset, () => LoadValue(managed.Context));
            StoreState(WarpLogicalMachineLayout.EscapedExceptionObjectOffset, () => LoadValue(managed.ObjectId));
            StoreState(WarpLogicalMachineLayout.EscapedExceptionGenerationOffset, () => LoadValue(managed.Generation));
            StoreState(WarpLogicalMachineLayout.SourceBoundaryStateOffset, () => Constant(0));
            StoreState(WarpLogicalMachineLayout.FaultKindOffset, () => Constant((int)WarpLogicalMachineLayout.ManagedExceptionFault));
            StoreState(WarpLogicalMachineLayout.FaultFunctionOffset, () => Constant(node.Function));
            StoreState(WarpLogicalMachineLayout.FaultBlockOffset, () => Constant(node.Block));
            StoreState(WarpLogicalMachineLayout.StatusOffset, () => Constant((int)WarpLogicalMachineLayout.Faulted));
            EmitQuantumReturn();
            il.MarkLabel(invalid);
            EmitFault(node, 3);
        }
    }
}
