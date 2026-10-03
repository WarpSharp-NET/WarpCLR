using System.Globalization;
using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed class WarpPortableMachineEmitter
{
    public const string EntryPoint = "warp_resume";

    public const string ReductionEntryPoint = "warp_reduce_pass";

    public static string Emit(WarpLogicalMachineLayout layout, WarpBackendKind backend)
        => Emit(layout, backend, WarpCompilationAdmission.MaximumSourceBytes);

    public static string Emit(WarpLogicalMachineLayout layout, WarpBackendKind backend, int maximumSourceBytes)
        => Emit(layout, backend, maximumSourceBytes, CancellationToken.None);

    public static string Emit(WarpLogicalMachineLayout layout, WarpBackendKind backend, int maximumSourceBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSourceBytes);
        cancellationToken.ThrowIfCancellationRequested();
        WarpCompilationAdmission.Validate(layout.Kernel);
        return new Emission(layout, backend, maximumSourceBytes, cancellationToken).Emit();
    }

    private sealed class Emission
    {
        private readonly WarpLogicalMachineLayout layout;
        private readonly WarpBackendKind backend;
        private readonly WarpBoundedSourceBuilder source;
        private int temporary;

        public Emission(WarpLogicalMachineLayout layout, WarpBackendKind backend, int maximumSourceBytes, CancellationToken cancellationToken)
        {
            this.layout = layout;
            this.backend = backend;
            if (backend is not (WarpBackendKind.NVPTX or WarpBackendKind.AMDGPU or WarpBackendKind.SPIRV))
            {
                throw new ArgumentOutOfRangeException(nameof(backend));
            }

            source = new WarpBoundedSourceBuilder(layout.Kernel.Name, maximumSourceBytes, cancellationToken);
        }

        public string Emit()
        {
            AppendHeader();
            AppendParameters();
            AppendEntry();
            AppendDispatch();
            foreach (WarpLogicalMachineNode node in layout.Nodes)
            {
                AppendNode(node);
            }

            Line("done:");
            Line("  ret void");
            Line("}");
            AppendReductionPass();
            if (backend == WarpBackendKind.AMDGPU)
            {
                Line($"attributes #0 = {{ nounwind \"amdgpu-flat-work-group-size\"=\"{N(WarpDeviceAbi.IntegerMapWorkgroupSize)},{N(WarpDeviceAbi.IntegerMapWorkgroupSize)}\" }}");
            }

            return source.ToString();
        }

        private void AppendHeader()
        {
            Line($"; {WarpRuntimeAbi.Version}; {WarpLogicalMachineLayout.Version}; {WarpRuntimeAbi.SafepointPolicy}");
            string triple = backend switch
            {
                WarpBackendKind.NVPTX => "nvptx64-nvidia-cuda",
                WarpBackendKind.AMDGPU => "amdgcn-amd-amdhsa",
                _ => "spir64-unknown-unknown",
            };
            Line($"target triple = \"{triple}\"");
            if (backend == WarpBackendKind.NVPTX)
            {
                Line("declare i32 @llvm.nvvm.read.ptx.sreg.tid.x()");
                Line("declare i32 @llvm.nvvm.read.ptx.sreg.ctaid.x()");
                Line("declare i32 @llvm.nvvm.read.ptx.sreg.ntid.x()");
            }
            else if (backend == WarpBackendKind.AMDGPU)
            {
                Line("declare i32 @llvm.amdgcn.workitem.id.x()");
                Line("declare i32 @llvm.amdgcn.workgroup.id.x()");
            }
            else
            {
                Line("declare spir_func i64 @_Z13get_global_idj(i32)");
            }
        }

        private void AppendReductionPass()
        {
            string convention = backend switch
            {
                WarpBackendKind.NVPTX => "ptx_kernel",
                WarpBackendKind.AMDGPU => "amdgpu_kernel",
                _ => "spir_kernel",
            };
            string attribute = backend == WarpBackendKind.AMDGPU ? " #0" : string.Empty;
            Line($"define {convention} void @{ReductionEntryPoint}(ptr addrspace(1) %warp_output, ptr addrspace(1) %warp_input, i32 %warp_count, i32 %warp_operation){attribute} {{");
            Line("entry:");
            AppendWorkerIndex();
            Line("  %count64 = zext i32 %warp_count to i64");
            Line("  %left_index = mul i64 %worker, 2");
            Line("  %in_range = icmp ult i64 %left_index, %count64");
            Line("  br i1 %in_range, label %load_left, label %done");
            Line("load_left:");
            Line("  %left_ptr = getelementptr i32, ptr addrspace(1) %warp_input, i64 %left_index");
            Line("  %left = load i32, ptr addrspace(1) %left_ptr, align 4");
            Line("  %output_ptr = getelementptr i32, ptr addrspace(1) %warp_output, i64 %worker");
            Line("  %right_index = add i64 %left_index, 1");
            Line("  %has_right = icmp ult i64 %right_index, %count64");
            Line("  br i1 %has_right, label %combine, label %single");
            Line("single:");
            Line("  store i32 %left, ptr addrspace(1) %output_ptr, align 4");
            Line("  br label %done");
            AppendReductionCombine();
            Line("done:");
            Line("  ret void");
            Line("}");
        }

        private void AppendReductionCombine()
        {
            Line("combine:");
            Line("  %right_ptr = getelementptr i32, ptr addrspace(1) %warp_input, i64 %right_index");
            Line("  %right = load i32, ptr addrspace(1) %right_ptr, align 4");
            Line("  switch i32 %warp_operation, label %done [ i32 0, label %sum i32 1, label %minimum i32 2, label %maximum ]");
            Line("sum:");
            Line("  %sum_value = add i32 %left, %right");
            Line("  store i32 %sum_value, ptr addrspace(1) %output_ptr, align 4");
            Line("  br label %done");
            Line("minimum:");
            Line("  %smaller = icmp ult i32 %left, %right");
            Line("  %minimum_value = select i1 %smaller, i32 %left, i32 %right");
            Line("  store i32 %minimum_value, ptr addrspace(1) %output_ptr, align 4");
            Line("  br label %done");
            Line("maximum:");
            Line("  %larger = icmp ugt i32 %left, %right");
            Line("  %maximum_value = select i1 %larger, i32 %left, i32 %right");
            Line("  store i32 %maximum_value, ptr addrspace(1) %output_ptr, align 4");
            Line("  br label %done");
        }

        private void AppendParameters()
        {
            string convention = backend switch
            {
                WarpBackendKind.NVPTX => "ptx_kernel",
                WarpBackendKind.AMDGPU => "amdgpu_kernel",
                _ => "spir_kernel",
            };
            var parameters = new List<string> { "ptr addrspace(1) %warp_states" };
            for (int index = 0; index < layout.Kernel.InputBufferCount; index++)
            {
                parameters.Add($"ptr addrspace(1) %warp_input_{N(index)}");
            }

            for (int index = 0; index < layout.Kernel.ScalarArgumentCount; index++)
            {
                parameters.Add($"i32 %warp_scalar_{N(index)}");
            }

            parameters.AddRange(["i32 %warp_count", "i32 %warp_base", "i32 %warp_max_depth", "i32 %warp_quantum"]);
            string attribute = backend == WarpBackendKind.AMDGPU ? " #0" : string.Empty;
            Line($"define {convention} void @{EntryPoint}({string.Join(", ", parameters)}){attribute} {{");
        }

        private void AppendEntry()
        {
            Line("entry:");
            Line("  %quantum_ptr = alloca i32, align 4");
            Line("  store i32 %warp_quantum, ptr %quantum_ptr, align 4");
            AppendWorkerIndex();
            Line("  %count64 = zext i32 %warp_count to i64");
            Line("  %in_range = icmp ult i64 %worker, %count64");
            Line("  br i1 %in_range, label %initialize, label %done");
            Line("initialize:");
            Line("  %base64 = zext i32 %warp_base to i64");
            Line("  %input_index = add i64 %base64, %worker");
            Line("  %max_depth64 = zext i32 %warp_max_depth to i64");
            Line($"  %frame_area = mul i64 %max_depth64, {N(layout.FrameWords)}");
            Line($"  %stride = add i64 %frame_area, {N(WarpLogicalMachineLayout.HeaderWords)}");
            Line("  %state_offset = mul i64 %worker, %stride");
            Line("  %state = getelementptr i32, ptr addrspace(1) %warp_states, i64 %state_offset");
            Line("  br label %dispatch");
        }

        private void AppendWorkerIndex()
        {
            if (backend == WarpBackendKind.SPIRV)
            {
                Line("  %worker = call spir_func i64 @_Z13get_global_idj(i32 0)");
                return;
            }

            string prefix = backend == WarpBackendKind.NVPTX ? "llvm.nvvm.read.ptx.sreg." : "llvm.amdgcn.";
            string local = backend == WarpBackendKind.NVPTX ? "tid.x" : "workitem.id.x";
            string group = backend == WarpBackendKind.NVPTX ? "ctaid.x" : "workgroup.id.x";
            Line($"  %local_id = call i32 @{prefix}{local}()");
            Line($"  %group_id = call i32 @{prefix}{group}()");
            string size = N(WarpDeviceAbi.IntegerMapWorkgroupSize);
            if (backend == WarpBackendKind.NVPTX)
            {
                Line("  %group_size = call i32 @llvm.nvvm.read.ptx.sreg.ntid.x()");
                size = "%group_size";
            }

            Line("  %group64 = zext i32 %group_id to i64");
            Line("  %local64 = zext i32 %local_id to i64");
            Line($"  %size64 = zext i32 {size} to i64");
            Line("  %group_base = mul i64 %group64, %size64");
            Line("  %worker = add i64 %group_base, %local64");
        }

        private void AppendDispatch()
        {
            Line("dispatch:");
            string status = LoadHeader(WarpLogicalMachineLayout.StatusOffset);
            string runnable = Assign($"icmp eq i32 {status}, {N(WarpLogicalMachineLayout.Runnable)}");
            Line($"  br i1 {runnable}, label %dispatch_frame, label %done");
            Line("dispatch_frame:");
            Line($"  %depth = load i32, ptr addrspace(1) {HeaderPointer(WarpLogicalMachineLayout.DepthOffset)}, align 4");
            Line("  %frame_number = sub i32 %depth, 1");
            Line("  %frame_number64 = zext i32 %frame_number to i64");
            Line($"  %frame_delta = mul i64 %frame_number64, {N(layout.FrameWords)}");
            Line($"  %frame_offset = add i64 %frame_delta, {N(WarpLogicalMachineLayout.HeaderWords)}");
            Line("  %frame = getelementptr i32, ptr addrspace(1) %state, i64 %frame_offset");
            string pc = LoadFrame(WarpLogicalMachineLayout.FrameProgramCounterOffset);
            Line($"  switch i32 {pc}, label %invalid_state [");
            foreach (WarpLogicalMachineNode node in layout.Nodes)
            {
                Line($"    i32 {N(node.ProgramCounter)}, label %node_{N(node.ProgramCounter)}");
            }

            Line("  ]");
            Line("invalid_state:");
            StoreHeader(WarpLogicalMachineLayout.FaultKindOffset, "3");
            StoreHeader(WarpLogicalMachineLayout.StatusOffset, N(WarpLogicalMachineLayout.Faulted));
            Line("  br label %done");
        }

        private void AppendNode(WarpLogicalMachineNode node)
        {
            Line($"node_{N(node.ProgramCounter)}:");
            if (node.StartsBlock)
            {
                AppendCharge(node);
            }

            foreach (WarpIrInstruction instruction in node.Instructions)
            {
                string value = EmitInstruction(instruction);
                StoreValue(instruction.Result, value);
            }

            if (node.Call is WarpIrInstruction call)
            {
                AppendCall(node, call);
            }
            else
            {
                AppendTerminator(node);
            }
        }

        private void AppendCharge(WarpLogicalMachineNode node)
        {
            string low = Assign($"zext i32 {LoadHeader(WarpLogicalMachineLayout.RemainingStepsLowOffset)} to i64");
            string high = Assign($"zext i32 {LoadHeader(WarpLogicalMachineLayout.RemainingStepsHighOffset)} to i64");
            high = Assign($"shl i64 {high}, 32");
            string remaining = Assign($"or i64 {low}, {high}");
            string exhausted = Assign($"icmp ult i64 {remaining}, {N(node.BlockCost)}");
            string suffix = N(node.ProgramCounter);
            Line($"  br i1 {exhausted}, label %step_fault_{suffix}, label %quantum_check_{suffix}");
            Line($"step_fault_{suffix}:");
            AppendFault(node, WarpLogicalMachineLayout.StepLimitFault);
            Line($"quantum_check_{suffix}:");
            string quantum = Assign("load i32, ptr %quantum_ptr, align 4");
            string yield = Assign($"icmp ult i32 {quantum}, {N(node.BlockCost)}");
            Line($"  br i1 {yield}, label %done, label %charged_{suffix}");
            Line($"charged_{suffix}:");
            string newQuantum = Assign($"sub i32 {quantum}, {N(node.BlockCost)}");
            Line($"  store i32 {newQuantum}, ptr %quantum_ptr, align 4");
            string newRemaining = Assign($"sub i64 {remaining}, {N(node.BlockCost)}");
            StoreHeader(WarpLogicalMachineLayout.RemainingStepsLowOffset, Assign($"trunc i64 {newRemaining} to i32"));
            string newHigh = Assign($"lshr i64 {newRemaining}, 32");
            StoreHeader(WarpLogicalMachineLayout.RemainingStepsHighOffset, Assign($"trunc i64 {newHigh} to i32"));
        }

        private string EmitInstruction(WarpIrInstruction instruction)
        {
            if (instruction.OpCode == WarpIrOpCode.Constant)
            {
                return N(instruction.Immediate);
            }

            if (instruction.OpCode == WarpIrOpCode.LoadScalar)
            {
                return $"%warp_scalar_{N(instruction.Immediate)}";
            }

            if (instruction.OpCode == WarpIrOpCode.LoadArgument)
            {
                return LoadFrame(checked(layout.ArgumentOffset + (int)instruction.Immediate));
            }

            if (instruction.OpCode == WarpIrOpCode.LoadInput)
            {
                string pointer = Assign($"getelementptr i32, ptr addrspace(1) %warp_input_{N(instruction.Immediate)}, i64 %input_index");
                return Assign($"load i32, ptr addrspace(1) {pointer}, align 4");
            }

            string left = LoadValue(instruction.Left);
            if (instruction.OpCode == WarpIrOpCode.BitwiseNot)
            {
                return Assign($"xor i32 {left}, -1");
            }

            string right = LoadValue(instruction.Right);
            if (instruction.OpCode == WarpIrOpCode.Select)
            {
                string condition = Assign($"icmp ne i32 {left}, 0");
                return Assign($"select i1 {condition}, i32 {right}, i32 {LoadValue(instruction.Third)}");
            }

            return EmitBinary(instruction.OpCode, left, right);
        }

        private string EmitBinary(WarpIrOpCode opCode, string left, string right)
        {
            string? instruction = opCode switch
            {
                WarpIrOpCode.Add => "add",
                WarpIrOpCode.Subtract => "sub",
                WarpIrOpCode.Multiply => "mul",
                WarpIrOpCode.BitwiseAnd => "and",
                WarpIrOpCode.BitwiseOr => "or",
                WarpIrOpCode.ExclusiveOr => "xor",
                WarpIrOpCode.ShiftLeft => "shl",
                WarpIrOpCode.ShiftRightLogical => "lshr",
                _ => null,
            };
            if (instruction is not null)
            {
                if (opCode is WarpIrOpCode.ShiftLeft or WarpIrOpCode.ShiftRightLogical)
                {
                    right = Assign($"and i32 {right}, 31");
                }

                return Assign($"{instruction} i32 {left}, {right}");
            }

            string predicate = opCode switch
            {
                WarpIrOpCode.Equal => "eq",
                WarpIrOpCode.NotEqual => "ne",
                WarpIrOpCode.LessThanUnsigned => "ult",
                WarpIrOpCode.LessThanOrEqualUnsigned => "ule",
                WarpIrOpCode.GreaterThanUnsigned => "ugt",
                WarpIrOpCode.GreaterThanOrEqualUnsigned => "uge",
                _ => throw new ArgumentOutOfRangeException(nameof(opCode)),
            };
            string comparison = Assign($"icmp {predicate} i32 {left}, {right}");
            return Assign($"zext i1 {comparison} to i32");
        }

        private void AppendCall(WarpLogicalMachineNode node, WarpIrInstruction call)
        {
            string exhausted = Assign("icmp uge i32 %depth, %warp_max_depth");
            string suffix = N(node.ProgramCounter);
            Line($"  br i1 {exhausted}, label %stack_fault_{suffix}, label %call_{suffix}");
            Line($"stack_fault_{suffix}:");
            AppendFault(node, WarpLogicalMachineLayout.CallDepthFault);
            Line($"call_{suffix}:");
            string[] arguments = call.Arguments.Select(LoadValue).ToArray();
            StoreFrame(WarpLogicalMachineLayout.FrameProgramCounterOffset, N(node.Continuation));
            string callee = Assign($"getelementptr i32, ptr addrspace(1) %frame, i64 {N(layout.FrameWords)}");
            StoreAt(callee, WarpLogicalMachineLayout.FrameFunctionOffset, N(call.Callee + 1));
            StoreAt(callee, WarpLogicalMachineLayout.FrameProgramCounterOffset, N(layout.GetBlockEntry(call.Callee + 1, 0)));
            StoreAt(callee, WarpLogicalMachineLayout.FrameReturnValueOffset, N(call.Result));
            for (int index = 0; index < arguments.Length; index++)
            {
                StoreAt(callee, layout.ArgumentOffset + index, arguments[index]);
            }

            StoreHeader(WarpLogicalMachineLayout.DepthOffset, Assign("add i32 %depth, 1"));
            Line("  br label %dispatch");
        }

        private void AppendTerminator(WarpLogicalMachineNode node)
        {
            switch (node.Terminator)
            {
                case WarpBranchTerminator branch:
                    AppendBranch(node.Function, branch.Target);
                    break;
                case WarpConditionalBranchTerminator conditional:
                    string predicate = Assign($"icmp ne i32 {LoadValue(conditional.Condition)}, 0");
                    string suffix = N(node.ProgramCounter);
                    Line($"  br i1 {predicate}, label %true_{suffix}, label %false_{suffix}");
                    Line($"true_{suffix}:");
                    AppendBranch(node.Function, conditional.WhenNonZero);
                    Line($"false_{suffix}:");
                    AppendBranch(node.Function, conditional.WhenZero);
                    break;
                case WarpReturnTerminator result:
                    AppendReturn(node, result);
                    break;
                default:
                    throw new InvalidOperationException("The verified machine has an unknown terminator.");
            }
        }

        private void AppendBranch(int function, WarpBranchTarget target)
        {
            string[] arguments = target.Arguments.Select(LoadValue).ToArray();
            var blocks = function == 0 ? layout.Kernel.Blocks : layout.Kernel.Functions[function - 1].Blocks;
            var parameters = blocks[target.Block].Parameters;
            for (int index = 0; index < arguments.Length; index++)
            {
                StoreValue(parameters[index].Value, arguments[index]);
            }

            StoreFrame(WarpLogicalMachineLayout.FrameProgramCounterOffset, N(layout.GetBlockEntry(function, target.Block)));
            Line("  br label %dispatch");
        }

        private void AppendReturn(WarpLogicalMachineNode node, WarpReturnTerminator result)
        {
            string value = LoadValue(result.Value);
            if (node.Function == 0)
            {
                StoreHeader(WarpLogicalMachineLayout.ResultOffset, value);
                StoreHeader(WarpLogicalMachineLayout.StatusOffset, N(WarpLogicalMachineLayout.Completed));
                Line("  br label %done");
                return;
            }

            string returnValue = LoadFrame(WarpLogicalMachineLayout.FrameReturnValueOffset);
            string caller = Assign($"getelementptr i32, ptr addrspace(1) %frame, i64 -{N(layout.FrameWords)}");
            string offset = Assign($"add i32 {returnValue}, {N(WarpLogicalMachineLayout.FrameHeaderWords)}");
            string offset64 = Assign($"zext i32 {offset} to i64");
            string pointer = Assign($"getelementptr i32, ptr addrspace(1) {caller}, i64 {offset64}");
            Line($"  store i32 {value}, ptr addrspace(1) {pointer}, align 4");
            StoreHeader(WarpLogicalMachineLayout.DepthOffset, Assign("sub i32 %depth, 1"));
            Line("  br label %dispatch");
        }

        private void AppendFault(WarpLogicalMachineNode node, uint kind)
        {
            StoreHeader(WarpLogicalMachineLayout.FaultKindOffset, N(kind));
            StoreHeader(WarpLogicalMachineLayout.FaultFunctionOffset, N(node.Function));
            StoreHeader(WarpLogicalMachineLayout.FaultBlockOffset, N(node.Block));
            StoreHeader(WarpLogicalMachineLayout.StatusOffset, N(WarpLogicalMachineLayout.Faulted));
            Line("  br label %done");
        }

        private string LoadHeader(int offset) => Assign($"load i32, ptr addrspace(1) {HeaderPointer(offset)}, align 4");

        private string HeaderPointer(int offset) => Pointer("%state", offset);

        private void StoreHeader(int offset, string value) => StoreAt("%state", offset, value);

        private string LoadValue(int value) => LoadFrame(WarpLogicalMachineLayout.FrameHeaderWords + value);

        private void StoreValue(int value, string operand) => StoreFrame(WarpLogicalMachineLayout.FrameHeaderWords + value, operand);

        private string LoadFrame(int offset) => Assign($"load i32, ptr addrspace(1) {Pointer("%frame", offset)}, align 4");

        private void StoreFrame(int offset, string value) => StoreAt("%frame", offset, value);

        private void StoreAt(string address, int offset, string value)
        {
            string pointer = Pointer(address, offset);
            Line($"  store i32 {value}, ptr addrspace(1) {pointer}, align 4");
        }

        private string Pointer(string address, int offset) => Assign($"getelementptr i32, ptr addrspace(1) {address}, i64 {N(offset)}");

        private string Assign(string instruction)
        {
            string name = $"%t{N(temporary++)}";
            Line($"  {name} = {instruction}");
            return name;
        }

        private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string N(uint value) => value.ToString(CultureInfo.InvariantCulture);

        private void Line(string line) => source.AppendLine(line);
    }
}
