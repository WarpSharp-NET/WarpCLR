using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed partial class CoreCLRResumableKernel
{
    private sealed partial class QuantumEmitter
    {
        private bool TryEmitFrameInstruction(WarpIrInstruction instruction)
        {
            if (WarpManagedFrameOpCode.IsOwner(instruction.OpCode)) { EmitFrameOwner(instruction); }
            else if (WarpManagedStateOpCode.IsState(instruction.OpCode)) { EmitStateInstruction(instruction); }
            else if (WarpManagedFrameOpCode.IsPrivate(instruction.OpCode)) { EmitPrivateInstruction(instruction); }
            else { return false; }
            return true;
        }

        private void EmitSourceBoundary(WarpLogicalMachineNode node)
        {
            if (layout.IsPrivateHelperBoundary(node)) { EmitPrivateHelperBoundary(); return; }
            if (!layout.HasLogicalAccounting || node.SourceCost == 0 || layout.IsRuntimeHelper(node.Function)) { return; }
            Label execute = il.DefineLabel();
            LoadState(WarpLogicalMachineLayout.SourceBoundaryModeOffset);
            il.Emit(OpCodes.Brfalse, execute);
            Label acknowledged = il.DefineLabel();
            LoadState(WarpLogicalMachineLayout.SourceBoundaryStateOffset);
            Constant((int)WarpLogicalMachineLayout.AcknowledgedSourceBoundary);
            il.Emit(OpCodes.Beq, acknowledged);
            StoreState(WarpLogicalMachineLayout.SourceBoundaryStateOffset, () => Constant((int)WarpLogicalMachineLayout.BeforeSourceBoundary));
            EmitQuantumReturn();
            il.MarkLabel(acknowledged);
            StoreState(WarpLogicalMachineLayout.SourceBoundaryStateOffset, () => Constant(0));
            il.MarkLabel(execute);
        }

        private void EmitFrameIdentity(WarpLogicalMachineNode node, int function)
        {
            StoreFrame(nextFrame, WarpLogicalMachineLayout.FramePrivateWordsOffset, () => Constant(layout.GetPrivateWordCount(function)));
            StoreFrame(nextFrame, WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset, () => Constant(0));
            StoreFrame(nextFrame, WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset, () => Constant(0));
            if (!layout.HasFrameOwners) { return; }
            Label admitted = il.DefineLabel();
            LoadState(WarpLogicalMachineLayout.NextActivationOffset);
            Constant(unchecked((int)uint.MaxValue));
            il.Emit(OpCodes.Bne_Un, admitted);
            EmitFault(node, WarpLogicalMachineLayout.ActivationExhaustionFault);
            il.MarkLabel(admitted);
            StoreState(WarpLogicalMachineLayout.NextActivationOffset, () =>
            {
                LoadState(WarpLogicalMachineLayout.NextActivationOffset);
                Constant(1);
                il.Emit(OpCodes.Add);
            });
            StoreFrame(nextFrame, WarpLogicalMachineLayout.FrameActivationOffset, () => LoadState(WarpLogicalMachineLayout.NextActivationOffset));
        }

        private void EmitFrameOwner(WarpIrInstruction instruction)
        {
            if (instruction.OpCode == WarpManagedFrameOpCode.OwnerContext) { LoadState(WarpLogicalMachineLayout.OwnerContextOffset); }
            else if (instruction.OpCode == WarpManagedFrameOpCode.OwnerFrame)
            {
                if (layout.GetAliasOwnerFunction(emittedFunction) == -1) { il.Emit(OpCodes.Ldloc, depth); }
                else { LoadFrame(frame, WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset); }
            }
            else
            {
                LoadFrame(frame, layout.GetAliasOwnerFunction(emittedFunction) == -1 ?
                    WarpLogicalMachineLayout.FrameActivationOffset : WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset);
            }
        }

        private void EmitStateInstruction(WarpIrInstruction instruction)
        {
            if (instruction.OpCode == WarpManagedStateOpCode.WordAddress) { LoadValue(instruction.Left); return; }
            il.Emit(OpCodes.Ldarg_3);
            if (instruction.OpCode == WarpManagedStateOpCode.WordCount)
            {
                il.Emit(OpCodes.Ldlen);
                il.Emit(OpCodes.Conv_U4);
                return;
            }
            LoadValue(instruction.Left);
            if (instruction.OpCode == WarpManagedStateOpCode.LoadWord) { il.Emit(OpCodes.Ldelem_U4); return; }
            LoadValue(instruction.Right);
            il.Emit(OpCodes.Stelem_I4);
            LoadValue(instruction.Right);
        }

        private void EmitStateBoundsCheck(WarpIrInstruction instruction, WarpLogicalMachineNode node)
        {
            Label admitted = il.DefineLabel();
            LoadValue(instruction.Left);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_U4);
            il.Emit(OpCodes.Blt_Un, admitted);
            EmitFault(node, WarpLogicalMachineLayout.ManagedMemoryBoundsFault);
            il.MarkLabel(admitted);
        }
    }
}
