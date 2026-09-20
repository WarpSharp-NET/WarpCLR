using System.Globalization;
using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Backend.SPIRV;

public sealed class SPIRVBackendCompiler : IWarpBackendCompiler
{
    private static readonly WarpBackendContract DeclaredContract = CreateContract();

    public WarpBackendKind Backend => WarpBackendKind.SPIRV;

    public WarpBackendContract Contract => DeclaredContract;

    private static WarpBackendContract CreateContract() => new(
        WarpProfileCatalog.ProfileId,
        [
            WarpIrOpCode.LoadInput,
            WarpIrOpCode.LoadScalar,
            WarpIrOpCode.Constant,
            WarpIrOpCode.BitwiseNot,
            WarpIrOpCode.Add,
            WarpIrOpCode.Subtract,
            WarpIrOpCode.Multiply,
            WarpIrOpCode.BitwiseAnd,
            WarpIrOpCode.BitwiseOr,
            WarpIrOpCode.ExclusiveOr,
            WarpIrOpCode.ShiftLeft,
            WarpIrOpCode.ShiftRightLogical,
            WarpIrOpCode.Equal,
            WarpIrOpCode.NotEqual,
            WarpIrOpCode.LessThanUnsigned,
            WarpIrOpCode.LessThanOrEqualUnsigned,
            WarpIrOpCode.GreaterThanUnsigned,
            WarpIrOpCode.GreaterThanOrEqualUnsigned,
            WarpIrOpCode.Select,
        ],
        [
            WarpControlFlowOperation.BlockArguments,
            WarpControlFlowOperation.Branch,
            WarpControlFlowOperation.ConditionalBranch,
            WarpControlFlowOperation.Return,
        ],
        [
            WarpReductionOperation.WrappingSum,
            WarpReductionOperation.Minimum,
            WarpReductionOperation.Maximum,
        ]);

    public WarpBackendArtifact Compile(WarpControlFlowKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        if (kernel.Reduction.HasValue)
        {
            return CompileReduction(kernel);
        }

        var llvm = new StringBuilder();
        llvm.Append("; ").AppendLine(WarpDeviceAbi.DevelopmentConformanceMarker);
        llvm.AppendLine("target triple = \"spirv64-unknown-unknown\"");
        llvm.AppendLine();
        llvm.AppendLine("declare spir_func i64 @_Z13get_global_idj(i32) #1");
        llvm.AppendLine();
        llvm.Append("define spir_kernel void @")
            .Append(WarpDeviceAbi.IntegerMapEntryPoint)
            .AppendLine("(");
        AppendParameters(llvm, kernel);
        llvm.AppendLine(") #0 {");
        llvm.AppendLine("entry:");
        llvm.AppendLine("  %warp_index = call spir_func i64 @_Z13get_global_idj(i32 0)");
        llvm.AppendLine("  %warp_count_64 = zext i32 %warp_count to i64");
        llvm.AppendLine("  %warp_in_range = icmp ult i64 %warp_index, %warp_count_64");
        llvm.AppendLine("  br i1 %warp_in_range, label %body, label %done");
        llvm.AppendLine();
        llvm.AppendLine("body:");
        llvm.AppendLine("  br label %warp_block_0");
        llvm.AppendLine();
        AppendControlFlow(llvm, kernel, "%warp_index");
        llvm.AppendLine("warp_kernel_return:");
        AppendReturnPhi(llvm, kernel);
        llvm.AppendLine("  %warp_output_ptr = getelementptr i32, ptr addrspace(1) %warp_output, i64 %warp_index");
        llvm.AppendLine("  store i32 %warp_kernel_result, ptr addrspace(1) %warp_output_ptr, align 4");
        llvm.AppendLine("  br label %done");
        llvm.AppendLine();
        llvm.AppendLine("done:");
        llvm.AppendLine("  ret void");
        llvm.AppendLine("}");
        llvm.AppendLine();
        llvm.AppendLine("attributes #0 = { nounwind }");
        llvm.AppendLine("attributes #1 = { convergent nounwind }");

        return new WarpBackendArtifact(
            Backend,
            WarpArtifactFormat.SPIRVLLVMIR,
            WarpDeviceAbi.IntegerMapEntryPoint,
            Encoding.UTF8.GetBytes(llvm.ToString()));
    }

