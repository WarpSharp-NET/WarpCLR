using System.Globalization;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

internal static class WarpNativeAtomicAdmission
{
    internal static void Validate(WarpLogicalMachineLayout layout, WarpNativeTarget target)
    {
        bool atomic = false, wide = false;
        foreach (WarpIrInstruction instruction in layout.Kernel.Instructions.Concat(layout.Kernel.Functions.SelectMany(function => function.Instructions)))
        {
            atomic |= WarpManagedAtomicOpCode.IsAtomic(instruction.OpCode);
            wide |= WarpManagedWideAtomicOpCode.IsAtomic(instruction.OpCode);
        }
        if (!target.SupportsInt64Atomics && wide)
        {
            throw new WarpHostException("WRPNATIVE1003", "The admitted native target lacks full-width Int64 atomic capabilities.");
        }
        if (target.Backend == WarpBackendKind.NVPTX && atomic &&
            uint.Parse(target.Architecture.AsSpan(3), CultureInfo.InvariantCulture) < 70)
        {
            throw new WarpHostException("WRPNATIVE1003", "The portable atomic memory-order contract requires NVPTX sm_70 or later.");
        }
    }
}
