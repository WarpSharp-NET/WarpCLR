using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed partial class WarpPortableMachineEmitter
{
    private sealed partial class Emission
    {
        private void AppendPrivateHelperScopeGuard()
        {
            if (!layout.HasPrivateHelperReturnFences) { return; }
            string suffix = $"scope_{N(temporary++)}";
            string mode = LoadHeader(WarpLogicalMachineLayout.SourceBoundaryModeOffset);
            string depth = LoadHeader(WarpLogicalMachineLayout.DepthOffset);
            string valid = JoinPrivateChecks([Assign($"icmp ule i32 {mode}, 1"),
                Assign($"icmp ugt i32 {depth}, 0"), Assign($"icmp ule i32 {depth}, %physical_max_depth")]);
            Line($"  br i1 {valid}, label %{suffix}_tag, label %done");
            Line($"{suffix}_tag:");
            string tag = LoadHeader(WarpLogicalMachineLayout.PrivateHelperScopeCallOffset);
            string absent = Assign($"icmp eq i32 {tag}, 0");
            Line($"  br i1 {absent}, label %{suffix}_absent, label %{suffix}_header");
            Line($"{suffix}_header:");
            string phase = LoadHeader(WarpLogicalMachineLayout.SourceBoundaryStateOffset);
            string header = PrivateScopeHeaderChecks(phase, mode);
            string callerDepth = LoadHeader(WarpLogicalMachineLayout.PrivateHelperScopeCallerDepthOffset);
            string bounds = JoinPrivateChecks([header, Assign($"icmp ugt i32 {callerDepth}, 0"),
                Assign($"icmp ult i32 {callerDepth}, %physical_max_depth"), Assign($"icmp ult i32 {callerDepth}, 2147483647")]);
            Line($"  br i1 {bounds}, label %{suffix}_site, label %done");
            Line($"{suffix}_site:");
            Line($"  switch i32 {tag}, label %done [");
            foreach (WarpPrivateHelperReturnSite site in layout.PrivateHelperReturnSites)
            { Line($"    i32 {N(site.CallProgramCounter + 1)}, label %{suffix}_site_{N(site.CallProgramCounter)}"); }
            Line("  ]");
            foreach (WarpPrivateHelperReturnSite site in layout.PrivateHelperReturnSites)
            { AppendPrivateScopeSite(site, suffix, depth, callerDepth, phase); }
            AppendAbsentPrivateScope(suffix, depth, mode);
            Line($"{suffix}_admitted:");
        }

        private string PrivateScopeHeaderChecks(string phase, string mode)
        {
            var checks = new List<string> { Assign($"icmp eq i32 {mode}, 1"),
                Assign($"icmp ule i32 {phase}, {N(layout.MaximumSourceBoundaryPhase)}") };
            foreach ((int offset, int expected) in new (int, int)[]
            {
                (WarpLogicalMachineLayout.StatusOffset, (int)WarpLogicalMachineLayout.Runnable),
                (WarpLogicalMachineLayout.FrameStrideOffset, layout.FrameWords), (WarpLogicalMachineLayout.PrivateBaseOffset, layout.PrivateOffset),
                (WarpLogicalMachineLayout.EscapedExceptionContextOffset, 0), (WarpLogicalMachineLayout.EscapedExceptionObjectOffset, 0),
                (WarpLogicalMachineLayout.EscapedExceptionGenerationOffset, 0),
            })
            { checks.Add(Assign($"icmp eq i32 {LoadHeader(offset)}, {N(expected)}")); }
            checks.Add(Assign($"icmp ne i32 {LoadHeader(WarpLogicalMachineLayout.OwnerContextOffset)}, 0"));
            checks.Add(Assign($"icmp ne i32 {LoadHeader(WarpLogicalMachineLayout.NextActivationOffset)}, 0"));
            checks.Add(Assign($"icmp ne i32 {LoadHeader(WarpLogicalMachineLayout.FaultKindOffset)}, {N(WarpLogicalMachineLayout.ManagedExceptionFault)}"));
            return JoinPrivateChecks(checks);
        }

        private void AppendPrivateScopeSite(WarpPrivateHelperReturnSite site, string guard, string topDepth, string callerDepth, string phase)
        {
            string suffix = $"{guard}_site_{N(site.CallProgramCounter)}"; Line($"{suffix}:");
            string caller = PrivateScopeFramePointer(callerDepth);
            AppendScopeFrameIdentity(site.CallerFunction, caller, callerDepth, suffix + "_caller");
            string callerExact = JoinPrivateChecks([
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameProgramCounterOffset, caller)}, {N(site.Continuation)}"),
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameActivationOffset, caller)}, {LoadHeader(WarpLogicalMachineLayout.PrivateHelperScopeCallerActivationOffset)}"),
            ]);
            Line($"  br i1 {callerExact}, label %{suffix}_outer, label %done"); Line($"{suffix}_outer:");
            string outerDepth = Assign($"add i32 {callerDepth}, 1"); string outer = PrivateScopeFramePointer(outerDepth);
            AppendScopeFrameIdentity(site.HelperFunction, outer, outerDepth, suffix + "_outer");
            string outerExact = JoinPrivateChecks([
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameActivationOffset, outer)}, {LoadHeader(WarpLogicalMachineLayout.PrivateHelperScopeActivationOffset)}"),
                PrivateScopeResultCheck(outer, site.ResultValue, site.ResultWordCount),
            ]);
            Line($"  br i1 {outerExact}, label %{suffix}_phase, label %done"); Line($"{suffix}_phase:");
            string active = Assign($"icmp eq i32 {phase}, 0");
            Line($"  br i1 {active}, label %{suffix}_active, label %{suffix}_restored");
            Line($"{suffix}_restored:");
            string restored = JoinPrivateChecks([Assign($"icmp uge i32 {phase}, {N(WarpLogicalMachineLayout.AwaitingRootRelease)}"),
                Assign($"icmp eq i32 {topDepth}, {callerDepth}")]);
            Line($"  br i1 {restored}, label %{suffix}_return, label %done"); Line($"{suffix}_return:");
            AppendScopeNode(site, outer, outerDepth, suffix + "_return", returning: true, guard + "_admitted");
            Line($"{suffix}_active:");
            string deepEnough = Assign($"icmp uge i32 {topDepth}, {outerDepth}");
            Line($"  br i1 {deepEnough}, label %{suffix}_begin, label %done"); Line($"{suffix}_begin:");
            AppendActiveScopeFrames(site, guard, suffix, topDepth, outerDepth);
        }

        private void AppendActiveScopeFrames(WarpPrivateHelperReturnSite site, string guard, string suffix, string topDepth, string outerDepth)
        {
            Line($"  br label %{suffix}_scan"); Line($"{suffix}_scan:");
            string depth = $"%{suffix}_depth", next = $"%{suffix}_next_depth";
            Line($"  {depth} = phi i32 [ {outerDepth}, %{suffix}_begin ], [ {next}, %{suffix}_next ]");
            string pointer = PrivateScopeFramePointer(depth);
            AppendScopeNode(site, pointer, depth, suffix + "_node", returning: false, suffix + "_parent");
            Line($"{suffix}_parent:");
            string first = Assign($"icmp eq i32 {depth}, {outerDepth}");
            Line($"  br i1 {first}, label %{suffix}_checked, label %{suffix}_nested"); Line($"{suffix}_nested:");
            AppendNestedScopeReturn(site, pointer, suffix);
            Line($"{suffix}_checked:");
            string last = Assign($"icmp eq i32 {depth}, {topDepth}");
            Line($"  br i1 {last}, label %{guard}_admitted, label %{suffix}_next"); Line($"{suffix}_next:");
            Line($"  {next} = add i32 {depth}, 1"); Line($"  br label %{suffix}_scan");
        }

        private void AppendScopeNode(WarpPrivateHelperReturnSite site, string pointer, string depth, string suffix, bool returning, string accepted)
        {
            WarpLogicalMachineNode[] candidates = layout.GetPrivateScopeNodes(site).Where(node => !returning ||
                node.Call is null && (node.Terminator is WarpReturnTerminator or WarpTupleReturnTerminator)).ToArray();
            string pc = LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameProgramCounterOffset, pointer);
            Line($"  switch i32 {pc}, label %done [");
            foreach (WarpLogicalMachineNode node in candidates) { Line($"    i32 {N(node.ProgramCounter)}, label %{suffix}_{N(node.ProgramCounter)}"); }
            Line("  ]");
            foreach (WarpLogicalMachineNode node in candidates)
            {
                string entry = $"{suffix}_{N(node.ProgramCounter)}"; Line($"{entry}:");
                AppendScopeFrameIdentity(node.Function, pointer, depth, entry); Line($"  br label %{accepted}");
            }
        }

        private void AppendNestedScopeReturn(WarpPrivateHelperReturnSite site, string pointer, string suffix)
        {
            string parent = Assign($"getelementptr i32, ptr addrspace(1) {pointer}, i64 -{N(layout.FrameWords)}");
            WarpLogicalMachineNode[] calls = layout.GetPrivateScopeCalls(site).ToArray();
            if (calls.Length == 0) { Line("  br label %done"); return; }
            foreach (WarpLogicalMachineNode node in calls)
            {
                WarpIrInstruction call = node.Call!.Value; string entry = $"{suffix}_call_{N(node.ProgramCounter)}";
                string matches = JoinPrivateChecks([
                    Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameFunctionOffset, parent)}, {N(node.Function)}"),
                    Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameProgramCounterOffset, parent)}, {N(node.Continuation)}"),
                ]);
                Line($"  br i1 {matches}, label %{entry}, label %{entry}_next"); Line($"{entry}:");
                string valid = JoinPrivateChecks([Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameFunctionOffset, pointer)}, {N(call.Callee + 1)}"),
                    PrivateScopeResultCheck(pointer, call.ResultWordCount == 0 ? 0 : call.Result, call.ResultWordCount)]);
                Line($"  br i1 {valid}, label %{suffix}_checked, label %done"); Line($"{entry}_next:");
            }
            Line("  br label %done");
        }

        private string PrivateScopeResultCheck(string pointer, int value, int words) => JoinPrivateChecks([
            Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameReturnValueOffset, pointer)}, {N(value)}"),
            Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameReturnWordCountOffset, pointer)}, {N(words)}"),
        ]);

        private string PrivateScopeFramePointer(string depth)
        {
            string index = Assign($"sub i32 {depth}, 1"), wide = Assign($"zext i32 {index} to i64");
            string delta = Assign($"mul i64 {wide}, {N(layout.FrameWords)}"), offset = Assign($"add i64 {delta}, {N(WarpLogicalMachineLayout.HeaderWords)}");
            return Assign($"getelementptr i32, ptr addrspace(1) %state, i64 {offset}");
        }

        private void AppendAbsentPrivateScope(string suffix, string topDepth, string mode)
        {
            Line($"{suffix}_absent:");
            var checks = new List<string>();
            foreach (int offset in new int[] { WarpLogicalMachineLayout.PrivateHelperScopeCallerDepthOffset,
                WarpLogicalMachineLayout.PrivateHelperScopeCallerActivationOffset, WarpLogicalMachineLayout.PrivateHelperScopeActivationOffset })
            { checks.Add(Assign($"icmp eq i32 {LoadHeader(offset)}, 0")); }
            string phase = LoadHeader(WarpLogicalMachineLayout.SourceBoundaryStateOffset);
            checks.Add(Assign($"icmp ult i32 {phase}, {N(WarpLogicalMachineLayout.AwaitingRootRelease)}"));
            Line($"  br i1 {JoinPrivateChecks(checks)}, label %{suffix}_absent_mode, label %done"); Line($"{suffix}_absent_mode:");
            string enabled = Assign($"icmp eq i32 {mode}, 1");
            Line($"  br i1 {enabled}, label %{suffix}_absent_begin, label %{suffix}_disabled"); Line($"{suffix}_disabled:");
            string phaseZero = Assign($"icmp eq i32 {phase}, 0");
            Line($"  br i1 {phaseZero}, label %{suffix}_admitted, label %done"); Line($"{suffix}_absent_begin:");
            Line($"  br label %{suffix}_absent_scan"); Line($"{suffix}_absent_scan:");
            string depth = $"%{suffix}_absent_depth", next = $"%{suffix}_absent_next_depth";
            Line($"  {depth} = phi i32 [ 1, %{suffix}_absent_begin ], [ {next}, %{suffix}_absent_next ]");
            string more = Assign($"icmp ult i32 {depth}, {topDepth}");
            Line($"  br i1 {more}, label %{suffix}_absent_item, label %{suffix}_admitted"); Line($"{suffix}_absent_item:");
            string pointer = PrivateScopeFramePointer(depth), child = Assign($"getelementptr i32, ptr addrspace(1) {pointer}, i64 {N(layout.FrameWords)}");
            foreach (WarpPrivateHelperReturnSite site in layout.PrivateHelperReturnSites)
            {
                string entry = $"{suffix}_absent_{N(site.CallProgramCounter)}";
                string matches = JoinPrivateChecks([
                    Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameFunctionOffset, pointer)}, {N(site.CallerFunction)}"),
                    Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameProgramCounterOffset, pointer)}, {N(site.Continuation)}"),
                    Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameFunctionOffset, child)}, {N(site.HelperFunction)}"),
                ]);
                Line($"  br i1 {matches}, label %done, label %{entry}_next"); Line($"{entry}_next:");
            }
            Line($"  br label %{suffix}_absent_next"); Line($"{suffix}_absent_next:");
            Line($"  {next} = add i32 {depth}, 1"); Line($"  br label %{suffix}_absent_scan");
        }

        private void AppendPrivateHelperScopeStart(WarpLogicalMachineNode node, string helper)
        {
            if (!layout.HasPrivateHelperReturnFences || !layout.IsPrivateHelperBoundary(node)) { return; }
            string suffix = $"scope_start_{N(node.ProgramCounter)}", enabled = Assign($"icmp eq i32 {LoadHeader(WarpLogicalMachineLayout.SourceBoundaryModeOffset)}, 1");
            Line($"  br i1 {enabled}, label %{suffix}, label %{suffix}_next"); Line($"{suffix}:");
            StoreHeader(WarpLogicalMachineLayout.PrivateHelperScopeCallOffset, N(node.ProgramCounter + 1));
            StoreHeader(WarpLogicalMachineLayout.PrivateHelperScopeCallerDepthOffset, "%depth");
            StoreHeader(WarpLogicalMachineLayout.PrivateHelperScopeCallerActivationOffset, LoadFrame(WarpLogicalMachineLayout.FrameActivationOffset));
            StoreHeader(WarpLogicalMachineLayout.PrivateHelperScopeActivationOffset, LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameActivationOffset, helper));
            Line($"  br label %{suffix}_next"); Line($"{suffix}_next:");
        }
    }
}
