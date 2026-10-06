using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed partial class WarpPortableMachineEmitter
{
    private sealed partial class Emission
    {
        private string PrivateSitePhaseCheck(WarpLogicalMachineNode node)
        {
            string phase = LoadHeader(WarpLogicalMachineLayout.SourceBoundaryStateOffset);
            return layout.IsPrivateHelperBoundary(node) ?
                Assign($"icmp ule i32 {phase}, {N(WarpLogicalMachineLayout.AcknowledgedPrivateHelper)}") :
                Assign($"icmp uge i32 {phase}, {N(WarpLogicalMachineLayout.AwaitingRootRelease)}");
        }

        private void AppendPrivateReturnDispatchPhase()
        {
            if (!layout.HasPrivateHelperReturnFences) { return; }
            string phase = LoadHeader(WarpLogicalMachineLayout.SourceBoundaryStateOffset);
            string parked = Assign($"icmp eq i32 {phase}, {N(WarpLogicalMachineLayout.AwaitingRootRelease)}");
            Line($"  br i1 {parked}, label %done, label %private_release_check");
            Line("private_release_check:");
            string released = Assign($"icmp eq i32 {phase}, {N(WarpLogicalMachineLayout.AcknowledgedRootRelease)}");
            Line($"  br i1 {released}, label %private_release_word, label %private_release_admitted");
            Line("private_release_word:");
            string zero = Assign("icmp eq i32 %warp_scalar_0, 0");
            Line($"  br i1 {zero}, label %private_release_clear, label %done");
            Line("private_release_clear:");
            StoreHeader(WarpLogicalMachineLayout.SourceBoundaryStateOffset, "0");
            foreach (int offset in new int[] { WarpLogicalMachineLayout.PrivateHelperScopeCallOffset, WarpLogicalMachineLayout.PrivateHelperScopeCallerDepthOffset,
                WarpLogicalMachineLayout.PrivateHelperScopeCallerActivationOffset, WarpLogicalMachineLayout.PrivateHelperScopeActivationOffset })
            { StoreHeader(offset, "0"); }
            Line("  br label %private_release_admitted");
            Line("private_release_admitted:");
        }

        private void AppendPrivateHelperReturnWordGuard()
        {
            AppendPrivateHelperScopeGuard();
        }

        private void AppendPrivateHelperReturnFence(WarpLogicalMachineNode node)
        {
            if (!layout.HasPrivateHelperReturnFences) { return; }
            string suffix = N(node.ProgramCounter), callerDepth = LoadHeader(WarpLogicalMachineLayout.PrivateHelperScopeCallerDepthOffset);
            string outerDepth = Assign($"add i32 {callerDepth}, 1");
            string fence = JoinPrivateChecks([
                Assign($"icmp eq i32 {LoadHeader(WarpLogicalMachineLayout.SourceBoundaryModeOffset)}, 1"),
                Assign($"icmp ne i32 {LoadHeader(WarpLogicalMachineLayout.PrivateHelperScopeCallOffset)}, 0"),
                Assign($"icmp eq i32 %depth, {outerDepth}"),
            ]);
            Line($"  br i1 {fence}, label %private_return_fence_{suffix}, label %private_return_next_{suffix}");
            Line($"private_return_fence_{suffix}:");
            StoreHeader(WarpLogicalMachineLayout.SourceBoundaryStateOffset, N(WarpLogicalMachineLayout.AwaitingRootRelease));
            Line("  br label %done"); Line($"private_return_next_{suffix}:");
        }
    }
}
