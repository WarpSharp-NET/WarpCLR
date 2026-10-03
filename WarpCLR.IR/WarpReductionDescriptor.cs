using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

[StructLayout(LayoutKind.Auto)]
public readonly record struct WarpReductionDescriptor(
    WarpReductionOperation Operation,
    uint Identity);
