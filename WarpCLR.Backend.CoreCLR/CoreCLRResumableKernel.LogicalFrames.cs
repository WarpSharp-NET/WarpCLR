using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed partial class CoreCLRResumableKernel
{
    private sealed partial class QuantumEmitter
    {
        private void EmitStateDispatch(WarpLogicalMachineNode node, WarpStateDispatchTerminator dispatch)
        {
            Label runnable = il.DefineLabel();
            Label invalid = il.DefineLabel();
            Label terminal = il.DefineLabel();
            EmitStateDispatchStatus(runnable, invalid, terminal);
            il.MarkLabel(runnable);
            LoadState(WarpLogicalMachineLayout.DepthOffset);
            Constant(1);
            il.Emit(OpCodes.Blt_Un, invalid);
            LoadState(WarpLogicalMachineLayout.DepthOffset);
            LoadPhysicalCapacity();
            il.Emit(OpCodes.Bgt_Un, invalid);
            LoadState(WarpLogicalMachineLayout.LogicalDepthOffset);
            il.Emit(OpCodes.Ldarg_S, (byte)4);
            il.Emit(OpCodes.Bgt_Un, invalid);
            LoadState(WarpLogicalMachineLayout.DepthOffset);
            Constant(1);
            il.Emit(OpCodes.Sub);
            Constant(layout.FrameWords);
            il.Emit(OpCodes.Mul);
            Constant(WarpLogicalMachineLayout.HeaderWords);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, nextFrame);
            LoadFrame(nextFrame, WarpLogicalMachineLayout.FrameActivationOffset);
            il.Emit(OpCodes.Brfalse, invalid);
            foreach (WarpStateDispatchTarget destination in dispatch.Destinations)
            {
                Label next = il.DefineLabel();
                LoadFrame(nextFrame, WarpLogicalMachineLayout.FrameFunctionOffset);
                Constant(destination.Function);
                il.Emit(OpCodes.Bne_Un, next);
                LoadFrame(nextFrame, WarpLogicalMachineLayout.FrameProgramCounterOffset);
                Constant(layout.GetBlockEntry(destination.Function, destination.Block));
                il.Emit(OpCodes.Beq, loop);
                il.MarkLabel(next);
            }
            il.MarkLabel(invalid);
            EmitFault(node, 3);
            il.MarkLabel(terminal);
            il.Emit(OpCodes.Ret);
        }

        private void EmitStateDispatchStatus(Label runnable, Label invalid, Label terminal)
        {
            LoadState(WarpLogicalMachineLayout.StatusOffset);
            Constant((int)WarpLogicalMachineLayout.Runnable);
            il.Emit(OpCodes.Beq, runnable);
            LoadState(WarpLogicalMachineLayout.StatusOffset);
            Constant((int)WarpLogicalMachineLayout.Completed);
            il.Emit(OpCodes.Beq, terminal);
            LoadState(WarpLogicalMachineLayout.StatusOffset);
            Constant((int)WarpLogicalMachineLayout.Faulted);
            il.Emit(OpCodes.Beq, terminal);
            il.Emit(OpCodes.Br, invalid);
        }

        private void EmitPrivateInstruction(WarpIrInstruction instruction)
        {
            int offset = checked(layout.PrivateOffset + (int)instruction.Immediate);
            LocalBuilder privateFrame = instruction.Immediate < layout.GetAliasPrefixWords(emittedFunction) ? aliasOwnerFrame : frame;
            if (instruction.OpCode == WarpManagedFrameOpCode.StorePrivateWord)
            {
                StoreFrame(privateFrame, offset, () => LoadValue(instruction.Left));
            }
            LoadFrame(privateFrame, offset);
        }

        private void LoadPhysicalCapacity()
        {
            il.Emit(OpCodes.Ldarg_S, (byte)4);
            Constant(layout.Kernel.HelperExpansionFactor);
            il.Emit(OpCodes.Mul);
            if (!layout.CountsSourceDepth(0))
            {
                Constant(1);
                il.Emit(OpCodes.Add);
            }
        }

        private void EmitCallCapacity(WarpLogicalMachineNode node, WarpIrInstruction call)
        {
            if (layout.HasLogicalAccounting && layout.CountsSourceDepth(call.Callee + 1))
            {
                Label logical = il.DefineLabel();
                LoadState(WarpLogicalMachineLayout.LogicalDepthOffset);
                il.Emit(OpCodes.Ldarg_S, (byte)4);
                il.Emit(OpCodes.Blt_Un, logical);
                EmitFault(node, WarpLogicalMachineLayout.CallDepthFault);
                il.MarkLabel(logical);
            }
            Label physical = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, depth);
            LoadPhysicalCapacity();
            il.Emit(OpCodes.Blt, physical);
            EmitFault(node, layout.HasLogicalAccounting ? WarpLogicalMachineLayout.PhysicalFrameCapacityFault : WarpLogicalMachineLayout.CallDepthFault);
            il.MarkLabel(physical);
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
