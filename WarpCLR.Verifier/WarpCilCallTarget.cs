using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed record WarpCilCallTarget(
    int FunctionId,
    int ParameterCount,
    string Identity);
