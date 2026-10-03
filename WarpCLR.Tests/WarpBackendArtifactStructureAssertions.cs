using WarpCLR.IR;

namespace WarpCLR.Tests;

internal static class WarpBackendArtifactStructureAssertions
{
    public static void IsValidHeader(string text, WarpBackendKind backend, WarpControlFlowKernel kernel)
    {
        switch (backend)
        {
            case WarpBackendKind.CoreCLR:
                StringAssert.Contains(text, "warp.coreclr.cfg/0.4", StringComparison.Ordinal);
                StringAssert.Contains(text, $"entry={WarpDeviceAbi.GetEntryPoint(kernel)}", StringComparison.Ordinal);
                break;

            case WarpBackendKind.NVPTX:
                StringAssert.Contains(text, ".target sm_50", StringComparison.Ordinal);
                StringAssert.Contains(text, $".visible .entry {WarpDeviceAbi.GetEntryPoint(kernel)}", StringComparison.Ordinal);
                break;

            case WarpBackendKind.AMDGPU:
                StringAssert.Contains(text, "target triple = \"amdgcn-amd-amdhsa\"", StringComparison.Ordinal);
                StringAssert.Contains(text, $"define amdgpu_kernel void @{WarpDeviceAbi.GetEntryPoint(kernel)}", StringComparison.Ordinal);
                Assert.IsFalse(text.Contains(" nsw ", StringComparison.Ordinal));
                Assert.IsFalse(text.Contains(" nuw ", StringComparison.Ordinal));
                break;

            case WarpBackendKind.SPIRV:
                StringAssert.Contains(text, "target triple = \"spirv64-unknown-unknown\"", StringComparison.Ordinal);
                StringAssert.Contains(text, $"define spir_kernel void @{WarpDeviceAbi.GetEntryPoint(kernel)}", StringComparison.Ordinal);
                Assert.IsFalse(text.Contains("SPV_INTEL_", StringComparison.Ordinal));
                Assert.IsFalse(text.Contains(" nsw ", StringComparison.Ordinal));
                Assert.IsFalse(text.Contains(" nuw ", StringComparison.Ordinal));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(backend), backend, "The backend is not registered.");
        }
    }

    public static void IsValidBody(string text, WarpBackendKind backend, WarpControlFlowKernel kernel)
    {
        foreach (WarpIrInstruction instruction in kernel.Instructions)
        {
            StringAssert.Contains(text, BackendArtifactAssertions.GetInstructionMarker(backend, instruction), StringComparison.Ordinal);
        }

        foreach (WarpControlFlowFunction function in kernel.Functions)
        {
            string functionMarker = backend switch
            {
                WarpBackendKind.CoreCLR => $"function={function.Id},{function.ParameterCount},",
                WarpBackendKind.NVPTX => $".func (.param .b32 warp_function_{function.Id}_result) warp_function_{function.Id}",
                WarpBackendKind.AMDGPU => $"define internal i32 @warp_function_{function.Id}",
                WarpBackendKind.SPIRV => $"define internal spir_func i32 @warp_function_{function.Id}",
                _ => throw new ArgumentOutOfRangeException(nameof(backend)),
            };
            StringAssert.Contains(text, functionMarker, StringComparison.Ordinal);

            foreach (WarpIrInstruction instruction in function.Instructions)
            {
                StringAssert.Contains(text, BackendArtifactAssertions.GetInstructionMarker(backend, instruction), StringComparison.Ordinal);
            }
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
            StringAssert.Contains(text, marker, StringComparison.Ordinal);

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
                StringAssert.Contains(text, parameterMarker, StringComparison.Ordinal);
            }
        }
    }
}
