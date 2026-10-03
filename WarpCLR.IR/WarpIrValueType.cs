using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

public enum WarpIrValueType
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "This IR value kind denotes the exact CLR UInt32 primitive type; changing it would obscure the type contract.")]
    UInt32,
}
