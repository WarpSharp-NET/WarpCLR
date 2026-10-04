using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed partial class WarpPortableMachineEmitter
{
    private sealed partial class Emission
    {
        private string EmitPrivateInstruction(WarpIrInstruction instruction)
        {
            int offset = checked(layout.PrivateOffset + (int)instruction.Immediate);
            if (instruction.OpCode == WarpManagedFrameOpCode.StorePrivateWord)
            {
                StoreFrame(offset, LoadValue(instruction.Left));
            }
            return LoadFrame(offset);
        }

        private void AppendCallCapacity(WarpLogicalMachineNode node, WarpIrInstruction call)
        {
            string physical = Assign("icmp uge i32 %depth, %physical_max_depth");
            string exhausted = physical;
            if (layout.HasLogicalAccounting && !layout.IsRuntimeHelper(call.Callee + 1))
            {
                string logical = Assign($"icmp uge i32 {LoadHeader(WarpLogicalMachineLayout.LogicalDepthOffset)}, %warp_max_depth");
                exhausted = Assign($"or i1 {physical}, {logical}");
            }
            string suffix = N(node.ProgramCounter);
            Line($"  br i1 {exhausted}, label %stack_fault_{suffix}, label %call_{suffix}");
            Line($"stack_fault_{suffix}:");
            AppendFault(node, WarpLogicalMachineLayout.CallDepthFault);
            Line($"call_{suffix}:");
        }

        private void ChangeLogicalDepth(int change) => StoreHeader(WarpLogicalMachineLayout.LogicalDepthOffset,
            Assign($"{(change > 0 ? "add" : "sub")} i32 {LoadHeader(WarpLogicalMachineLayout.LogicalDepthOffset)}, 1"));

        private void AppendPrivateInitialization(string callee, int function)
        {
            int count = layout.GetPrivateWordCount(function);
            for (int word = 0; word < count; word++)
            {
                StoreAt(callee, checked(layout.PrivateOffset + word), "0");
            }
        }

        private void AppendOperationalProgress(WarpLogicalMachineNode node)
        {
            if (!layout.HasLogicalAccounting) { return; }
            string low = Assign($"zext i32 {LoadHeader(WarpLogicalMachineLayout.UsedOperationsLowOffset)} to i64");
            string high = Assign($"zext i32 {LoadHeader(WarpLogicalMachineLayout.UsedOperationsHighOffset)} to i64");
            high = Assign($"shl i64 {high}, 32");
            string used = Assign($"or i64 {low}, {high}");
            string next = Assign($"add i64 {used}, {N(node.BlockCost)}");
            string overflow = Assign($"icmp ult i64 {next}, {used}");
            string suffix = N(node.ProgramCounter);
            Line($"  br i1 {overflow}, label %operation_fault_{suffix}, label %operation_counted_{suffix}");
            Line($"operation_fault_{suffix}:");
            AppendFault(node, WarpLogicalMachineLayout.OperationalOverflowFault);
            Line($"operation_counted_{suffix}:");
            StoreHeader(WarpLogicalMachineLayout.UsedOperationsLowOffset, Assign($"trunc i64 {next} to i32"));
            StoreHeader(WarpLogicalMachineLayout.UsedOperationsHighOffset, Assign($"trunc i64 {Assign($"lshr i64 {next}, 32")} to i32"));
        }
    }
}
