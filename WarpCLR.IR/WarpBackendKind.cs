using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public enum WarpBackendKind
{
    CoreCLR,
    NVPTX,
    AMDGPU,
    SPIRV,
}
