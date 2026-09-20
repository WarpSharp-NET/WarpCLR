using System.Globalization;
using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Backend.NVPTX;

public sealed class NVPTXBackendCompiler : IWarpBackendCompiler
{
    private const int FirstValueRegister = 5;
    private static readonly WarpBackendContract DeclaredContract = CreateContract();

    public WarpBackendKind Backend => WarpBackendKind.NVPTX;

    public WarpBackendContract Contract => DeclaredContract;

    private static WarpBackendContract CreateContract() => new(
        WarpProfileCatalog.ProfileId,
        [
            WarpIrOpCode.LoadInput,
            WarpIrOpCode.LoadScalar,
            WarpIrOpCode.LoadArgument,
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
            WarpIrOpCode.Call,
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

        int shiftRegister = FirstValueRegister + kernel.ValueCount;
        int kernelResultRegister = shiftRegister + 1;
        int copyTemporaryRegister = kernelResultRegister + 1;
        int registerCount = copyTemporaryRegister + GetMaximumParameterCount(kernel);
        int firstInputBaseRegister = 2;
        int addressRegister = firstInputBaseRegister + kernel.InputBufferCount;
        int addressRegisterCount = addressRegister + 1;

        var ptx = new StringBuilder();
        ptx.AppendLine(".version 6.0");
        ptx.AppendLine(".target sm_50");
        ptx.AppendLine(".address_size 64");
        ptx.Append("// ").AppendLine(WarpDeviceAbi.DevelopmentConformanceMarker);
        ptx.AppendLine();
        AppendFunctions(ptx, kernel);
        ptx.Append(".visible .entry ")
            .Append(WarpDeviceAbi.IntegerMapEntryPoint)
            .AppendLine("(");
        AppendParameters(ptx, kernel);
        ptx.AppendLine(")");
        ptx.AppendLine("{");
        ptx.AppendLine("    .reg .pred %p<2>;");
        ptx.Append("    .reg .b32 %r<")
            .Append(Invariant(registerCount))
            .AppendLine(">;");
        ptx.Append("    .reg .b64 %rd<")
            .Append(Invariant(addressRegisterCount))
            .AppendLine(">;");
        AppendCallParameterStorage(ptx, kernel.Instructions);
        ptx.AppendLine();
        ptx.AppendLine("    mov.u32 %r0, %tid.x;");
        ptx.AppendLine("    mov.u32 %r1, %ctaid.x;");
        ptx.AppendLine("    mov.u32 %r2, %ntid.x;");
        ptx.AppendLine("    mad.lo.u32 %r3, %r1, %r2, %r0;");
        ptx.AppendLine("    ld.param.u32 %r4, [warp_count];");
        ptx.AppendLine("    setp.ge.u32 %p0, %r3, %r4;");
        ptx.AppendLine("    @%p0 bra warp_done;");
        ptx.AppendLine();
        ptx.AppendLine("    mul.wide.u32 %rd0, %r3, 4;");
        ptx.AppendLine("    ld.param.u64 %rd1, [warp_output];");

        for (int inputIndex = 0; inputIndex < kernel.InputBufferCount; inputIndex++)
        {
            ptx.Append("    ld.param.u64 %rd")
                .Append(Invariant(firstInputBaseRegister + inputIndex))
                .Append(", [warp_input_")
                .Append(Invariant(inputIndex))
                .AppendLine("];");
        }

        ptx.AppendLine();
        ptx.AppendLine("    bra warp_block_0;");
        ptx.AppendLine();
        AppendControlFlow(
            ptx,
            kernel.Blocks,
            firstInputBaseRegister,
            addressRegister,
            shiftRegister,
            kernelResultRegister,
            copyTemporaryRegister,
            "warp",
            "warp_kernel_return");
        ptx.AppendLine("warp_kernel_return:");
        ptx.AppendLine();
        ptx.Append("    add.u64 %rd")
            .Append(Invariant(addressRegister))
            .AppendLine(", %rd1, %rd0;");
        ptx.Append("    st.global.u32 [%rd")
            .Append(Invariant(addressRegister))
            .Append("], %r")
            .Append(Invariant(kernelResultRegister))
            .AppendLine(";");
        ptx.AppendLine("warp_done:");
        ptx.AppendLine("    ret;");
        ptx.AppendLine("}");

        return new WarpBackendArtifact(
            Backend,
            WarpArtifactFormat.NVPTX,
            WarpDeviceAbi.IntegerMapEntryPoint,
            Encoding.UTF8.GetBytes(ptx.ToString()));
    }

    private WarpBackendArtifact CompileReduction(WarpControlFlowKernel kernel)
    {
        WarpReductionOperation operation = kernel.Reduction
            ?? throw new ArgumentException("A reduction operation is required.", nameof(kernel));
        int shiftRegister = FirstValueRegister + kernel.ValueCount;
        int accumulatorRegister = shiftRegister + 1;
        int kernelResultRegister = accumulatorRegister + 1;
        int copyTemporaryRegister = kernelResultRegister + 1;
        int registerCount = copyTemporaryRegister + GetMaximumParameterCount(kernel);
        int firstInputBaseRegister = 2;
        int addressRegister = firstInputBaseRegister + kernel.InputBufferCount;
        int addressRegisterCount = addressRegister + 1;

        var ptx = new StringBuilder();
        ptx.AppendLine(".version 6.0");
        ptx.AppendLine(".target sm_50");
        ptx.AppendLine(".address_size 64");
        ptx.Append("// ").AppendLine(WarpDeviceAbi.DevelopmentConformanceMarker);
        ptx.AppendLine();
        AppendFunctions(ptx, kernel);
        ptx.Append(".visible .entry ")
            .Append(WarpDeviceAbi.IntegerReductionEntryPoint)
            .AppendLine("(");
        AppendParameters(ptx, kernel);
        ptx.AppendLine(")");
        ptx.AppendLine("{");
        ptx.AppendLine("    .reg .pred %p<2>;");
        ptx.Append("    .reg .b32 %r<")
            .Append(Invariant(registerCount))
            .AppendLine(">;");
        ptx.Append("    .reg .b64 %rd<")
            .Append(Invariant(addressRegisterCount))
            .AppendLine(">;");
        AppendCallParameterStorage(ptx, kernel.Instructions);
        ptx.AppendLine();
        ptx.AppendLine("    mov.u32 %r0, %tid.x;");
        ptx.AppendLine("    mov.u32 %r1, %ctaid.x;");
        ptx.AppendLine("    setp.ne.u32 %p0, %r0, 0;");
        ptx.AppendLine("    @%p0 bra warp_reduce_done;");
        ptx.AppendLine("    setp.ne.u32 %p0, %r1, 0;");
        ptx.AppendLine("    @%p0 bra warp_reduce_done;");
        ptx.AppendLine("    ld.param.u32 %r4, [warp_count];");
        ptx.AppendLine("    ld.param.u64 %rd1, [warp_output];");

        for (int inputIndex = 0; inputIndex < kernel.InputBufferCount; inputIndex++)
        {
            ptx.Append("    ld.param.u64 %rd")
                .Append(Invariant(firstInputBaseRegister + inputIndex))
                .Append(", [warp_input_")
                .Append(Invariant(inputIndex))
                .AppendLine("];");
        }

        ptx.Append("    mov.u32 %r")
            .Append(Invariant(accumulatorRegister))
            .Append(", ")
            .Append(Invariant(WarpReductionContract.GetDescriptor(operation).Identity))
            .AppendLine(";");
        ptx.AppendLine("    mov.u32 %r3, 0;");
        ptx.AppendLine();
        ptx.AppendLine("warp_reduce_loop:");
        ptx.AppendLine("    setp.ge.u32 %p0, %r3, %r4;");
        ptx.AppendLine("    @%p0 bra warp_reduce_store;");
        ptx.AppendLine("    mul.wide.u32 %rd0, %r3, 4;");
        ptx.AppendLine("    bra warp_block_0;");
        ptx.AppendLine();
        AppendControlFlow(
            ptx,
            kernel.Blocks,
            firstInputBaseRegister,
            addressRegister,
            shiftRegister,
            kernelResultRegister,
            copyTemporaryRegister,
            "warp",
            "warp_kernel_return");
        ptx.AppendLine("warp_kernel_return:");
        AppendReduction(
            ptx,
            operation,
            accumulatorRegister,
            kernelResultRegister);
        ptx.AppendLine("    add.u32 %r3, %r3, 1;");
        ptx.AppendLine("    bra warp_reduce_loop;");
        ptx.AppendLine();
        ptx.AppendLine("warp_reduce_store:");
        ptx.Append("    st.global.u32 [%rd1], %r")
            .Append(Invariant(accumulatorRegister))
            .AppendLine(";");
        ptx.AppendLine("warp_reduce_done:");
        ptx.AppendLine("    ret;");
        ptx.AppendLine("}");

        return new WarpBackendArtifact(
            Backend,
            WarpArtifactFormat.NVPTX,
            WarpDeviceAbi.IntegerReductionEntryPoint,
            Encoding.UTF8.GetBytes(ptx.ToString()));
    }

    private static void AppendReduction(
        StringBuilder ptx,
        WarpReductionOperation operation,
        int accumulatorRegister,
        int valueRegister)
    {
        switch (operation)
        {
            case WarpReductionOperation.WrappingSum:
                AppendBinary(
                    ptx,
                    "add.u32",
                    accumulatorRegister,
                    accumulatorRegister,
                    valueRegister);
                break;

            case WarpReductionOperation.Minimum:
                AppendSelection(
                    ptx,
                    "setp.lt.u32",
                    accumulatorRegister,
                    valueRegister);
                break;

            case WarpReductionOperation.Maximum:
                AppendSelection(
                    ptx,
                    "setp.gt.u32",
                    accumulatorRegister,
                    valueRegister);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static void AppendSelection(
        StringBuilder ptx,
        string comparison,
        int accumulatorRegister,
        int valueRegister)
    {
        ptx.Append("    ")
            .Append(comparison)
            .Append(" %p1, %r")
            .Append(Invariant(valueRegister))
            .Append(", %r")
            .Append(Invariant(accumulatorRegister))
            .AppendLine(";");
        ptx.Append("    selp.u32 %r")
            .Append(Invariant(accumulatorRegister))
            .Append(", %r")
            .Append(Invariant(valueRegister))
            .Append(", %r")
            .Append(Invariant(accumulatorRegister))
            .AppendLine(", %p1;");
    }

    private static void AppendFunctions(
        StringBuilder ptx,
        WarpControlFlowKernel kernel)
    {
        foreach (WarpControlFlowFunction function in kernel.Functions)
        {
            AppendFunctionSignature(ptx, function);
            ptx.AppendLine(";");
        }

        if (kernel.Functions.Count != 0)
        {
            ptx.AppendLine();
        }

        foreach (WarpControlFlowFunction function in kernel.Functions)
        {
            int shiftRegister = FirstValueRegister + function.ValueCount;
            int resultRegister = shiftRegister + 1;
            int copyTemporaryRegister = resultRegister + 1;
            int registerCount = copyTemporaryRegister + GetMaximumParameterCount(function.Blocks);
            string prefix = $"warp_function_{Invariant(function.Id)}";

            AppendFunctionSignature(ptx, function);
            ptx.AppendLine();
            ptx.AppendLine("{");
            ptx.AppendLine("    .reg .pred %p<2>;");
            ptx.Append("    .reg .b32 %r<")
                .Append(Invariant(registerCount))
                .AppendLine(">;");
            AppendCallParameterStorage(ptx, function.Instructions);
            ptx.Append("    bra ")
                .Append(prefix)
                .AppendLine("_block_0;");
            ptx.AppendLine();
            AppendControlFlow(
                ptx,
                function.Blocks,
                firstInputBaseRegister: 0,
                addressRegister: 0,
                shiftRegister,
                resultRegister,
                copyTemporaryRegister,
                prefix,
                $"{prefix}_return");
            ptx.Append(prefix).AppendLine("_return:");
            ptx.Append("    st.param.b32 [")
                .Append(prefix)
                .Append("_result], %r")
                .Append(Invariant(resultRegister))
                .AppendLine(";");
            ptx.AppendLine("    ret;");
            ptx.AppendLine("}");
            ptx.AppendLine();
        }
    }

    private static void AppendFunctionSignature(
        StringBuilder ptx,
        WarpControlFlowFunction function)
    {
        string prefix = $"warp_function_{Invariant(function.Id)}";
        ptx.Append(".func (.param .b32 ")
            .Append(prefix)
            .Append("_result) ")
            .Append(prefix)
            .AppendLine("(");
        for (int argument = 0; argument < function.ParameterCount; argument++)
        {
            ptx.Append("    .param .b32 warp_arg_")
                .Append(Invariant(argument))
                .AppendLine(argument + 1 == function.ParameterCount ? string.Empty : ",");
        }

        ptx.Append(')');
    }

    private static void AppendCallParameterStorage(
        StringBuilder ptx,
        IReadOnlyList<WarpIrInstruction> instructions)
    {
        int maximumArguments = instructions
            .Where(instruction => instruction.OpCode == WarpIrOpCode.Call)
            .Select(instruction => instruction.Arguments.Count)
            .DefaultIfEmpty(0)
            .Max();
        if (maximumArguments == 0 &&
            instructions.All(instruction => instruction.OpCode != WarpIrOpCode.Call))
        {
            return;
        }

        ptx.AppendLine("    .param .b32 warp_call_result;");
        for (int argument = 0; argument < maximumArguments; argument++)
        {
            ptx.Append("    .param .b32 warp_call_arg_")
                .Append(Invariant(argument))
                .AppendLine(";");
        }
    }

    private static void AppendParameters(StringBuilder ptx, WarpControlFlowKernel kernel)
    {
        var parameters = new List<string>(
            kernel.InputBufferCount + kernel.ScalarArgumentCount + 2);

        for (int inputIndex = 0; inputIndex < kernel.InputBufferCount; inputIndex++)
        {
            parameters.Add($".param .u64 warp_input_{Invariant(inputIndex)}");
        }

        parameters.Add(".param .u64 warp_output");
        parameters.Add(".param .u32 warp_count");

        for (int scalarIndex = 0; scalarIndex < kernel.ScalarArgumentCount; scalarIndex++)
        {
            parameters.Add($".param .u32 warp_scalar_{Invariant(scalarIndex)}");
        }

        for (int index = 0; index < parameters.Count; index++)
        {
            ptx.Append("    ")
                .Append(parameters[index])
                .AppendLine(index + 1 == parameters.Count ? string.Empty : ",");
        }
    }

    private static void AppendControlFlow(
        StringBuilder ptx,
        IReadOnlyList<WarpBasicBlock> blocks,
        int firstInputBaseRegister,
        int addressRegister,
        int shiftRegister,
        int kernelResultRegister,
        int copyTemporaryRegister,
        string labelPrefix,
        string returnLabel)
    {
        foreach (WarpBasicBlock block in blocks)
        {
            ptx.Append(labelPrefix)
                .Append("_block_")
                .Append(Invariant(block.Id))
                .AppendLine(":");
            foreach (WarpIrInstruction instruction in block.Instructions)
            {
                AppendInstruction(
                    ptx,
                    instruction,
                    firstInputBaseRegister,
                    addressRegister,
                    shiftRegister);
            }

            switch (block.Terminator)
            {
                case WarpBranchTerminator branch:
                    AppendEdgeCopies(ptx, blocks, branch.Target, copyTemporaryRegister);
                    ptx.Append("    bra ")
                        .Append(labelPrefix)
                        .Append("_block_")
                        .Append(Invariant(branch.Target.Block))
                        .AppendLine(";");
                    break;

                case WarpConditionalBranchTerminator conditional:
                    ptx.Append("    setp.ne.u32 %p1, %r")
                        .Append(Invariant(ValueRegister(conditional.Condition)))
                        .AppendLine(", 0;");
                    ptx.Append("    @!%p1 bra ")
                        .Append(labelPrefix)
                        .Append("_edge_zero_")
                        .Append(Invariant(block.Id))
                        .AppendLine(";");
                    AppendEdgeCopies(
                        ptx,
                        blocks,
                        conditional.WhenNonZero,
                        copyTemporaryRegister);
                    ptx.Append("    bra ")
                        .Append(labelPrefix)
                        .Append("_block_")
                        .Append(Invariant(conditional.WhenNonZero.Block))
                        .AppendLine(";");
                    ptx.Append(labelPrefix)
                        .Append("_edge_zero_")
                        .Append(Invariant(block.Id))
                        .AppendLine(":");
                    AppendEdgeCopies(
                        ptx,
                        blocks,
                        conditional.WhenZero,
                        copyTemporaryRegister);
                    ptx.Append("    bra ")
                        .Append(labelPrefix)
                        .Append("_block_")
                        .Append(Invariant(conditional.WhenZero.Block))
                        .AppendLine(";");
                    break;

                case WarpReturnTerminator @return:
                    ptx.Append("    mov.u32 %r")
                        .Append(Invariant(kernelResultRegister))
                        .Append(", %r")
                        .Append(Invariant(ValueRegister(@return.Value)))
                        .AppendLine(";");
                    ptx.Append("    bra ").Append(returnLabel).AppendLine(";");
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(block));
            }

            ptx.AppendLine();
        }
    }

    private static void AppendEdgeCopies(
        StringBuilder ptx,
        IReadOnlyList<WarpBasicBlock> blocks,
        WarpBranchTarget target,
        int copyTemporaryRegister)
    {
        WarpBasicBlock destination = blocks[target.Block];
        for (int index = 0; index < target.Arguments.Count; index++)
        {
            ptx.Append("    mov.u32 %r")
                .Append(Invariant(copyTemporaryRegister + index))
                .Append(", %r")
                .Append(Invariant(ValueRegister(target.Arguments[index])))
                .AppendLine(";");
        }

        for (int index = 0; index < target.Arguments.Count; index++)
        {
            ptx.Append("    mov.u32 %r")
                .Append(Invariant(ValueRegister(destination.Parameters[index].Value)))
                .Append(", %r")
                .Append(Invariant(copyTemporaryRegister + index))
                .AppendLine(";");
        }
    }

    private static int GetMaximumParameterCount(WarpControlFlowKernel kernel) =>
        GetMaximumParameterCount(kernel.Blocks);

    private static int GetMaximumParameterCount(IReadOnlyList<WarpBasicBlock> blocks) =>
        blocks.Max(block => block.Parameters.Count);

    private static void AppendInstruction(
        StringBuilder ptx,
        WarpIrInstruction instruction,
        int firstInputBaseRegister,
        int addressRegister,
        int shiftRegister)
    {
        int result = ValueRegister(instruction.Result);
        int left = ValueRegister(instruction.Left);
        int right = ValueRegister(instruction.Right);
        int third = ValueRegister(instruction.Third);

        switch (instruction.OpCode)
        {
            case WarpIrOpCode.LoadInput:
                ptx.Append("    add.u64 %rd")
                    .Append(Invariant(addressRegister))
                    .Append(", %rd")
                    .Append(Invariant(firstInputBaseRegister + checked((int)instruction.Immediate)))
                    .AppendLine(", %rd0;");
                ptx.Append("    ld.global.u32 %r")
                    .Append(Invariant(result))
                    .Append(", [%rd")
                    .Append(Invariant(addressRegister))
                    .AppendLine("];");
                break;

            case WarpIrOpCode.LoadScalar:
                ptx.Append("    ld.param.u32 %r")
                    .Append(Invariant(result))
                    .Append(", [warp_scalar_")
                    .Append(Invariant(instruction.Immediate))
                    .AppendLine("];");
                break;

            case WarpIrOpCode.LoadArgument:
                ptx.Append("    ld.param.u32 %r")
                    .Append(Invariant(result))
                    .Append(", [warp_arg_")
                    .Append(Invariant(instruction.Immediate))
                    .AppendLine("];");
                break;

            case WarpIrOpCode.Constant:
                ptx.Append("    mov.u32 %r")
                    .Append(Invariant(result))
                    .Append(", 0x")
                    .Append(instruction.Immediate.ToString("X8", CultureInfo.InvariantCulture))
                    .AppendLine(";");
                break;

            case WarpIrOpCode.BitwiseNot:
                AppendUnary(ptx, "not.b32", result, left);
                break;

            case WarpIrOpCode.Add:
                AppendBinary(ptx, "add.u32", result, left, right);
                break;

            case WarpIrOpCode.Subtract:
                AppendBinary(ptx, "sub.u32", result, left, right);
                break;

            case WarpIrOpCode.Multiply:
                AppendBinary(ptx, "mul.lo.u32", result, left, right);
                break;

            case WarpIrOpCode.BitwiseAnd:
                AppendBinary(ptx, "and.b32", result, left, right);
                break;

            case WarpIrOpCode.BitwiseOr:
                AppendBinary(ptx, "or.b32", result, left, right);
                break;

            case WarpIrOpCode.ExclusiveOr:
                AppendBinary(ptx, "xor.b32", result, left, right);
                break;

            case WarpIrOpCode.ShiftLeft:
                AppendShift(ptx, "shl.b32", result, left, right, shiftRegister);
                break;

            case WarpIrOpCode.ShiftRightLogical:
                AppendShift(ptx, "shr.u32", result, left, right, shiftRegister);
                break;

            case WarpIrOpCode.Equal:
                AppendComparison(ptx, "setp.eq.u32", result, left, right);
                break;

            case WarpIrOpCode.NotEqual:
                AppendComparison(ptx, "setp.ne.u32", result, left, right);
                break;

            case WarpIrOpCode.LessThanUnsigned:
                AppendComparison(ptx, "setp.lt.u32", result, left, right);
                break;

            case WarpIrOpCode.LessThanOrEqualUnsigned:
                AppendComparison(ptx, "setp.le.u32", result, left, right);
                break;

            case WarpIrOpCode.GreaterThanUnsigned:
                AppendComparison(ptx, "setp.gt.u32", result, left, right);
                break;

            case WarpIrOpCode.GreaterThanOrEqualUnsigned:
                AppendComparison(ptx, "setp.ge.u32", result, left, right);
                break;

            case WarpIrOpCode.Select:
                AppendConditionalSelection(ptx, result, left, right, third);
                break;

            case WarpIrOpCode.Call:
                for (int argument = 0; argument < instruction.Arguments.Count; argument++)
                {
                    ptx.Append("    st.param.b32 [warp_call_arg_")
                        .Append(Invariant(argument))
                        .Append("], %r")
                        .Append(Invariant(ValueRegister(instruction.Arguments[argument])))
                        .AppendLine(";");
                }

                ptx.Append("    call (warp_call_result), warp_function_")
                    .Append(Invariant(instruction.Callee))
                    .Append(", (");
                for (int argument = 0; argument < instruction.Arguments.Count; argument++)
                {
                    if (argument != 0)
                    {
                        ptx.Append(", ");
                    }

                    ptx.Append("warp_call_arg_").Append(Invariant(argument));
                }

                ptx.AppendLine(");");
                ptx.Append("    ld.param.u32 %r")
                    .Append(Invariant(result))
                    .AppendLine(", [warp_call_result];");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(instruction));
        }
    }

    private static void AppendUnary(StringBuilder ptx, string opcode, int result, int operand)
    {
        ptx.Append("    ")
            .Append(opcode)
            .Append(" %r")
            .Append(Invariant(result))
            .Append(", %r")
            .Append(Invariant(operand))
            .AppendLine(";");
    }

    private static void AppendBinary(
        StringBuilder ptx,
        string opcode,
        int result,
        int left,
        int right)
    {
        ptx.Append("    ")
            .Append(opcode)
            .Append(" %r")
            .Append(Invariant(result))
            .Append(", %r")
            .Append(Invariant(left))
            .Append(", %r")
            .Append(Invariant(right))
            .AppendLine(";");
    }

    private static void AppendShift(
        StringBuilder ptx,
        string opcode,
        int result,
        int left,
        int right,
        int shiftRegister)
    {
        ptx.Append("    and.b32 %r")
            .Append(Invariant(shiftRegister))
            .Append(", %r")
            .Append(Invariant(right))
            .AppendLine(", 31;");
        AppendBinary(ptx, opcode, result, left, shiftRegister);
    }

    private static void AppendComparison(
        StringBuilder ptx,
        string opcode,
        int result,
        int left,
        int right)
    {
        ptx.Append("    ")
            .Append(opcode)
            .Append(" %p1, %r")
            .Append(Invariant(left))
            .Append(", %r")
            .Append(Invariant(right))
            .AppendLine(";");
        ptx.Append("    selp.u32 %r")
            .Append(Invariant(result))
            .AppendLine(", 1, 0, %p1;");
    }

    private static void AppendConditionalSelection(
        StringBuilder ptx,
        int result,
        int condition,
        int whenNonZero,
        int whenZero)
    {
        ptx.Append("    setp.ne.u32 %p1, %r")
            .Append(Invariant(condition))
            .AppendLine(", 0;");
        ptx.Append("    selp.u32 %r")
            .Append(Invariant(result))
            .Append(", %r")
            .Append(Invariant(whenNonZero))
            .Append(", %r")
            .Append(Invariant(whenZero))
            .AppendLine(", %p1;");
    }

    private static int ValueRegister(int result) => FirstValueRegister + result;

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(uint value) => value.ToString(CultureInfo.InvariantCulture);
}
