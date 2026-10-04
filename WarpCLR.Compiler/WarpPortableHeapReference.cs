using System.Runtime.InteropServices;

namespace WarpCLR.Compiler;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct WarpPortableHeapReference(uint Context, uint Slot, uint Generation)
{
    public bool IsNull => (Context | Slot | Generation) == 0;
}
