using WarpCLR.IR;

namespace WarpCLR.Tests;

internal static class BackendArtifactAssertions
{
    public static void IsValid(
        WarpBackendArtifact artifact,
        WarpBackendKind backend,
        WarpControlFlowKernel kernel)
    {
        Assert.AreEqual(backend, artifact.Backend);
        Assert.AreEqual(WarpArtifactFormatCatalog.ForBackend(backend), artifact.Format);
        Assert.AreEqual(WarpDeviceAbi.GetEntryPoint(kernel), artifact.EntryPoint);
        Assert.AreEqual(
            WarpConformanceStatus.DevelopmentNonconforming,
            artifact.ConformanceStatus);
        Assert.IsFalse(artifact.Content.IsEmpty);

        string text = artifact.GetText();
        StringAssert.Contains(text, WarpDeviceAbi.DevelopmentConformanceMarker);
        switch (backend)
        {
            case WarpBackendKind.CoreCLR:
                StringAssert.Contains(text, "warp.coreclr.cfg/0.3");
                StringAssert.Contains(text, $"entry={WarpDeviceAbi.GetEntryPoint(kernel)}");
                break;

            case WarpBackendKind.NVPTX:
                StringAssert.Contains(text, ".target sm_50");
                StringAssert.Contains(text, $".visible .entry {WarpDeviceAbi.GetEntryPoint(kernel)}");
                break;

            case WarpBackendKind.AMDGPU:
                StringAssert.Contains(text, "target triple = \"amdgcn-amd-amdhsa\"");
                StringAssert.Contains(text, $"define amdgpu_kernel void @{WarpDeviceAbi.GetEntryPoint(kernel)}");
                Assert.IsFalse(text.Contains(" nsw ", StringComparison.Ordinal));
                Assert.IsFalse(text.Contains(" nuw ", StringComparison.Ordinal));
                break;

            case WarpBackendKind.SPIRV:
                StringAssert.Contains(text, "target triple = \"spirv64-unknown-unknown\"");
                StringAssert.Contains(text, $"define spir_kernel void @{WarpDeviceAbi.GetEntryPoint(kernel)}");
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

        foreach (WarpBasicBlock block in kernel.Blocks)
        {
            string marker = backend switch
            {
                WarpBackendKind.CoreCLR => $"block={block.Id}",
                WarpBackendKind.NVPTX => $"warp_block_{block.Id}:",
                WarpBackendKind.AMDGPU or WarpBackendKind.SPIRV => $"warp_block_{block.Id}:",
                _ => throw new ArgumentOutOfRangeException(nameof(backend)),
            };
            StringAssert.Contains(text, marker);

            foreach (WarpBlockParameter parameter in block.Parameters)
            {
                string parameterMarker = backend switch
                {
                    WarpBackendKind.CoreCLR =>
                        $"parameter={parameter.Value},{parameter.Type}",
                    WarpBackendKind.NVPTX =>
                        $"mov.u32 %r{parameter.Value + 5}, %r",
                    WarpBackendKind.AMDGPU or WarpBackendKind.SPIRV =>
                        $"%warp_v{parameter.Value} = phi i32",
                    _ => throw new ArgumentOutOfRangeException(nameof(backend)),
                };
                StringAssert.Contains(text, parameterMarker);
            }
        }

        if (kernel.Reduction.HasValue)
        {
            IsValidReduction(text, backend, kernel.Reduction.Value);
        }
    }

    private static void IsValidReduction(
        string text,
        WarpBackendKind backend,
        WarpReductionOperation operation)
    {
        string operationMarker = (backend, operation) switch
        {
            (WarpBackendKind.CoreCLR, WarpReductionOperation.WrappingSum) =>
                "operation=reduce-wrapping-sum",
            (WarpBackendKind.CoreCLR, WarpReductionOperation.Minimum) =>
                "operation=reduce-minimum",
            (WarpBackendKind.CoreCLR, WarpReductionOperation.Maximum) =>
                "operation=reduce-maximum",
            (WarpBackendKind.NVPTX, WarpReductionOperation.WrappingSum) => "warp_reduce_loop:",
            (WarpBackendKind.NVPTX, WarpReductionOperation.Minimum) => "setp.lt.u32 %p1",
            (WarpBackendKind.NVPTX, WarpReductionOperation.Maximum) => "setp.gt.u32 %p1",
            (WarpBackendKind.AMDGPU or WarpBackendKind.SPIRV, WarpReductionOperation.WrappingSum) =>
                "%warp_next_accumulator = add i32",
            (WarpBackendKind.AMDGPU or WarpBackendKind.SPIRV, WarpReductionOperation.Minimum) =>
                "%warp_reduce_compare = icmp ult i32",
            (WarpBackendKind.AMDGPU or WarpBackendKind.SPIRV, WarpReductionOperation.Maximum) =>
                "%warp_reduce_compare = icmp ugt i32",
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        StringAssert.Contains(text, operationMarker);
        Assert.IsFalse(text.Contains("atomic", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("subgroup", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetInstructionMarker(
        WarpBackendKind backend,
        WarpIrInstruction instruction) => backend switch
        {
            WarpBackendKind.CoreCLR =>
                $"instruction={instruction.Result},{instruction.ResultType},{instruction.OpCode},",
            WarpBackendKind.NVPTX => GetNVPTXMarker(instruction),
            WarpBackendKind.AMDGPU or WarpBackendKind.SPIRV => GetLlvmMarker(instruction),
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "The backend is not registered."),
        };

    private static string GetNVPTXMarker(WarpIrInstruction instruction)
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
            WarpIrOpCode.Equal or
            WarpIrOpCode.NotEqual or
            WarpIrOpCode.LessThanUnsigned or
            WarpIrOpCode.LessThanOrEqualUnsigned or
            WarpIrOpCode.GreaterThanUnsigned or
            WarpIrOpCode.GreaterThanOrEqualUnsigned or
            WarpIrOpCode.Select => "selp.u32",
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };

        return $"{opcode} %r{resultRegister},";
    }

    private static string GetLlvmMarker(WarpIrInstruction instruction)
    {
        if (instruction.OpCode is
            WarpIrOpCode.Equal or
            WarpIrOpCode.NotEqual or
            WarpIrOpCode.LessThanUnsigned or
            WarpIrOpCode.LessThanOrEqualUnsigned or
            WarpIrOpCode.GreaterThanUnsigned or
            WarpIrOpCode.GreaterThanOrEqualUnsigned)
        {
            return $"%warp_v{instruction.Result} = zext i1";
        }

        if (instruction.OpCode == WarpIrOpCode.Select)
        {
            return $"%warp_v{instruction.Result} = select i1";
        }

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
