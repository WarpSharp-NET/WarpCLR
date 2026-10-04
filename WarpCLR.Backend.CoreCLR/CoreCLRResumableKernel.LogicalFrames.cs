using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed partial class CoreCLRResumableKernel
{
    private sealed partial class QuantumEmitter
    {
        private void EmitPrivateInstruction(WarpIrInstruction instruction)
        {
            int offset = checked(layout.PrivateOffset + (int)instruction.Immediate);
            if (instruction.OpCode == WarpManagedFrameOpCode.StorePrivateWord)
            {
                StoreFrame(frame, offset, () => LoadValue(instruction.Left));
            }
            LoadFrame(frame, offset);
        }

        private void LoadPhysicalCapacity()
        {
            il.Emit(OpCodes.Ldarg_S, (byte)4);
            Constant(layout.Kernel.HelperExpansionFactor);
            il.Emit(OpCodes.Mul);
            if (layout.IsRuntimeHelper(0))
            {
                Constant(1);
                il.Emit(OpCodes.Add);
            }
        }

        private void EmitCallCapacity(WarpLogicalMachineNode node, WarpIrInstruction call)
        {
            Label physical = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, depth);
            LoadPhysicalCapacity();
            il.Emit(OpCodes.Blt, physical);
            EmitFault(node, WarpLogicalMachineLayout.CallDepthFault);
            il.MarkLabel(physical);
            if (layout.HasLogicalAccounting && !layout.IsRuntimeHelper(call.Callee + 1))
            {
                Label logical = il.DefineLabel();
                LoadState(WarpLogicalMachineLayout.LogicalDepthOffset);
                il.Emit(OpCodes.Ldarg_S, (byte)4);
                il.Emit(OpCodes.Blt_Un, logical);
                EmitFault(node, WarpLogicalMachineLayout.CallDepthFault);
                il.MarkLabel(logical);
            }
        }

        private void EmitClearPrivate(int function)
        {
            int count = layout.GetPrivateWordCount(function);
            if (count == 0) { return; }
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldloc, nextFrame);
            Constant(layout.PrivateOffset);
            il.Emit(OpCodes.Add);
            Constant(count);
            il.Emit(OpCodes.Call, typeof(Array).GetMethod(nameof(Array.Clear), [typeof(Array), typeof(int), typeof(int)])!);
        }

        private void ChangeLogicalDepth(int change) => StoreState(WarpLogicalMachineLayout.LogicalDepthOffset, () =>
        {
            LoadState(WarpLogicalMachineLayout.LogicalDepthOffset);
            Constant(Math.Abs(change));
            il.Emit(change > 0 ? OpCodes.Add : OpCodes.Sub);
        });

        private void EmitOperationalProgress(WarpLogicalMachineNode node)
        {
            if (!layout.HasLogicalAccounting) { return; }
            LoadState(WarpLogicalMachineLayout.UsedOperationsLowOffset);
            il.Emit(OpCodes.Conv_U8);
            LoadState(WarpLogicalMachineLayout.UsedOperationsHighOffset);
            il.Emit(OpCodes.Conv_U8);
            Constant(32);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Stloc, usedOperations);
            il.Emit(OpCodes.Ldloc, usedOperations);
            Constant(node.BlockCost);
            il.Emit(OpCodes.Conv_U8);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, nextOperations);
            Label admitted = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, nextOperations);
            il.Emit(OpCodes.Ldloc, usedOperations);
            il.Emit(OpCodes.Bge_Un, admitted);
            EmitFault(node, WarpLogicalMachineLayout.OperationalOverflowFault);
            il.MarkLabel(admitted);
            StoreState(WarpLogicalMachineLayout.UsedOperationsLowOffset, () =>
            {
                il.Emit(OpCodes.Ldloc, nextOperations);
                il.Emit(OpCodes.Conv_U4);
            });
            StoreState(WarpLogicalMachineLayout.UsedOperationsHighOffset, () =>
            {
                il.Emit(OpCodes.Ldloc, nextOperations);
                Constant(32);
                il.Emit(OpCodes.Shr_Un);
                il.Emit(OpCodes.Conv_U4);
            });
        }
    }
}
