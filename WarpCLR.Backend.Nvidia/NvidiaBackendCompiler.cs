using System.Globalization;
using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Backend.Nvidia;

public sealed class NvidiaBackendCompiler : IWarpBackendCompiler
{
    private const int FirstValueRegister = 5;

    public WarpBackendKind Backend => WarpBackendKind.Nvidia;

    public WarpBackendArtifact Compile(WarpLinearKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        if (kernel.Reduction.HasValue)
        {
            return CompileReduction(kernel);
        }

        int shiftRegister = FirstValueRegister + kernel.ValueCount;
        int registerCount = shiftRegister + 1;
        int firstInputBaseRegister = 2;
        int addressRegister = firstInputBaseRegister + kernel.InputBufferCount;
        int addressRegisterCount = addressRegister + 1;

        var ptx = new StringBuilder();
        ptx.AppendLine(".version 6.0");
        ptx.AppendLine(".target sm_50");
        ptx.AppendLine(".address_size 64");
        ptx.Append("// ").AppendLine(WarpDeviceAbi.DevelopmentConformanceMarker);
        ptx.AppendLine();
        ptx.Append(".visible .entry ")
            .Append(WarpDeviceAbi.IntegerMapEntryPoint)
            .AppendLine("(");
        AppendParameters(ptx, kernel);
        ptx.AppendLine(")");
        ptx.AppendLine("{");
        ptx.AppendLine("    .reg .pred %p<1>;");
        ptx.Append("    .reg .b32 %r<")
            .Append(Invariant(registerCount))
            .AppendLine(">;");
        ptx.Append("    .reg .b64 %rd<")
            .Append(Invariant(addressRegisterCount))
            .AppendLine(">;");
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
        foreach (WarpIrInstruction instruction in kernel.Instructions)
        {
            AppendInstruction(
                ptx,
                instruction,
                firstInputBaseRegister,
                addressRegister,
                shiftRegister);
        }

        ptx.AppendLine();
        ptx.Append("    add.u64 %rd")
            .Append(Invariant(addressRegister))
            .AppendLine(", %rd1, %rd0;");
        ptx.Append("    st.global.u32 [%rd")
            .Append(Invariant(addressRegister))
            .Append("], %r")
            .Append(Invariant(ValueRegister(kernel.Result)))
            .AppendLine(";");
        ptx.AppendLine("warp_done:");
        ptx.AppendLine("    ret;");
        ptx.AppendLine("}");

        return new WarpBackendArtifact(
            Backend,
            WarpArtifactFormat.NvidiaPtx,
            WarpDeviceAbi.IntegerMapEntryPoint,
            Encoding.UTF8.GetBytes(ptx.ToString()));
    }

    private WarpBackendArtifact CompileReduction(WarpLinearKernel kernel)
    {
        WarpReductionOperation operation = kernel.Reduction
            ?? throw new ArgumentException("A reduction operation is required.", nameof(kernel));
        int shiftRegister = FirstValueRegister + kernel.ValueCount;
        int accumulatorRegister = shiftRegister + 1;
        int registerCount = accumulatorRegister + 1;
        int firstInputBaseRegister = 2;
        int addressRegister = firstInputBaseRegister + kernel.InputBufferCount;
        int addressRegisterCount = addressRegister + 1;

        var ptx = new StringBuilder();
        ptx.AppendLine(".version 6.0");
        ptx.AppendLine(".target sm_50");
        ptx.AppendLine(".address_size 64");
        ptx.Append("// ").AppendLine(WarpDeviceAbi.DevelopmentConformanceMarker);
        ptx.AppendLine();
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

        foreach (WarpIrInstruction instruction in kernel.Instructions)
        {
            AppendInstruction(
                ptx,
                instruction,
                firstInputBaseRegister,
                addressRegister,
                shiftRegister);
        }

        AppendReduction(
            ptx,
            operation,
            accumulatorRegister,
            ValueRegister(kernel.Result));
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
            WarpArtifactFormat.NvidiaPtx,
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

    private static void AppendParameters(StringBuilder ptx, WarpLinearKernel kernel)
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

    private static int ValueRegister(int result) => FirstValueRegister + result;

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(uint value) => value.ToString(CultureInfo.InvariantCulture);
}
