using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed partial class CoreCLRResumableKernel
{
    private sealed partial class QuantumEmitter
    {
        private void EmitAliasGuard(WarpLogicalMachineNode node)
        {
            int owner = layout.GetAliasOwnerFunction(node.Function);
            if (owner == -1) { return; }
            Label invalid = il.DefineLabel();
            Label admitted = il.DefineLabel();
            EmitAliasDepthCheck(invalid);
            EmitAliasIdentityCheck(owner, invalid);
            EmitAliasUniquenessCheck(invalid);
            il.Emit(OpCodes.Br, admitted);
            il.MarkLabel(invalid);
            EmitFault(node, 3);
            il.MarkLabel(admitted);
        }

        private void EmitAliasDepthCheck(Label invalid)
        {
            LoadFrame(frame, WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset);
            il.Emit(OpCodes.Brfalse, invalid);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset);
            il.Emit(OpCodes.Ldloc, depth);
            il.Emit(OpCodes.Bge_Un, invalid);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset);
            Constant(1);
            il.Emit(OpCodes.Sub);
            Constant(layout.FrameWords);
            il.Emit(OpCodes.Mul);
            Constant(WarpLogicalMachineLayout.HeaderWords);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, aliasOwnerFrame);
        }

        private void EmitAliasIdentityCheck(int owner, Label invalid)
        {
            LoadFrame(aliasOwnerFrame, WarpLogicalMachineLayout.FrameFunctionOffset);
            Constant(owner);
            il.Emit(OpCodes.Bne_Un, invalid);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset);
            il.Emit(OpCodes.Brfalse, invalid);
            LoadFrame(aliasOwnerFrame, WarpLogicalMachineLayout.FrameActivationOffset);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset);
            il.Emit(OpCodes.Bne_Un, invalid);
            LoadFrame(aliasOwnerFrame, WarpLogicalMachineLayout.FramePrivateWordsOffset);
            Constant(layout.GetPrivateWordCount(owner));
            il.Emit(OpCodes.Bne_Un, invalid);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameReturnValueOffset);
            il.Emit(OpCodes.Brtrue, invalid);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameReturnWordCountOffset);
            il.Emit(OpCodes.Brtrue, invalid);
        }

        private void EmitAliasUniquenessCheck(Label invalid)
        {
            Label scan = il.DefineLabel();
            Label next = il.DefineLabel();
            Label done = il.DefineLabel();
            Constant(WarpLogicalMachineLayout.HeaderWords);
            il.Emit(OpCodes.Stloc, aliasCursor);
            il.MarkLabel(scan);
            il.Emit(OpCodes.Ldloc, aliasCursor);
            il.Emit(OpCodes.Ldloc, frame);
            il.Emit(OpCodes.Bge_Un, done);
            LoadFrame(aliasCursor, WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset);
            il.Emit(OpCodes.Bne_Un, next);
            LoadFrame(aliasCursor, WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset);
            il.Emit(OpCodes.Beq, invalid);
            il.MarkLabel(next);
            il.Emit(OpCodes.Ldloc, aliasCursor);
            Constant(layout.FrameWords);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, aliasCursor);
            il.Emit(OpCodes.Br, scan);
            il.MarkLabel(done);
        }
    }
}
