using WarpCLR.IR;

namespace WarpCLR.Tests;

internal static class BackendArtifactAssertions
{
    public static void IsValid(
        WarpBackendArtifact artifact,
        WarpBackendKind backend,
        WarpLinearKernel kernel)
    {
        Assert.AreEqual(backend, artifact.Backend);
        Assert.AreEqual(WarpArtifactFormatCatalog.ForBackend(backend), artifact.Format);
        Assert.AreEqual(WarpDeviceAbi.IntegerMapEntryPoint, artifact.EntryPoint);
        Assert.AreEqual(
            WarpConformanceStatus.DevelopmentNonconforming,
            artifact.ConformanceStatus);
        Assert.IsFalse(artifact.Content.IsEmpty);

        string text = artifact.GetText();
        StringAssert.Contains(text, WarpDeviceAbi.DevelopmentConformanceMarker);
        switch (backend)
        {
            case WarpBackendKind.CpuReference:
                StringAssert.Contains(text, "warp.cpu.linear/0.1");
                StringAssert.Contains(text, $"entry={WarpDeviceAbi.IntegerMapEntryPoint}");
                break;

            case WarpBackendKind.Nvidia:
                StringAssert.Contains(text, ".target sm_50");
                StringAssert.Contains(text, $".visible .entry {WarpDeviceAbi.IntegerMapEntryPoint}");
                break;

            case WarpBackendKind.Amd:
                StringAssert.Contains(text, "target triple = \"amdgcn-amd-amdhsa\"");
                StringAssert.Contains(text, $"define amdgpu_kernel void @{WarpDeviceAbi.IntegerMapEntryPoint}");
                Assert.IsFalse(text.Contains(" nsw ", StringComparison.Ordinal));
                Assert.IsFalse(text.Contains(" nuw ", StringComparison.Ordinal));
                break;

            case WarpBackendKind.Intel:
                StringAssert.Contains(text, "target triple = \"spirv64-unknown-unknown\"");
                StringAssert.Contains(text, $"define spir_kernel void @{WarpDeviceAbi.IntegerMapEntryPoint}");
                Assert.IsFalse(text.Contains("SPV_INTEL_", StringComparison.Ordinal));
                Assert.IsFalse(text.Contains(" nsw ", StringComparison.Ordinal));
                Assert.IsFalse(text.Contains(" nuw ", StringComparison.Ordinal));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(backend), backend, "The backend is not registered.");
        }

        foreach (WarpIrInstruction instruction in kernel.Instructions)
        {
            StringAssert.Contains(text, GetInstructionMarker(backend, instruction));
        }
    }

    private static string GetInstructionMarker(
        WarpBackendKind backend,
        WarpIrInstruction instruction) => backend switch
        {
            WarpBackendKind.CpuReference => $"{instruction.Result}={instruction.OpCode},",
            WarpBackendKind.Nvidia => GetNvidiaMarker(instruction),
            WarpBackendKind.Amd or WarpBackendKind.Intel => GetLlvmMarker(instruction),
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "The backend is not registered."),
        };

    private static string GetNvidiaMarker(WarpIrInstruction instruction)
    {
        int resultRegister = instruction.Result + 5;
        string opcode = instruction.OpCode switch
        {
            WarpIrOpCode.LoadInput => "ld.global.u32",
            WarpIrOpCode.LoadScalar => "ld.param.u32",
            WarpIrOpCode.Constant => "mov.u32",
            WarpIrOpCode.BitwiseNot => "not.b32",
            WarpIrOpCode.Add => "add.u32",
            WarpIrOpCode.Subtract => "sub.u32",
            WarpIrOpCode.Multiply => "mul.lo.u32",
            WarpIrOpCode.BitwiseAnd => "and.b32",
            WarpIrOpCode.BitwiseOr => "or.b32",
            WarpIrOpCode.ExclusiveOr => "xor.b32",
            WarpIrOpCode.ShiftLeft => "shl.b32",
            WarpIrOpCode.ShiftRightLogical => "shr.u32",
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };

        return $"{opcode} %r{resultRegister},";
    }

    private static string GetLlvmMarker(WarpIrInstruction instruction)
    {
        string opcode = instruction.OpCode switch
        {
            WarpIrOpCode.LoadInput => "load",
            WarpIrOpCode.LoadScalar => "add",
            WarpIrOpCode.Constant => "add",
            WarpIrOpCode.BitwiseNot => "xor",
            WarpIrOpCode.Add => "add",
            WarpIrOpCode.Subtract => "sub",
            WarpIrOpCode.Multiply => "mul",
            WarpIrOpCode.BitwiseAnd => "and",
            WarpIrOpCode.BitwiseOr => "or",
            WarpIrOpCode.ExclusiveOr => "xor",
            WarpIrOpCode.ShiftLeft => "shl",
            WarpIrOpCode.ShiftRightLogical => "lshr",
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };

        return $"%warp_v{instruction.Result} = {opcode} i32";
    }
}