    private WarpBackendArtifact CompileReduction(WarpControlFlowKernel kernel)
    {
        WarpReductionOperation operation = kernel.Reduction
            ?? throw new ArgumentException("A reduction operation is required.", nameof(kernel));
        uint identity = WarpReductionContract.GetDescriptor(operation).Identity;

        var llvm = new StringBuilder();
        llvm.Append("; ").AppendLine(WarpDeviceAbi.DevelopmentConformanceMarker);
        llvm.AppendLine("target triple = \"spirv64-unknown-unknown\"");
        llvm.AppendLine();
        llvm.AppendLine("declare spir_func i64 @_Z13get_global_idj(i32) #1");
        llvm.AppendLine();
        llvm.Append("define spir_kernel void @")
            .Append(WarpDeviceAbi.IntegerReductionEntryPoint)
            .AppendLine("(");
        AppendParameters(llvm, kernel);
        llvm.AppendLine(") #0 {");
        llvm.AppendLine("entry:");
        llvm.AppendLine("  %warp_global_index = call spir_func i64 @_Z13get_global_idj(i32 0)");
        llvm.AppendLine("  %warp_is_leader = icmp eq i64 %warp_global_index, 0");
        llvm.AppendLine("  br i1 %warp_is_leader, label %leader, label %done");
        llvm.AppendLine();
        llvm.AppendLine("leader:");
        llvm.AppendLine("  %warp_count_64 = zext i32 %warp_count to i64");
        llvm.AppendLine("  br label %reduce_loop");
        llvm.AppendLine();
        llvm.AppendLine("reduce_loop:");
        llvm.AppendLine("  %warp_reduce_index = phi i64 [ 0, %leader ], [ %warp_next_index, %warp_kernel_return ]");
        llvm.Append("  %warp_accumulator = phi i32 [ ")
            .Append(Signed(identity))
            .AppendLine(", %leader ], [ %warp_next_accumulator, %warp_kernel_return ]");
        llvm.AppendLine("  %warp_has_item = icmp ult i64 %warp_reduce_index, %warp_count_64");
        llvm.AppendLine("  br i1 %warp_has_item, label %reduce_body, label %reduce_store");
        llvm.AppendLine();
        llvm.AppendLine("reduce_body:");
        llvm.AppendLine("  br label %warp_block_0");
        llvm.AppendLine();
        AppendControlFlow(llvm, kernel, "%warp_reduce_index");
        llvm.AppendLine("warp_kernel_return:");
        AppendReturnPhi(llvm, kernel);
        AppendReduction(llvm, operation, "%warp_kernel_result");
        llvm.AppendLine("  %warp_next_index = add i64 %warp_reduce_index, 1");
        llvm.AppendLine("  br label %reduce_loop");
        llvm.AppendLine();
        llvm.AppendLine("reduce_store:");
        llvm.AppendLine("  store i32 %warp_accumulator, ptr addrspace(1) %warp_output, align 4");
        llvm.AppendLine("  br label %done");
        llvm.AppendLine();
        llvm.AppendLine("done:");
        llvm.AppendLine("  ret void");
        llvm.AppendLine("}");
        llvm.AppendLine();
        llvm.AppendLine("attributes #0 = { nounwind }");
        llvm.AppendLine("attributes #1 = { convergent nounwind }");

        return new WarpBackendArtifact(
            Backend,
            WarpArtifactFormat.SPIRVLLVMIR,
            WarpDeviceAbi.IntegerReductionEntryPoint,
            Encoding.UTF8.GetBytes(llvm.ToString()));
    }

