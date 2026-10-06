using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed partial class CoreCLRResumableKernel
{
    private sealed partial class QuantumEmitter
    {
        private void EmitPrivateHelperScopeGuard()
        {
            if (!layout.HasPrivateHelperReturnFences) { return; }
            Label invalid = il.DefineLabel(); Label admitted = il.DefineLabel(); Label absent = il.DefineLabel();
            LoadState(WarpLogicalMachineLayout.SourceBoundaryModeOffset); Constant(1); il.Emit(OpCodes.Bgt_Un, invalid);
            EmitPrivateDepthGuard(invalid);
            il.Emit(OpCodes.Ldloc, depth); il.Emit(OpCodes.Stloc, privateScopeTopDepth);
            LoadState(WarpLogicalMachineLayout.PrivateHelperScopeCallOffset); il.Emit(OpCodes.Brfalse, absent);
            EmitPrivateHeaderGuard(invalid); EmitPrivateScopeDepthGuard(invalid);
            foreach (WarpPrivateHelperReturnSite site in layout.PrivateHelperReturnSites)
            {
                Label next = il.DefineLabel();
                LoadState(WarpLogicalMachineLayout.PrivateHelperScopeCallOffset); Constant(site.CallProgramCounter + 1); il.Emit(OpCodes.Bne_Un, next);
                EmitPrivateScopeSite(site, invalid, admitted); il.MarkLabel(next);
            }
            il.Emit(OpCodes.Br, invalid);
            il.MarkLabel(absent); EmitAbsentPrivateScope(invalid, admitted);
            il.MarkLabel(invalid); EmitQuantumReturn();
            il.MarkLabel(admitted); EmitPrivateDepthGuard(invalid);
        }

        private void EmitPrivateScopeDepthGuard(Label invalid)
        {
            LoadState(WarpLogicalMachineLayout.PrivateHelperScopeCallerDepthOffset); il.Emit(OpCodes.Stloc, depth);
            il.Emit(OpCodes.Ldloc, depth); il.Emit(OpCodes.Brfalse, invalid);
            il.Emit(OpCodes.Ldloc, depth); Constant(int.MaxValue); il.Emit(OpCodes.Bge_Un, invalid);
            il.Emit(OpCodes.Ldloc, depth); Constant(1); il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldarg_S, (byte)4); Constant(layout.Kernel.HelperExpansionFactor); il.Emit(OpCodes.Mul);
            Constant(!layout.CountsSourceDepth(0) ? 1 : 0); il.Emit(OpCodes.Add); il.Emit(OpCodes.Bgt_Un, invalid);
            il.Emit(OpCodes.Ldloc, depth); Constant(1); il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4);
            Constant(WarpLogicalMachineLayout.HeaderWords + layout.ResultTailWords); il.Emit(OpCodes.Sub);
            Constant(layout.FrameWords); il.Emit(OpCodes.Div_Un); il.Emit(OpCodes.Bgt_Un, invalid);
            EmitPrivateScopeFrameAddress();
        }

        private void EmitPrivateScopeSite(WarpPrivateHelperReturnSite site, Label invalid, Label admitted)
        {
            EmitPrivateFrameGuard(site.CallerFunction, invalid);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameProgramCounterOffset); Constant(site.Continuation); il.Emit(OpCodes.Bne_Un, invalid);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameActivationOffset);
            LoadState(WarpLogicalMachineLayout.PrivateHelperScopeCallerActivationOffset); il.Emit(OpCodes.Bne_Un, invalid);
            EmitAdvancePrivateScopeFrame(); EmitPrivateFrameGuard(site.HelperFunction, invalid);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameActivationOffset);
            LoadState(WarpLogicalMachineLayout.PrivateHelperScopeActivationOffset); il.Emit(OpCodes.Bne_Un, invalid);
            EmitPrivateScopeResultGuard(site.ResultValue, site.ResultWordCount, invalid);
            Label active = il.DefineLabel();
            LoadState(WarpLogicalMachineLayout.SourceBoundaryStateOffset); il.Emit(OpCodes.Brfalse, active);
            LoadState(WarpLogicalMachineLayout.SourceBoundaryStateOffset); Constant((int)WarpLogicalMachineLayout.AwaitingRootRelease); il.Emit(OpCodes.Blt_Un, invalid);
            il.Emit(OpCodes.Ldloc, privateScopeTopDepth);
            LoadState(WarpLogicalMachineLayout.PrivateHelperScopeCallerDepthOffset); il.Emit(OpCodes.Bne_Un, invalid);
            EmitPrivateScopeNode(site, returning: true, invalid); il.Emit(OpCodes.Br, admitted);
            il.MarkLabel(active);
            il.Emit(OpCodes.Ldloc, privateScopeTopDepth); il.Emit(OpCodes.Ldloc, depth); il.Emit(OpCodes.Blt_Un, invalid);
            EmitActivePrivateScopeFrames(site, invalid, admitted);
        }

        private void EmitActivePrivateScopeFrames(WarpPrivateHelperReturnSite site, Label invalid, Label admitted)
        {
            Label scan = il.DefineLabel(); Label outer = il.DefineLabel();
            il.MarkLabel(scan); EmitPrivateScopeNode(site, returning: false, invalid);
            il.Emit(OpCodes.Ldloc, depth); LoadState(WarpLogicalMachineLayout.PrivateHelperScopeCallerDepthOffset);
            Constant(1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Beq, outer);
            EmitPrivateNestedReturnLocation(site, invalid);
            il.MarkLabel(outer);
            il.Emit(OpCodes.Ldloc, depth); il.Emit(OpCodes.Ldloc, privateScopeTopDepth); il.Emit(OpCodes.Beq, admitted);
            EmitAdvancePrivateScopeFrame(); il.Emit(OpCodes.Br, scan);
        }

        private void EmitPrivateScopeNode(WarpPrivateHelperReturnSite site, bool returning, Label invalid)
        {
            Label accepted = il.DefineLabel();
            foreach (WarpLogicalMachineNode node in layout.GetPrivateScopeNodes(site).Where(item => !returning ||
                item.Call is null && (item.Terminator is WarpReturnTerminator or WarpTupleReturnTerminator)))
            {
                Label next = il.DefineLabel();
                LoadFrame(frame, WarpLogicalMachineLayout.FrameProgramCounterOffset); Constant(node.ProgramCounter); il.Emit(OpCodes.Bne_Un, next);
                EmitPrivateFrameGuard(node.Function, invalid); il.Emit(OpCodes.Br, accepted); il.MarkLabel(next);
            }
            il.Emit(OpCodes.Br, invalid); il.MarkLabel(accepted);
        }

        private void EmitPrivateNestedReturnLocation(WarpPrivateHelperReturnSite site, Label invalid)
        {
            il.Emit(OpCodes.Ldloc, frame); Constant(layout.FrameWords); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, nextFrame);
            Label accepted = il.DefineLabel();
            foreach (WarpLogicalMachineNode node in layout.GetPrivateScopeCalls(site))
            {
                WarpIrInstruction call = node.Call!.Value; Label next = il.DefineLabel();
                LoadFrame(nextFrame, WarpLogicalMachineLayout.FrameFunctionOffset); Constant(node.Function); il.Emit(OpCodes.Bne_Un, next);
                LoadFrame(nextFrame, WarpLogicalMachineLayout.FrameProgramCounterOffset); Constant(node.Continuation); il.Emit(OpCodes.Bne_Un, next);
                LoadFrame(frame, WarpLogicalMachineLayout.FrameFunctionOffset); Constant(call.Callee + 1); il.Emit(OpCodes.Bne_Un, invalid);
                EmitPrivateScopeResultGuard(call.ResultWordCount == 0 ? 0 : call.Result, call.ResultWordCount, invalid);
                il.Emit(OpCodes.Br, accepted); il.MarkLabel(next);
            }
            il.Emit(OpCodes.Br, invalid); il.MarkLabel(accepted);
        }

        private void EmitPrivateScopeResultGuard(int value, int words, Label invalid)
        {
            LoadFrame(frame, WarpLogicalMachineLayout.FrameReturnValueOffset); Constant(value); il.Emit(OpCodes.Bne_Un, invalid);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameReturnWordCountOffset); Constant(words); il.Emit(OpCodes.Bne_Un, invalid);
        }

        private void EmitAbsentPrivateScope(Label invalid, Label admitted)
        {
            foreach (int offset in new int[] { WarpLogicalMachineLayout.PrivateHelperScopeCallerDepthOffset,
                WarpLogicalMachineLayout.PrivateHelperScopeCallerActivationOffset, WarpLogicalMachineLayout.PrivateHelperScopeActivationOffset })
            { LoadState(offset); il.Emit(OpCodes.Brtrue, invalid); }
            LoadState(WarpLogicalMachineLayout.SourceBoundaryStateOffset); Constant((int)WarpLogicalMachineLayout.AwaitingRootRelease); il.Emit(OpCodes.Bge_Un, invalid);
            Label scan = il.DefineLabel();
            LoadState(WarpLogicalMachineLayout.SourceBoundaryModeOffset); il.Emit(OpCodes.Brtrue, scan);
            LoadState(WarpLogicalMachineLayout.SourceBoundaryStateOffset); il.Emit(OpCodes.Brtrue, invalid);
            il.Emit(OpCodes.Br, admitted); il.MarkLabel(scan);
            Constant(1); il.Emit(OpCodes.Stloc, depth); Constant(WarpLogicalMachineLayout.HeaderWords); il.Emit(OpCodes.Stloc, frame);
            Label item = il.DefineLabel(); il.MarkLabel(item);
            il.Emit(OpCodes.Ldloc, depth); il.Emit(OpCodes.Ldloc, privateScopeTopDepth); il.Emit(OpCodes.Bge_Un, admitted);
            il.Emit(OpCodes.Ldloc, frame); Constant(layout.FrameWords); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, nextFrame);
            foreach (WarpPrivateHelperReturnSite site in layout.PrivateHelperReturnSites)
            {
                Label next = il.DefineLabel();
                LoadFrame(frame, WarpLogicalMachineLayout.FrameFunctionOffset); Constant(site.CallerFunction); il.Emit(OpCodes.Bne_Un, next);
                LoadFrame(frame, WarpLogicalMachineLayout.FrameProgramCounterOffset); Constant(site.Continuation); il.Emit(OpCodes.Bne_Un, next);
                LoadFrame(nextFrame, WarpLogicalMachineLayout.FrameFunctionOffset); Constant(site.HelperFunction); il.Emit(OpCodes.Beq, invalid);
                il.MarkLabel(next);
            }
            EmitAdvancePrivateScopeFrame(); il.Emit(OpCodes.Br, item);
        }

        private void EmitPrivateScopeFrameAddress()
        {
            il.Emit(OpCodes.Ldloc, depth); Constant(1); il.Emit(OpCodes.Sub); Constant(layout.FrameWords); il.Emit(OpCodes.Mul);
            Constant(WarpLogicalMachineLayout.HeaderWords); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, frame);
        }

        private void EmitAdvancePrivateScopeFrame()
        {
            il.Emit(OpCodes.Ldloc, depth); Constant(1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, depth);
            il.Emit(OpCodes.Ldloc, frame); Constant(layout.FrameWords); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, frame);
        }

        private void EmitPrivateHelperScopeStart(WarpLogicalMachineNode node)
        {
            if (!layout.HasPrivateHelperReturnFences || !layout.IsPrivateHelperBoundary(node)) { return; }
            Label skipped = il.DefineLabel();
            LoadState(WarpLogicalMachineLayout.SourceBoundaryModeOffset); Constant(1); il.Emit(OpCodes.Bne_Un, skipped);
            StoreState(WarpLogicalMachineLayout.PrivateHelperScopeCallOffset, () => Constant(node.ProgramCounter + 1));
            StoreState(WarpLogicalMachineLayout.PrivateHelperScopeCallerDepthOffset, () => il.Emit(OpCodes.Ldloc, depth));
            StoreState(WarpLogicalMachineLayout.PrivateHelperScopeCallerActivationOffset,
                () => LoadFrame(frame, WarpLogicalMachineLayout.FrameActivationOffset));
            StoreState(WarpLogicalMachineLayout.PrivateHelperScopeActivationOffset,
                () => LoadFrame(nextFrame, WarpLogicalMachineLayout.FrameActivationOffset));
            il.MarkLabel(skipped);
        }
    }
}
