using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableFaultTicketDriver
{
    internal uint Command(uint command, uint? raise = null)
    {
        uint scratch = Arena[WarpPortableHeapLayout.ScratchStart];
        Parameters.CopyTo(Arena, checked((int)scratch + 8));
        Arena[scratch + 8 + (uint)Parameters.Length] = raise ?? 0;
        Arena[scratch + 1] = uint.MaxValue;
        Arena[scratch] = command;
        DriveUntil(() => Arena[scratch] == 0 && AtWait());
        return Arena[scratch + 1];
    }

    private bool AtWait()
    {
        if (State[WarpLogicalMachineLayout.DepthOffset] != 2) { return false; }
        int frame = WarpLogicalMachineLayout.HeaderWords + Program.Layout.FrameWords;
        WarpLogicalMachineNode node = Program.Layout.Nodes[checked((int)State[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset])];
        return node.Function == 1 && (node.Block >= Program.Waiting && node.Block <= Program.Waiting + 2 ||
            node.Block == Program.Waiting + 7);
    }

    private void DriveUntil(Func<bool> completed)
    {
        for (int iteration = 0; iteration < 1000000; iteration++)
        {
            if (completed()) { return; }
            Assert.AreEqual(WarpLogicalMachineLayout.Runnable, State[WarpLogicalMachineLayout.StatusOffset],
                $"Protocol machine fault {State[WarpLogicalMachineLayout.FaultKindOffset]} at {State[WarpLogicalMachineLayout.FaultFunctionOffset]}/{State[WarpLogicalMachineLayout.FaultBlockOffset]}");
            compiled.ExecuteManagedQuantum([[0]], [], 0, State, 128, quantum, Arena, CancellationToken.None);
        }
        Assert.Fail("The generated prepared-fault consistency fixture exceeded its finite quantum bound.");
    }
}
