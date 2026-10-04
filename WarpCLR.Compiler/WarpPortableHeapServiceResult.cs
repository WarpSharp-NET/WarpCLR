using System.Runtime.InteropServices;

namespace WarpCLR.Compiler;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct WarpPortableHeapServiceResult(uint Fault, uint Operation, uint Argument0, uint Argument1,
    uint Word0, uint Word1, uint Word2, uint Word3, uint Word4, uint Word5)
{
    public WarpPortableHeapReference Reference => new(Word0, Word1, Word2);
}