    private static void AppendParameters(StringBuilder llvm, WarpControlFlowKernel kernel)
    {
        var parameters = new List<string>(
            kernel.InputBufferCount + kernel.ScalarArgumentCount + 2);

        for (int inputIndex = 0; inputIndex < kernel.InputBufferCount; inputIndex++)
        {
            parameters.Add($"ptr addrspace(1) %warp_input_{Invariant(inputIndex)}");
        }

        parameters.Add("ptr addrspace(1) %warp_output");
        parameters.Add("i32 %warp_count");

        for (int scalarIndex = 0; scalarIndex < kernel.ScalarArgumentCount; scalarIndex++)
        {
            parameters.Add($"i32 %warp_scalar_{Invariant(scalarIndex)}");
        }

        for (int index = 0; index < parameters.Count; index++)
        {
            llvm.Append("    ")
                .Append(parameters[index])
                .AppendLine(index + 1 == parameters.Count ? string.Empty : ",");
        }
    }

    private static void AppendControlFlow(
        StringBuilder llvm,
        WarpControlFlowKernel kernel,
        string indexValue)
    {
        IReadOnlyDictionary<int, IReadOnlyList<IncomingEdge>> incoming = GetIncomingEdges(kernel);
        foreach (WarpBasicBlock block in kernel.Blocks)
        {
            llvm.Append("warp_block_").Append(Invariant(block.Id)).AppendLine(":");
            for (int parameterIndex = 0; parameterIndex < block.Parameters.Count; parameterIndex++)
            {
                WarpBlockParameter parameter = block.Parameters[parameterIndex];
                llvm.Append("  ")
                    .Append(Value(parameter.Value))
                    .Append(" = phi i32 ");
                IReadOnlyList<IncomingEdge> edges = incoming[block.Id];
                for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
                {
                    if (edgeIndex != 0)
                    {
                        llvm.Append(", ");
                    }

                    IncomingEdge edge = edges[edgeIndex];
                    llvm.Append("[ ")
                        .Append(Value(edge.Target.Arguments[parameterIndex]))
                        .Append(", %warp_block_")
                        .Append(Invariant(edge.SourceBlock))
                        .Append(" ]");
                }

                llvm.AppendLine();
            }

            foreach (WarpIrInstruction instruction in block.Instructions)
            {
                AppendInstruction(llvm, instruction, indexValue);
            }

            AppendTerminator(llvm, block);
            llvm.AppendLine();
        }
    }

