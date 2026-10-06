using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed partial class WarpPortableMachineEmitter
{
    private sealed partial class Emission
    {
        private void AppendScopeFrameIdentity(int function, string pointer, string depth, string suffix)
        {
            string parent = AppendScopeParentActivation(pointer, depth, suffix);
            string activation = LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameActivationOffset, pointer);
            var checks = new List<string>
            {
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameFunctionOffset, pointer)}, {N(function)}"),
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FramePrivateWordsOffset, pointer)}, {N(layout.GetPrivateWordCount(function))}"),
                Assign($"icmp ugt i32 {activation}, {parent}"),
                Assign($"icmp ule i32 {activation}, {LoadHeader(WarpLogicalMachineLayout.NextActivationOffset)}"),
            };
            int owner = layout.GetAliasOwnerFunction(function);
            string ownerDepth = LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset, pointer);
            string ownerActivation = LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset, pointer);
            if (owner == -1)
            {
                checks.Add(Assign($"icmp eq i32 {ownerDepth}, 0")); checks.Add(Assign($"icmp eq i32 {ownerActivation}, 0"));
                Line($"  br i1 {JoinPrivateChecks(checks)}, label %{suffix}_identity_valid, label %done");
                Line($"{suffix}_identity_valid:"); return;
            }
            checks.Add(Assign($"icmp ugt i32 {ownerDepth}, 0")); checks.Add(Assign($"icmp ult i32 {ownerDepth}, {depth}"));
            checks.Add(Assign($"icmp ne i32 {ownerActivation}, 0"));
            checks.Add(PrivateScopeResultCheck(pointer, 0, 0));
            Line($"  br i1 {JoinPrivateChecks(checks)}, label %{suffix}_alias_owner, label %done");
            Line($"{suffix}_alias_owner:");
            string ownerPointer = PrivateScopeFramePointer(ownerDepth);
            string exact = JoinPrivateChecks([
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameFunctionOffset, ownerPointer)}, {N(owner)}"),
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameActivationOffset, ownerPointer)}, {ownerActivation}"),
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FramePrivateWordsOffset, ownerPointer)}, {N(layout.GetPrivateWordCount(owner))}"),
            ]);
            Line($"  br i1 {exact}, label %{suffix}_alias_begin, label %done"); Line($"{suffix}_alias_begin:");
            AppendScopeAliasUniqueness(depth, ownerDepth, ownerActivation, suffix);
            Line($"{suffix}_identity_valid:");
        }

        private string AppendScopeParentActivation(string pointer, string depth, string suffix)
        {
            string first = Assign($"icmp eq i32 {depth}, 1");
            Line($"  br i1 {first}, label %{suffix}_first, label %{suffix}_parent"); Line($"{suffix}_first:");
            Line($"  br label %{suffix}_identity"); Line($"{suffix}_parent:");
            string parent = Assign($"getelementptr i32, ptr addrspace(1) {pointer}, i64 -{N(layout.FrameWords)}");
            string activation = LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameActivationOffset, parent);
            Line($"  br label %{suffix}_identity"); Line($"{suffix}_identity:");
            string value = $"%{suffix}_parent_activation";
            Line($"  {value} = phi i32 [ 0, %{suffix}_first ], [ {activation}, %{suffix}_parent ]");
            return value;
        }

        private void AppendScopeAliasUniqueness(string depth, string ownerDepth, string ownerActivation, string suffix)
        {
            Line($"  br label %{suffix}_alias_scan"); Line($"{suffix}_alias_scan:");
            string index = $"%{suffix}_alias_index", next = $"%{suffix}_alias_next_index";
            Line($"  {index} = phi i32 [ 1, %{suffix}_alias_begin ], [ {next}, %{suffix}_alias_next ]");
            string more = Assign($"icmp ult i32 {index}, {depth}");
            Line($"  br i1 {more}, label %{suffix}_alias_item, label %{suffix}_identity_valid"); Line($"{suffix}_alias_item:");
            string previous = PrivateScopeFramePointer(index);
            string duplicate = JoinPrivateChecks([
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset, previous)}, {ownerDepth}"),
                Assign($"icmp eq i32 {LoadPrivateDispatchFrame(WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset, previous)}, {ownerActivation}"),
            ]);
            Line($"  br i1 {duplicate}, label %done, label %{suffix}_alias_next"); Line($"{suffix}_alias_next:");
            Line($"  {next} = add i32 {index}, 1"); Line($"  br label %{suffix}_alias_scan");
        }
    }
}
