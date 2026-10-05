using System.Runtime.InteropServices;

namespace WarpCLR.IR;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct WarpStateDispatchTarget(int Function, int Block);
