using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

[StructLayout(LayoutKind.Auto)]
public readonly record struct WarpFeatureDescriptor(
    WarpProfileFeature Feature,
    WarpFeatureLayer Layer);
