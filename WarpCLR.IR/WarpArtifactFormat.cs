using System.Security.Cryptography;
using System.Text;

namespace WarpCLR.IR;

public enum WarpArtifactFormat
{
    CoreCLRPlan,
    NVPTX,
    AMDGPULLVMIR,
    SPIRVLLVMIR,
}
