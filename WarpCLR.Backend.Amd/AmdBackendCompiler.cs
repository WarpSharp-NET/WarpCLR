using System.Globalization;
using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Backend.Amd;

public sealed class AmdBackendCompiler : IWarpBackendCompiler
{
    public WarpBackendKind Backend => WarpBackendKind.Amd;

    public WarpBackendArtifact Compile(WarpLinearKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var llvm = new StringBuilder();
        llvm.Append("; ").AppendLine(WarpDeviceAbi.DevelopmentConformanceMarker);
        llvm.AppendLine("target triple = \"amdgcn-amd-amdhsa\"");
        llvm.AppendLine();
        llvm.AppendLine("declare i32 @llvm.amdgcn.workitem.id.x()");
        llvm.AppendLine("declare i32 @llvm.amdgcn.workgroup.id.x()");
        llvm.AppendLine();
        llvm.Append("define amdgpu_kernel void @")
            .Append(WarpDeviceAbi.IntegerMapEntryPoint)
            .AppendLine("(");
        AppendParameters(llvm, kernel);
        llvm.AppendLine(") #0 {");
        llvm.AppendLine("entry:");
        llvm.AppendLine("  %warp_local_id = call i32 @llvm.amdgcn.workitem.id.x()");
        llvm.AppendLine("  %warp_group_id = call i32 @llvm.amdgcn.workgroup.id.x()");
        llvm.Append("  %warp_group_base = mul i32 %warp_group_id, ")
            .Append(Invariant(WarpDeviceAbi.IntegerMapWorkgroupSize))
            .AppendLine();
        llvm.AppendLine("  %warp_index = add i32 %warp_group_base, %warp_local_id");
        llvm.AppendLine("  %warp_in_range = icmp ult i32 %warp_index, %warp_count");
        llvm.AppendLine("  br i1 %warp_in_range, label %body, label %done");
        llvm.AppendLine();
        llvm.AppendLine("body:");

        foreach (WarpIrInstruction instruction in kernel.Instructions)
        {
            AppendInstruction(llvm, instruction);
        }

        llvm.Append("  %warp_output_ptr = getelementptr i32, ptr addrspace(1) %warp_output, i32 %warp_index")
            .AppendLine();
        llvm.Append("  store i32 %warp_v")
            .Append(Invariant(kernel.Result))
            .AppendLine(", ptr addrspace(1) %warp_output_ptr, align 4");
        llvm.AppendLine("  br label %done");
        llvm.AppendLine();
        llvm.AppendLine("done:");
        llvm.AppendLine("  ret void");
        llvm.AppendLine("}");
        llvm.AppendLine();
        llvm.Append("attributes #0 = { nounwind \"amdgpu-flat-work-group-size\"=\"")
            .Append(Invariant(WarpDeviceAbi.IntegerMapWorkgroupSize))
            .Append(',')
            .Append(Invariant(WarpDeviceAbi.IntegerMapWorkgroupSize))
            .AppendLine("\" }");

        return new WarpBackendArtifact(
            Backend,
            WarpArtifactFormat.AmdLlvmIr,
            WarpDeviceAbi.IntegerMapEntryPoint,
            Encoding.UTF8.GetBytes(llvm.ToString()));
    }

    private static void AppendParameters(StringBuilder llvm, WarpLinearKernel kernel)
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

    private static void AppendInstruction(StringBuilder llvm, WarpIrInstruction instruction)
    {
        string result = Value(instruction.Result);
        string left = Value(instruction.Left);
        string right = Value(instruction.Right);

        switch (instruction.OpCode)
        {
            case WarpIrOpCode.LoadInput:
                llvm.Append("  %warp_input_ptr_")
                    .Append(Invariant(instruction.Result))
                    .Append(" = getelementptr i32, ptr addrspace(1) %warp_input_")
                    .Append(Invariant(instruction.Immediate))
                    .AppendLine(", i32 %warp_index");
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

            default:
                throw new ArgumentOutOfRangeException(nameof(instruction));
        }
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

    private static string Value(int result) => $"%warp_v{Invariant(result)}";

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(uint value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Signed(uint value) =>
        unchecked((int)value).ToString(CultureInfo.InvariantCulture);
}
