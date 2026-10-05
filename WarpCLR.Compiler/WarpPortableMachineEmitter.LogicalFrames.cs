using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed partial class WarpPortableMachineEmitter
{
    private sealed partial class Emission
    {
        private void AppendStateDispatch(WarpLogicalMachineNode node, WarpStateDispatchTerminator dispatch)
        {
            string suffix = N(node.ProgramCounter);
            string status = LoadHeader(WarpLogicalMachineLayout.StatusOffset);
            string runnable = Assign($"icmp eq i32 {status}, {N(WarpLogicalMachineLayout.Runnable)}");
            Line($"  br i1 {runnable}, label %nonlocal_depth_{suffix}, label %nonlocal_terminal_{suffix}");
            Line($"nonlocal_terminal_{suffix}:");
            string completed = Assign($"icmp eq i32 {status}, {N(WarpLogicalMachineLayout.Completed)}");
            string faulted = Assign($"icmp eq i32 {status}, {N(WarpLogicalMachineLayout.Faulted)}");
            string terminal = Assign($"or i1 {completed}, {faulted}");
            Line($"  br i1 {terminal}, label %done, label %nonlocal_invalid_{suffix}");
            Line($"nonlocal_depth_{suffix}:");
            string newDepth = LoadHeader(WarpLogicalMachineLayout.DepthOffset);
            string positive = Assign($"icmp uge i32 {newDepth}, 1");
            string bounded = Assign($"icmp ule i32 {newDepth}, %physical_max_depth");
            string logical = Assign($"icmp ule i32 {LoadHeader(WarpLogicalMachineLayout.LogicalDepthOffset)}, %warp_max_depth");
            string depthValid = Assign($"and i1 {Assign($"and i1 {positive}, {bounded}")}, {logical}");
            Line($"  br i1 {depthValid}, label %nonlocal_target_{suffix}, label %nonlocal_invalid_{suffix}");
            Line($"nonlocal_target_{suffix}:");
            string number = Assign($"sub i32 {newDepth}, 1");
            string offset = Assign($"add i32 {Assign($"mul i32 {number}, {N(layout.FrameWords)}")}, {N(WarpLogicalMachineLayout.HeaderWords)}");
            string framePointer = Assign($"getelementptr i32, ptr addrspace(1) %state, i32 {offset}");
            string function = LoadAt(framePointer, WarpLogicalMachineLayout.FrameFunctionOffset);
            string pc = LoadAt(framePointer, WarpLogicalMachineLayout.FrameProgramCounterOffset);
            string activation = Assign($"icmp ne i32 {LoadAt(framePointer, WarpLogicalMachineLayout.FrameActivationOffset)}, 0");
            string targetValid = "false";
            foreach (WarpStateDispatchTarget destination in dispatch.Destinations)
            {
                string sameFunction = Assign($"icmp eq i32 {function}, {N(destination.Function)}");
                string samePc = Assign($"icmp eq i32 {pc}, {N(layout.GetBlockEntry(destination.Function, destination.Block))}");
                targetValid = Assign($"or i1 {targetValid}, {Assign($"and i1 {sameFunction}, {samePc}")}");
            }
            string admitted = Assign($"and i1 {targetValid}, {activation}");
            Line($"  br i1 {admitted}, label %dispatch, label %nonlocal_invalid_{suffix}");
            Line($"nonlocal_invalid_{suffix}:");
            AppendFault(node, 3);
        }

        private string LoadAt(string address, int offset) => Assign($"load i32, ptr addrspace(1) {Pointer(address, offset)}, align 4");

        private string EmitPrivateInstruction(WarpIrInstruction instruction)
        {
            int offset = checked(layout.PrivateOffset + (int)instruction.Immediate);
            string privateFrame = instruction.Immediate < layout.GetAliasPrefixWords(emittedFunction) ? aliasOwnerFrame : "%frame";
            if (instruction.OpCode == WarpManagedFrameOpCode.StorePrivateWord)
            {
                StoreAt(privateFrame, offset, LoadValue(instruction.Left));
            }
            return LoadAt(privateFrame, offset);
        }

        private void AppendCallCapacity(WarpLogicalMachineNode node, WarpIrInstruction call)
        {
            string physical = Assign("icmp uge i32 %depth, %physical_max_depth");
            string suffix = N(node.ProgramCounter);
            if (layout.HasLogicalAccounting && layout.CountsSourceDepth(call.Callee + 1))
            {
                string logical = Assign($"icmp uge i32 {LoadHeader(WarpLogicalMachineLayout.LogicalDepthOffset)}, %warp_max_depth");
                Line($"  br i1 {logical}, label %logical_stack_fault_{suffix}, label %physical_stack_check_{suffix}");
                Line($"logical_stack_fault_{suffix}:");
                AppendFault(node, WarpLogicalMachineLayout.CallDepthFault);
                Line($"physical_stack_check_{suffix}:");
            }
            Line($"  br i1 {physical}, label %stack_fault_{suffix}, label %call_{suffix}");
            Line($"stack_fault_{suffix}:");
            AppendFault(node, layout.HasLogicalAccounting ? WarpLogicalMachineLayout.PhysicalFrameCapacityFault : WarpLogicalMachineLayout.CallDepthFault);
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
