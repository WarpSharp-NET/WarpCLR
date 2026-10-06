using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed partial class WarpPortableMachineEmitter
{
    private sealed partial class Emission
    {
        private void AppendPrivateHelperDispatchGuard()
        {
            AppendPrivateHelperScopeGuard();
            string phase = LoadHeader(WarpLogicalMachineLayout.SourceBoundaryStateOffset);
            string special = Assign($"icmp uge i32 {phase}, {N(WarpLogicalMachineLayout.NeedsPrivateHelper)}");
            Line($"  br i1 {special}, label %private_dispatch_header, label %private_dispatch_admitted");
            Line("private_dispatch_header:");
            string header = PrivateDispatchHeaderChecks(phase);
            Line($"  br i1 {header}, label %private_dispatch_frame, label %done");
            Line("private_dispatch_frame:");
            Line("  %private_frame_number = sub i32 %private_depth, 1");
            Line("  %private_frame_number64 = zext i32 %private_frame_number to i64");
            Line($"  %private_frame_delta = mul i64 %private_frame_number64, {N(layout.FrameWords)}");
            Line($"  %private_frame_offset = add i64 %private_frame_delta, {N(WarpLogicalMachineLayout.HeaderWords)}");
            Line("  %private_frame = getelementptr i32, ptr addrspace(1) %state, i64 %private_frame_offset");
            AppendPrivateParentActivation();
            string activation = LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameActivationOffset);
            string identity = JoinPrivateChecks([
                Assign($"icmp ugt i32 {activation}, %private_parent_activation"),
                Assign($"icmp ule i32 {activation}, {LoadHeader(WarpLogicalMachineLayout.NextActivationOffset)}"),
            ]);
            Line($"  br i1 {identity}, label %private_dispatch_site, label %done");
            Line("private_dispatch_site:");
            string pc = LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameProgramCounterOffset);
            Line($"  switch i32 {pc}, label %done [");
            WarpLogicalMachineNode[] bridges = layout.PrivateHelperDispatchNodes.ToArray();
            foreach (WarpLogicalMachineNode node in bridges)
            { Line($"    i32 {N(node.ProgramCounter)}, label %private_dispatch_site_{N(node.ProgramCounter)}"); }
            Line("  ]");
            foreach (WarpLogicalMachineNode node in bridges) { AppendPrivateHelperSiteGuard(node); }
            Line("private_dispatch_admitted:");
            AppendPrivateReturnDispatchPhase();
        }

        private string PrivateDispatchHeaderChecks(string phase)
        {
            var checks = new List<string> { Assign($"icmp ule i32 {phase}, {N(layout.MaximumSourceBoundaryPhase)}") };
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
            { checks.Add(Assign($"icmp eq i32 {LoadHeader(offset)}, {N(expected)}")); }
            checks.Add(Assign($"icmp ne i32 {LoadHeader(WarpLogicalMachineLayout.OwnerContextOffset)}, 0"));
            checks.Add(Assign($"icmp ne i32 {LoadHeader(WarpLogicalMachineLayout.NextActivationOffset)}, 0"));
            checks.Add(Assign($"icmp ne i32 {LoadHeader(WarpLogicalMachineLayout.FaultKindOffset)}, {N(WarpLogicalMachineLayout.ManagedExceptionFault)}"));
            Line($"  %private_depth = load i32, ptr addrspace(1) {HeaderPointer(WarpLogicalMachineLayout.DepthOffset)}, align 4");
            checks.Add(Assign("icmp ugt i32 %private_depth, 0"));
            checks.Add(Assign("icmp ule i32 %private_depth, %physical_max_depth"));
            return JoinPrivateChecks(checks);
        }

        private void AppendPrivateParentActivation()
        {
            string first = Assign("icmp eq i32 %private_depth, 1");
            Line($"  br i1 {first}, label %private_dispatch_first, label %private_dispatch_parent");
            Line("private_dispatch_first:");
            Line("  br label %private_dispatch_identity");
            Line("private_dispatch_parent:");
            Line($"  %private_parent_offset = sub i64 %private_frame_offset, {N(layout.FrameWords)}");
            string pointer = Assign("getelementptr i32, ptr addrspace(1) %state, i64 %private_parent_offset");
            string activation = LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameActivationOffset, pointer);
            Line("  br label %private_dispatch_identity");
            Line("private_dispatch_identity:");
            Line($"  %private_parent_activation = phi i32 [ 0, %private_dispatch_first ], [ {activation}, %private_dispatch_parent ]");
        }

        private void AppendPrivateHelperSiteGuard(WarpLogicalMachineNode node)
        {
            string suffix = N(node.ProgramCounter);
            Line($"private_dispatch_site_{suffix}:");
            var checks = new List<string>
            {
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameFunctionOffset)}, {N(node.Function)}"),
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FramePrivateWordsOffset)}, {N(layout.GetPrivateWordCount(node.Function))}"),
            };
            if (layout.HasPrivateHelperReturnFences) { checks.Add(PrivateSitePhaseCheck(node)); }
            int owner = layout.GetAliasOwnerFunction(node.Function);
            if (owner == -1)
            {
                checks.Add(Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset)}, 0"));
                checks.Add(Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset)}, 0"));
                Line($"  br i1 {JoinPrivateChecks(checks)}, label %private_dispatch_admitted, label %done");
                return;
            }
            string ownerDepth = LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset);
            string ownerActivation = LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset);
            checks.Add(Assign($"icmp ugt i32 {ownerDepth}, 0"));
            checks.Add(Assign($"icmp ult i32 {ownerDepth}, %private_depth"));
            checks.Add(Assign($"icmp ne i32 {ownerActivation}, 0"));
            checks.Add(Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameReturnValueOffset)}, 0"));
            checks.Add(Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameReturnWordCountOffset)}, 0"));
            Line($"  br i1 {JoinPrivateChecks(checks)}, label %private_alias_owner_{suffix}, label %done");
            AppendPrivateAliasIdentity(node, owner, ownerDepth, ownerActivation);
        }

        private void AppendPrivateAliasIdentity(WarpLogicalMachineNode node, int owner, string ownerDepth, string ownerActivation)
        {
            string suffix = N(node.ProgramCounter);
            Line($"private_alias_owner_{suffix}:");
            string index = Assign($"sub i32 {ownerDepth}, 1");
            string wide = Assign($"zext i32 {index} to i64");
            string delta = Assign($"mul i64 {wide}, {N(layout.FrameWords)}");
            string offset = Assign($"add i64 {delta}, {N(WarpLogicalMachineLayout.HeaderWords)}");
            string pointer = Assign($"getelementptr i32, ptr addrspace(1) %state, i64 {offset}");
            string valid = JoinPrivateChecks([
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameFunctionOffset, pointer)}, {N(owner)}"),
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameActivationOffset, pointer)}, {ownerActivation}"),
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FramePrivateWordsOffset, pointer)}, {N(layout.GetPrivateWordCount(owner))}"),
            ]);
            Line($"  br i1 {valid}, label %private_alias_begin_{suffix}, label %done");
            AppendPrivateAliasUniqueness(suffix, ownerDepth, ownerActivation);
        }

        private void AppendPrivateAliasUniqueness(string suffix, string ownerDepth, string ownerActivation)
        {
            Line($"private_alias_begin_{suffix}:");
            Line($"  br label %private_alias_scan_{suffix}");
            Line($"private_alias_scan_{suffix}:");
            string cursor = $"%private_alias_cursor_{suffix}";
            string next = $"%private_alias_next_index_{suffix}";
            Line($"  {cursor} = phi i32 [ 0, %private_alias_begin_{suffix} ], [ {next}, %private_alias_next_{suffix} ]");
            string more = Assign($"icmp ult i32 {cursor}, %private_frame_number");
            Line($"  br i1 {more}, label %private_alias_item_{suffix}, label %private_dispatch_admitted");
            Line($"private_alias_item_{suffix}:");
            string wide = Assign($"zext i32 {cursor} to i64");
            string delta = Assign($"mul i64 {wide}, {N(layout.FrameWords)}");
            string offset = Assign($"add i64 {delta}, {N(WarpLogicalMachineLayout.HeaderWords)}");
            string pointer = Assign($"getelementptr i32, ptr addrspace(1) %state, i64 {offset}");
            string duplicate = JoinPrivateChecks([
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset, pointer)}, {ownerDepth}"),
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset, pointer)}, {ownerActivation}"),
            ]);
            Line($"  br i1 {duplicate}, label %done, label %private_alias_next_{suffix}");
            Line($"private_alias_next_{suffix}:");
            Line($"  {next} = add i32 {cursor}, 1");
            Line($"  br label %private_alias_scan_{suffix}");
        }

        private string JoinPrivateChecks(List<string> checks)
        {
            string result = checks[0];
            for (int index = 1; index < checks.Count; index++) { result = Assign($"and i1 {result}, {checks[index]}"); }
            return result;
        }

        private string LoadPrivateDispatchFrame(int offset, string pointer = "%private_frame")
        {
            string address = Assign($"getelementptr i32, ptr addrspace(1) {pointer}, i32 {N(offset)}");
            return Assign($"load i32, ptr addrspace(1) {address}, align 4");
        }

        private void AppendPrivateHelperBoundary(WarpLogicalMachineNode node)
        {
            string suffix = N(node.ProgramCounter);
            string enabled = Assign($"icmp ne i32 {LoadHeader(WarpLogicalMachineLayout.SourceBoundaryModeOffset)}, 0");
            Line($"  br i1 {enabled}, label %private_check_{suffix}, label %private_execute_{suffix}");
            Line($"private_check_{suffix}:");
            string acknowledged = Assign($"icmp eq i32 {LoadHeader(WarpLogicalMachineLayout.SourceBoundaryStateOffset)}, {N(WarpLogicalMachineLayout.AcknowledgedPrivateHelper)}");
            string supplied = Assign("icmp ne i32 %warp_scalar_0, 0");
            Line($"  br i1 {acknowledged}, label %private_word_{suffix}, label %private_park_{suffix}");
            Line($"private_word_{suffix}:");
            Line($"  br i1 {supplied}, label %private_ack_{suffix}, label %done");
            Line($"private_park_{suffix}:");
            StoreHeader(WarpLogicalMachineLayout.SourceBoundaryStateOffset, N(WarpLogicalMachineLayout.NeedsPrivateHelper));
            Line("  br label %done");
            Line($"private_ack_{suffix}:");
            StoreHeader(WarpLogicalMachineLayout.SourceBoundaryStateOffset, "0");
            Line($"  br label %private_execute_{suffix}");
            Line($"private_execute_{suffix}:");
        }
    }
}