    private static void AppendTerminator(StringBuilder llvm, WarpBasicBlock block)
    {
        switch (block.Terminator)
        {
            case WarpBranchTerminator branch:
                llvm.Append("  br label %warp_block_")
                    .Append(Invariant(branch.Target.Block))
                    .AppendLine();
                break;

            case WarpConditionalBranchTerminator conditional:
                llvm.Append("  %warp_branch_condition_")
                    .Append(Invariant(block.Id))
                    .Append(" = icmp ne i32 ")
                    .Append(Value(conditional.Condition))
                    .AppendLine(", 0");
                llvm.Append("  br i1 %warp_branch_condition_")
                    .Append(Invariant(block.Id))
                    .Append(", label %warp_block_")
                    .Append(Invariant(conditional.WhenNonZero.Block))
                    .Append(", label %warp_block_")
                    .Append(Invariant(conditional.WhenZero.Block))
                    .AppendLine();
                break;

            case WarpReturnTerminator:
                llvm.AppendLine("  br label %warp_kernel_return");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(block));
        }
    }

    private static void AppendReturnPhi(StringBuilder llvm, WarpControlFlowKernel kernel)
    {
        WarpBasicBlock[] returns = kernel.Blocks
            .Where(block => block.Terminator is WarpReturnTerminator)
            .ToArray();
        llvm.Append("  %warp_kernel_result = phi i32 ");
        for (int index = 0; index < returns.Length; index++)
        {
            if (index != 0)
            {
                llvm.Append(", ");
            }

            var terminator = (WarpReturnTerminator)returns[index].Terminator;
            llvm.Append("[ ")
                .Append(Value(terminator.Value))
                .Append(", %warp_block_")
                .Append(Invariant(returns[index].Id))
                .Append(" ]");
        }

        llvm.AppendLine();
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<IncomingEdge>> GetIncomingEdges(
        WarpControlFlowKernel kernel)
    {
        var result = kernel.Blocks.ToDictionary(
            block => block.Id,
            _ => new List<IncomingEdge>());
        foreach (WarpBasicBlock source in kernel.Blocks)
        {
            switch (source.Terminator)
            {
                case WarpBranchTerminator branch:
                    result[branch.Target.Block].Add(new IncomingEdge(source.Id, branch.Target));
                    break;

                case WarpConditionalBranchTerminator conditional:
                    result[conditional.WhenNonZero.Block].Add(
                        new IncomingEdge(source.Id, conditional.WhenNonZero));
                    result[conditional.WhenZero.Block].Add(
                        new IncomingEdge(source.Id, conditional.WhenZero));
                    break;
            }
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<IncomingEdge>)pair.Value);
    }

    private static void AppendInstruction(
        StringBuilder llvm,
        WarpIrInstruction instruction,
        string indexValue)
    {
        string result = Value(instruction.Result);
        string left = Value(instruction.Left);
        string right = Value(instruction.Right);
        string third = Value(instruction.Third);

        switch (instruction.OpCode)
        {
            case WarpIrOpCode.LoadInput:
                llvm.Append("  %warp_input_ptr_")
                    .Append(Invariant(instruction.Result))
                    .Append(" = getelementptr i32, ptr addrspace(1) %warp_input_")
                    .Append(Invariant(instruction.Immediate))
                    .Append(", i64 ")
                    .AppendLine(indexValue);
                llvm.Append("  ")
                    .Append(result)
                    .Append(" = load i32, ptr addrspace(1) %warp_input_ptr_")
                    .Append(Invariant(instruction.Result))
                    .AppendLine(", align 4");
                break;

            case WarpIrOpCode.LoadScalar:
                llvm.Append("  ")
                    .Append(result)
                    .Append(" = add i32 0, %warp_scalar_")
                    .Append(Invariant(instruction.Immediate))
                    .AppendLine();
                break;

            case WarpIrOpCode.Constant:
                llvm.Append("  ")
                    .Append(result)
                    .Append(" = add i32 0, ")
                    .Append(Signed(instruction.Immediate))
                    .AppendLine();
                break;

            case WarpIrOpCode.BitwiseNot:
                AppendBinary(llvm, result, "xor", left, "-1");
                break;

            case WarpIrOpCode.Add:
                AppendBinary(llvm, result, "add", left, right);
                break;

            case WarpIrOpCode.Subtract:
                AppendBinary(llvm, result, "sub", left, right);
                break;

            case WarpIrOpCode.Multiply:
                AppendBinary(llvm, result, "mul", left, right);
                break;

            case WarpIrOpCode.BitwiseAnd:
                AppendBinary(llvm, result, "and", left, right);
                break;

            case WarpIrOpCode.BitwiseOr:
                AppendBinary(llvm, result, "or", left, right);
                break;

            case WarpIrOpCode.ExclusiveOr:
                AppendBinary(llvm, result, "xor", left, right);
                break;

            case WarpIrOpCode.ShiftLeft:
                AppendShift(llvm, instruction.Result, "shl", left, right);
                break;

            case WarpIrOpCode.ShiftRightLogical:
                AppendShift(llvm, instruction.Result, "lshr", left, right);
                break;

            case WarpIrOpCode.Equal:
                AppendComparison(llvm, instruction.Result, "eq", left, right);
                break;

            case WarpIrOpCode.NotEqual:
                AppendComparison(llvm, instruction.Result, "ne", left, right);
                break;

            case WarpIrOpCode.LessThanUnsigned:
                AppendComparison(llvm, instruction.Result, "ult", left, right);
                break;

            case WarpIrOpCode.LessThanOrEqualUnsigned:
                AppendComparison(llvm, instruction.Result, "ule", left, right);
                break;

            case WarpIrOpCode.GreaterThanUnsigned:
                AppendComparison(llvm, instruction.Result, "ugt", left, right);
                break;

            case WarpIrOpCode.GreaterThanOrEqualUnsigned:
                AppendComparison(llvm, instruction.Result, "uge", left, right);
                break;

            case WarpIrOpCode.Select:
                AppendConditionalSelection(
                    llvm,
                    instruction.Result,
                    left,
                    right,
                    third);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(instruction));
        }
    }

    private static void AppendReduction(
        StringBuilder llvm,
        WarpReductionOperation operation,
        string value)
    {
        switch (operation)
        {
            case WarpReductionOperation.WrappingSum:
                AppendBinary(
                    llvm,
                    "%warp_next_accumulator",
                    "add",
                    "%warp_accumulator",
                    value);
                break;

            case WarpReductionOperation.Minimum:
                llvm.Append("  %warp_reduce_compare = icmp ult i32 ")
                    .Append(value)
                    .AppendLine(", %warp_accumulator");
                AppendSelection(llvm, value);
                break;

            case WarpReductionOperation.Maximum:
                llvm.Append("  %warp_reduce_compare = icmp ugt i32 ")
                    .Append(value)
                    .AppendLine(", %warp_accumulator");
                AppendSelection(llvm, value);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static void AppendSelection(StringBuilder llvm, string value)
    {
        llvm.Append("  %warp_next_accumulator = select i1 %warp_reduce_compare, i32 ")
            .Append(value)
            .AppendLine(", i32 %warp_accumulator");
    }

    private static void AppendBinary(
        StringBuilder llvm,
        string result,
        string opcode,
        string left,
        string right)
    {
        llvm.Append("  ")
            .Append(result)
            .Append(" = ")
            .Append(opcode)
            .Append(" i32 ")
            .Append(left)
            .Append(", ")
            .Append(right)
            .AppendLine();
    }

    private static void AppendShift(
        StringBuilder llvm,
        int resultIndex,
        string opcode,
        string left,
        string right)
    {
        string shift = $"%warp_shift_{Invariant(resultIndex)}";
        AppendBinary(llvm, shift, "and", right, "31");
        AppendBinary(llvm, Value(resultIndex), opcode, left, shift);
    }

    private static void AppendComparison(
        StringBuilder llvm,
        int resultIndex,
        string predicate,
        string left,
        string right)
    {
        string comparison = $"%warp_compare_{Invariant(resultIndex)}";
        llvm.Append("  ")
            .Append(comparison)
            .Append(" = icmp ")
            .Append(predicate)
            .Append(" i32 ")
            .Append(left)
            .Append(", ")
            .AppendLine(right);
        llvm.Append("  ")
            .Append(Value(resultIndex))
            .Append(" = zext i1 ")
            .Append(comparison)
            .AppendLine(" to i32");
    }

    private static void AppendConditionalSelection(
        StringBuilder llvm,
        int resultIndex,
        string condition,
        string whenNonZero,
        string whenZero)
    {
        string comparison = $"%warp_select_condition_{Invariant(resultIndex)}";
        llvm.Append("  ")
            .Append(comparison)
            .Append(" = icmp ne i32 ")
            .Append(condition)
            .AppendLine(", 0");
        llvm.Append("  ")
            .Append(Value(resultIndex))
            .Append(" = select i1 ")
            .Append(comparison)
            .Append(", i32 ")
            .Append(whenNonZero)
            .Append(", i32 ")
            .AppendLine(whenZero);
    }

    private static string Value(int result) => $"%warp_v{Invariant(result)}";

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(uint value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Signed(uint value) =>
        unchecked((int)value).ToString(CultureInfo.InvariantCulture);

    private sealed record IncomingEdge(int SourceBlock, WarpBranchTarget Target);
}
