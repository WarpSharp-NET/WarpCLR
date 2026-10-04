using System.Runtime.InteropServices;

namespace WarpCLR.Compiler;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct WarpPortableHeapReferenceLayout(uint Offset, uint TypeId);
