using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed partial class CoreCLRResumableKernel
{
    private sealed partial class QuantumEmitter
    {
        private void EmitPrivateSitePhaseGuard(WarpLogicalMachineNode node, Label invalid)
        {
            if (!layout.HasPrivateHelperReturnFences) { return; }
            LoadState(WarpLogicalMachineLayout.SourceBoundaryStateOffset);
            if (layout.IsPrivateHelperBoundary(node))
            {
                Constant((int)WarpLogicalMachineLayout.AcknowledgedPrivateHelper); il.Emit(OpCodes.Bgt_Un, invalid);
            }
            else
            {
                Constant((int)WarpLogicalMachineLayout.AwaitingRootRelease); il.Emit(OpCodes.Blt_Un, invalid);
            }
        }

        private void EmitPrivateReturnDispatchPhase()
        {
            if (!layout.HasPrivateHelperReturnFences) { return; }
            Label released = il.DefineLabel(); Label done = il.DefineLabel();
            LoadState(WarpLogicalMachineLayout.SourceBoundaryStateOffset);
            Constant((int)WarpLogicalMachineLayout.AwaitingRootRelease); il.Emit(OpCodes.Bne_Un, released);
            EmitQuantumReturn();
            il.MarkLabel(released);
            LoadState(WarpLogicalMachineLayout.SourceBoundaryStateOffset);
            Constant((int)WarpLogicalMachineLayout.AcknowledgedRootRelease); il.Emit(OpCodes.Bne_Un, done);
            il.Emit(OpCodes.Ldarg_1); Constant(0); il.Emit(OpCodes.Ldelem_U4);
            Label clear = il.DefineLabel(); il.Emit(OpCodes.Brfalse, clear); EmitQuantumReturn();
            il.MarkLabel(clear);
            StoreState(WarpLogicalMachineLayout.SourceBoundaryStateOffset, () => Constant(0));
            foreach (int offset in new int[] { WarpLogicalMachineLayout.PrivateHelperScopeCallOffset, WarpLogicalMachineLayout.PrivateHelperScopeCallerDepthOffset,
                WarpLogicalMachineLayout.PrivateHelperScopeCallerActivationOffset, WarpLogicalMachineLayout.PrivateHelperScopeActivationOffset })
            { StoreState(offset, () => Constant(0)); }
            il.MarkLabel(done);
        }

        private void EmitPrivateHelperReturnWordGuard()
        {
            // The same scope admission runs before dispatch and again immediately
            // before any tuple write; a no-match active outer scope cannot bypass it.
            EmitPrivateHelperScopeGuard();
        }

        private void EmitPrivateHelperReturnFence()
        {
            if (!layout.HasPrivateHelperReturnFences) { return; }
            Label next = il.DefineLabel();
            LoadState(WarpLogicalMachineLayout.SourceBoundaryModeOffset); Constant(1); il.Emit(OpCodes.Bne_Un, next);
            LoadState(WarpLogicalMachineLayout.PrivateHelperScopeCallOffset); il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldloc, depth); LoadState(WarpLogicalMachineLayout.PrivateHelperScopeCallerDepthOffset);
            Constant(1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Bne_Un, next);
            StoreState(WarpLogicalMachineLayout.SourceBoundaryStateOffset, () => Constant((int)WarpLogicalMachineLayout.AwaitingRootRelease));
            EmitQuantumReturn(); il.MarkLabel(next);
        }
    }
}
