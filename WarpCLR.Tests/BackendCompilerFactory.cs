using WarpCLR.Backend.AMDGPU;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Backend.SPIRV;
using WarpCLR.Backend.NVPTX;
using WarpCLR.IR;

namespace WarpCLR.Tests;

internal static class BackendCompilerFactory
{
    public static IWarpBackendCompiler Create(WarpBackendKind backend) => backend switch
    {
        WarpBackendKind.CoreCLR => new CoreCLRBackendCompiler(),
        WarpBackendKind.NVPTX => new NVPTXBackendCompiler(),
        WarpBackendKind.AMDGPU => new AMDGPUBackendCompiler(),
        WarpBackendKind.SPIRV => new SPIRVBackendCompiler(),
        _ => throw new ArgumentOutOfRangeException(nameof(backend)),
    };
}
