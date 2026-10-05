using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed partial class WarpPortableMachineEmitter
{
    private sealed partial class Emission
    {
        private int emittedFunction;
        private string aliasOwnerFrame = string.Empty;

        private void AppendAliasGuard(WarpLogicalMachineNode node)
        {
            int owner = layout.GetAliasOwnerFunction(node.Function);
            if (owner == -1) { return; }
            string suffix = N(node.ProgramCounter);
            string ownerDepth = LoadFrame(WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset);
            string positive = Assign($"icmp ne i32 {ownerDepth}, 0");
            string earlier = Assign($"icmp ult i32 {ownerDepth}, %depth");
            string valid = Assign($"and i1 {positive}, {earlier}");
            Line($"  br i1 {valid}, label %alias_identity_{suffix}, label %alias_invalid_{suffix}");
            Line($"alias_identity_{suffix}:");
            string number = Assign($"sub i32 {ownerDepth}, 1");
            string offset = Assign($"add i32 {Assign($"mul i32 {number}, {N(layout.FrameWords)}")}, {N(WarpLogicalMachineLayout.HeaderWords)}");
            aliasOwnerFrame = Assign($"getelementptr i32, ptr addrspace(1) %state, i32 {offset}");
            string activation = LoadFrame(WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset);
            valid = AppendAliasIdentity(owner, activation);
            Line($"  br i1 {valid}, label %alias_scan_entry_{suffix}, label %alias_invalid_{suffix}");
            AppendAliasUniqueness(suffix, ownerDepth, activation);
            Line($"alias_invalid_{suffix}:");
            AppendFault(node, 3);
            Line($"alias_admitted_{suffix}:");
        }

        private string AppendAliasIdentity(int owner, string activation)
        {
            string sameFunction = Assign($"icmp eq i32 {LoadAt(aliasOwnerFrame, WarpLogicalMachineLayout.FrameFunctionOffset)}, {N(owner)}");
            string positive = Assign($"icmp ne i32 {activation}, 0");
            string sameActivation = Assign($"icmp eq i32 {LoadAt(aliasOwnerFrame, WarpLogicalMachineLayout.FrameActivationOffset)}, {activation}");
            string samePrivate = Assign($"icmp eq i32 {LoadAt(aliasOwnerFrame, WarpLogicalMachineLayout.FramePrivateWordsOffset)}, {N(layout.GetPrivateWordCount(owner))}");
            string noValue = Assign($"icmp eq i32 {LoadFrame(WarpLogicalMachineLayout.FrameReturnValueOffset)}, 0");
            string noWords = Assign($"icmp eq i32 {LoadFrame(WarpLogicalMachineLayout.FrameReturnWordCountOffset)}, 0");
            string identity = Assign($"and i1 {Assign($"and i1 {sameFunction}, {positive}")}, {sameActivation}");
            string storage = Assign($"and i1 {Assign($"and i1 {samePrivate}, {noValue}")}, {noWords}");
            return Assign($"and i1 {identity}, {storage}");
        }

        private void AppendAliasUniqueness(string suffix, string ownerDepth, string activation)
        {
            Line($"alias_scan_entry_{suffix}:");
            Line($"  br label %alias_scan_{suffix}");
            Line($"alias_scan_{suffix}:");
            string cursor = Assign($"phi i32 [ {N(WarpLogicalMachineLayout.HeaderWords)}, %alias_scan_entry_{suffix} ], [ %alias_next_{suffix}, %alias_scan_next_{suffix} ]");
            string frameOffset = Assign($"add i32 {Assign($"mul i32 {Assign("sub i32 %depth, 1")}, {N(layout.FrameWords)}")}, {N(WarpLogicalMachineLayout.HeaderWords)}");
            string bounded = Assign($"icmp ult i32 {cursor}, {frameOffset}");
            Line($"  br i1 {bounded}, label %alias_scan_word_{suffix}, label %alias_admitted_{suffix}");
            Line($"alias_scan_word_{suffix}:");
            string address = Assign($"getelementptr i32, ptr addrspace(1) %state, i32 {cursor}");
            string sameDepth = Assign($"icmp eq i32 {LoadAt(address, WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset)}, {ownerDepth}");
            string sameActivation = Assign($"icmp eq i32 {LoadAt(address, WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset)}, {activation}");
            string duplicate = Assign($"and i1 {sameDepth}, {sameActivation}");
            Line($"  br i1 {duplicate}, label %alias_invalid_{suffix}, label %alias_scan_next_{suffix}");
            Line($"alias_scan_next_{suffix}:");
            Line($"  %alias_next_{suffix} = add i32 {cursor}, {N(layout.FrameWords)}");
            Line($"  br label %alias_scan_{suffix}");
        }
    }
}
