using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed partial class WarpPortableMachineEmitter
{
    private sealed partial class Emission
    {
        private string EmitFrameInstruction(WarpIrInstruction instruction)
        {
            if (WarpManagedFrameOpCode.IsOwner(instruction.OpCode)) { return EmitFrameOwner(instruction); }
            if (WarpManagedStateOpCode.IsState(instruction.OpCode)) { return EmitStateInstruction(instruction); }
            return EmitPrivateInstruction(instruction);
        }

        private void AppendSourceBoundary(WarpLogicalMachineNode node)
        {
            if (layout.IsPrivateHelperBoundary(node)) { AppendPrivateHelperBoundary(node); return; }
            if (!layout.HasLogicalAccounting || node.SourceCost == 0 || layout.IsRuntimeHelper(node.Function)) { return; }
            string suffix = N(node.ProgramCounter);
            string enabled = Assign($"icmp ne i32 {LoadHeader(WarpLogicalMachineLayout.SourceBoundaryModeOffset)}, 0");
            Line($"  br i1 {enabled}, label %boundary_check_{suffix}, label %boundary_execute_{suffix}");
            Line($"boundary_check_{suffix}:");
            string acknowledged = Assign($"icmp eq i32 {LoadHeader(WarpLogicalMachineLayout.SourceBoundaryStateOffset)}, {N(WarpLogicalMachineLayout.AcknowledgedSourceBoundary)}");
            Line($"  br i1 {acknowledged}, label %boundary_ack_{suffix}, label %boundary_park_{suffix}");
            Line($"boundary_park_{suffix}:");
            StoreHeader(WarpLogicalMachineLayout.SourceBoundaryStateOffset, N(WarpLogicalMachineLayout.BeforeSourceBoundary));
            Line("  br label %done");
            Line($"boundary_ack_{suffix}:");
            StoreHeader(WarpLogicalMachineLayout.SourceBoundaryStateOffset, "0");
            Line($"  br label %boundary_execute_{suffix}");
            Line($"boundary_execute_{suffix}:");
        }

        private void AppendFrameIdentity(WarpLogicalMachineNode node, string callee, int function)
        {
            StoreAt(callee, WarpLogicalMachineLayout.FramePrivateWordsOffset, N(layout.GetPrivateWordCount(function)));
            StoreAt(callee, WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset, "0");
            StoreAt(callee, WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset, "0");
            if (!layout.HasFrameOwners) { return; }
            string suffix = N(node.ProgramCounter);
            string previous = LoadHeader(WarpLogicalMachineLayout.NextActivationOffset);
            string exhausted = Assign($"icmp eq i32 {previous}, -1");
            Line($"  br i1 {exhausted}, label %activation_fault_{suffix}, label %activation_admitted_{suffix}");
            Line($"activation_fault_{suffix}:");
            AppendFault(node, WarpLogicalMachineLayout.ActivationExhaustionFault);
            Line($"activation_admitted_{suffix}:");
            string next = Assign($"add i32 {previous}, 1");
            StoreHeader(WarpLogicalMachineLayout.NextActivationOffset, next);
            StoreAt(callee, WarpLogicalMachineLayout.FrameActivationOffset, next);
        }

        private string EmitFrameOwner(WarpIrInstruction instruction) => instruction.OpCode switch
        {
            WarpManagedFrameOpCode.OwnerContext => LoadHeader(WarpLogicalMachineLayout.OwnerContextOffset),
            WarpManagedFrameOpCode.OwnerFrame => layout.GetAliasOwnerFunction(emittedFunction) == -1 ? "%depth" :
                LoadFrame(WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset),
            _ => LoadFrame(layout.GetAliasOwnerFunction(emittedFunction) == -1 ?
                WarpLogicalMachineLayout.FrameActivationOffset : WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset),
        };

        private string EmitStateInstruction(WarpIrInstruction instruction)
        {
            if (instruction.OpCode == WarpManagedStateOpCode.WordCount) { return Assign("trunc i64 %stride to i32"); }
            string index = LoadValue(instruction.Left);
            if (instruction.OpCode == WarpManagedStateOpCode.WordAddress) { return index; }
            string address = Assign($"getelementptr i32, ptr addrspace(1) %state, i64 {Assign($"zext i32 {index} to i64")}");
            if (instruction.OpCode == WarpManagedStateOpCode.LoadWord) { return Assign($"load i32, ptr addrspace(1) {address}, align 4"); }
            string value = LoadValue(instruction.Right);
            Line($"  store i32 {value}, ptr addrspace(1) {address}, align 4");
            return value;
        }

        private void AppendStateBoundsCheck(WarpIrInstruction instruction, WarpLogicalMachineNode node)
        {
            string index = Assign($"zext i32 {LoadValue(instruction.Left)} to i64");
            string invalid = Assign($"icmp uge i64 {index}, %stride");
            string suffix = N(node.ProgramCounter) + "_" + N(instruction.Result);
            Line($"  br i1 {invalid}, label %state_fault_{suffix}, label %state_access_{suffix}");
            Line($"state_fault_{suffix}:");
            AppendFault(node, WarpLogicalMachineLayout.ManagedMemoryBoundsFault);
            Line($"state_access_{suffix}:");
        }
    }
}
