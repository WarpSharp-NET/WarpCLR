using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed partial class CoreCLRResumableKernel
{
    private sealed partial class QuantumEmitter
    {
        private void EmitPrivateHelperDispatchGuard()
        {
            EmitPrivateHelperScopeGuard();
            Label admitted = il.DefineLabel();
            Label invalid = il.DefineLabel();
            LoadState(WarpLogicalMachineLayout.SourceBoundaryStateOffset);
            Constant((int)WarpLogicalMachineLayout.NeedsPrivateHelper);
            il.Emit(OpCodes.Blt_Un, admitted);
            EmitPrivateHeaderGuard(invalid);
            EmitPrivateDepthGuard(invalid);
            foreach (WarpLogicalMachineNode node in layout.PrivateHelperDispatchNodes)
            {
                Label next = il.DefineLabel();
                LoadFrame(frame, WarpLogicalMachineLayout.FrameProgramCounterOffset);
                Constant(node.ProgramCounter);
                il.Emit(OpCodes.Bne_Un, next);
                EmitPrivateSitePhaseGuard(node, invalid);
                EmitPrivateFrameGuard(node.Function, invalid);
                il.Emit(OpCodes.Br, admitted);
                il.MarkLabel(next);
            }
            il.MarkLabel(invalid);
            EmitQuantumReturn();
            il.MarkLabel(admitted);
            EmitPrivateReturnDispatchPhase();
        }

        private void EmitPrivateHeaderGuard(Label invalid)
        {
            LoadState(WarpLogicalMachineLayout.SourceBoundaryStateOffset);
            Constant((int)layout.MaximumSourceBoundaryPhase);
            il.Emit(OpCodes.Bgt_Un, invalid);
            foreach ((int offset, int expected) in new (int, int)[]
            {
                (WarpLogicalMachineLayout.SourceBoundaryModeOffset, 1),
                (WarpLogicalMachineLayout.StatusOffset, (int)WarpLogicalMachineLayout.Runnable),
                (WarpLogicalMachineLayout.FrameStrideOffset, layout.FrameWords),
                (WarpLogicalMachineLayout.PrivateBaseOffset, layout.PrivateOffset),
                (WarpLogicalMachineLayout.EscapedExceptionContextOffset, 0),
                (WarpLogicalMachineLayout.EscapedExceptionObjectOffset, 0),
                (WarpLogicalMachineLayout.EscapedExceptionGenerationOffset, 0),
            })
            {
                LoadState(offset); Constant(expected); il.Emit(OpCodes.Bne_Un, invalid);
            }
            LoadState(WarpLogicalMachineLayout.OwnerContextOffset); il.Emit(OpCodes.Brfalse, invalid);
            LoadState(WarpLogicalMachineLayout.NextActivationOffset); il.Emit(OpCodes.Brfalse, invalid);
            LoadState(WarpLogicalMachineLayout.FaultKindOffset);
            Constant((int)WarpLogicalMachineLayout.ManagedExceptionFault); il.Emit(OpCodes.Beq, invalid);
        }

        private void EmitPrivateDepthGuard(Label invalid)
        {
            LoadState(WarpLogicalMachineLayout.DepthOffset); il.Emit(OpCodes.Stloc, depth);
            il.Emit(OpCodes.Ldloc, depth); il.Emit(OpCodes.Brfalse, invalid);
            il.Emit(OpCodes.Ldloc, depth);
            il.Emit(OpCodes.Ldarg_S, (byte)4); Constant(layout.Kernel.HelperExpansionFactor); il.Emit(OpCodes.Mul);
            Constant(!layout.CountsSourceDepth(0) ? 1 : 0); il.Emit(OpCodes.Add); il.Emit(OpCodes.Bgt_Un, invalid);
            il.Emit(OpCodes.Ldloc, depth); il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4);
            Constant(WarpLogicalMachineLayout.HeaderWords + (layout.HasPrivateHelperReturnFences ? layout.ResultTailWords : 0)); il.Emit(OpCodes.Sub);
            Constant(layout.FrameWords); il.Emit(OpCodes.Div_Un); il.Emit(OpCodes.Bgt_Un, invalid);
            il.Emit(OpCodes.Ldloc, depth); Constant(1); il.Emit(OpCodes.Sub);
            Constant(layout.FrameWords); il.Emit(OpCodes.Mul);
            Constant(WarpLogicalMachineLayout.HeaderWords); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, frame);
        }

        private void EmitPrivateFrameGuard(int function, Label invalid)
        {
            LoadFrame(frame, WarpLogicalMachineLayout.FrameFunctionOffset); Constant(function); il.Emit(OpCodes.Bne_Un, invalid);
            LoadFrame(frame, WarpLogicalMachineLayout.FramePrivateWordsOffset);
            Constant(layout.GetPrivateWordCount(function)); il.Emit(OpCodes.Bne_Un, invalid);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameActivationOffset); il.Emit(OpCodes.Brfalse, invalid);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameActivationOffset);
            LoadState(WarpLogicalMachineLayout.NextActivationOffset); il.Emit(OpCodes.Bgt_Un, invalid);
            Label first = il.DefineLabel(); Label parentChecked = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, depth); Constant(1); il.Emit(OpCodes.Beq, first);
            il.Emit(OpCodes.Ldloc, frame); Constant(layout.FrameWords); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, aliasCursor);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameActivationOffset);
            LoadFrame(aliasCursor, WarpLogicalMachineLayout.FrameActivationOffset); il.Emit(OpCodes.Ble_Un, invalid);
            il.Emit(OpCodes.Br, parentChecked); il.MarkLabel(first); il.MarkLabel(parentChecked);
            int owner = layout.GetAliasOwnerFunction(function);
            if (owner == -1)
            {
                LoadFrame(frame, WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset); il.Emit(OpCodes.Brtrue, invalid);
                LoadFrame(frame, WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset); il.Emit(OpCodes.Brtrue, invalid);
                return;
            }
            EmitAliasDepthCheck(invalid); EmitAliasIdentityCheck(owner, invalid); EmitAliasUniquenessCheck(invalid);
        }

        private void EmitPrivateHelperBoundary()
        {
            Label execute = il.DefineLabel();
            LoadState(WarpLogicalMachineLayout.SourceBoundaryModeOffset);
            il.Emit(OpCodes.Brfalse, execute);
            Label park = il.DefineLabel();
            LoadState(WarpLogicalMachineLayout.SourceBoundaryStateOffset);
            Constant((int)WarpLogicalMachineLayout.AcknowledgedPrivateHelper);
            il.Emit(OpCodes.Bne_Un, park);
            il.Emit(OpCodes.Ldarg_1);
            Constant(0);
            il.Emit(OpCodes.Ldelem_U4);
            Label invalidWord = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, invalidWord);
            StoreState(WarpLogicalMachineLayout.SourceBoundaryStateOffset, () => Constant(0));
            il.Emit(OpCodes.Br, execute);
            il.MarkLabel(park);
            StoreState(WarpLogicalMachineLayout.SourceBoundaryStateOffset, () => Constant((int)WarpLogicalMachineLayout.NeedsPrivateHelper));
            EmitQuantumReturn();
            il.MarkLabel(invalidWord);
            EmitQuantumReturn();
            il.MarkLabel(execute);
        }
    }
}
