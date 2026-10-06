using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Tests;

// Controlled generated-entry fixture only. No child command, Root grant or
// genuine guest wait/cctor/filter admission is constructed by this fixture.
internal sealed class WarpPrivateHelperReturnFenceFixture
{
    internal const int MaximumDepth = 8;
    internal const uint Controller = 0x81234567;
    internal const string Service = "warp.proof.helper-return-fence/raw-u32-quad/0.1";

    internal WarpPrivateHelperReturnFenceFixture(bool nested = false, bool guestSuccessor = false, bool returnFences = true)
    {
        Kernel = Create(nested, guestSuccessor, returnFences);
        Layout = new(Kernel); Compiled = CoreCLRResumableKernel.Compile(Layout);
    }

    internal WarpControlFlowKernel Kernel { get; }
    internal WarpLogicalMachineLayout Layout { get; }
    internal CoreCLRResumableKernel Compiled { get; }

    internal void Invoke(uint[] state, uint[] arena, uint controller, int quantum) =>
        Compiled.CompiledEntryPoint.Invoke(null, [new uint[][] { [0] }, new uint[] { controller }, 0,
            state, MaximumDepth, quantum, CancellationToken.None, arena]);

    internal uint[] ParkBeforeHelper(uint[] arena, int quantum)
    {
        uint[] state = Layout.CreateInitialState(MaximumDepth, 100); Layout.SetSourceBoundaryMode(state, true);
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.NeedsPrivateHelper) { return state; }
            if (state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.BeforeSourceBoundary)
            { WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state); }
            Invoke(state, arena, 0xDEADBEEF, quantum);
        }
        throw new InvalidOperationException("The controlled source bridge did not park.");
    }

    internal uint[] ParkAfterHelper(uint[] arena, int quantum)
    {
        uint[] state = ParkBeforeHelper(arena, quantum); Layout.AcknowledgePrivateHelperBoundary(state);
        for (int attempt = 0; attempt < 100; attempt++)
        {
            Invoke(state, arena, Controller, quantum);
            if (state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.AwaitingRootRelease) { return state; }
            Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[0]);
        }
        throw new InvalidOperationException("The controlled helper return did not fence.");
    }

    private static WarpControlFlowKernel Create(bool nested, bool guestSuccessor, bool returnFences)
    {
        WarpBasicBlock[] root = [new(0, [], [new(0, 0, [], 4)], new WarpTupleReturnTerminator([0, 1, 2, 3]))];
        WarpBlockTerminator end = guestSuccessor ? new WarpBranchTerminator(new(2, [])) : new WarpTupleReturnTerminator([11, 12, 13, 14]);
        WarpControlFlowFunction[] functions =
        [
            new(0, "synthetic-source-caller", 0, [new(0, [], [new(0, WarpIrOpCode.Constant, immediate: 0xA5A5A5A5),
                new(1, WarpManagedFrameOpCode.StorePrivateWord, 0, immediate: 0), new(2, WarpManagedFrameOpCode.StorePrivateWord, 0, immediate: 1)],
                new WarpBranchTerminator(new(1, []))), new(1, [], [new(8, WarpPrivateControllerOpCode.LoadController),
                new(9, WarpIrOpCode.Constant, immediate: 0x7FC12345), new(10, WarpIrOpCode.Constant, immediate: 0x80000000),
                new(11, 1, [8, 9, 10], 4), new(15, WarpManagedFrameOpCode.StorePrivateWord, 11, immediate: 0),
                new(16, WarpManagedFrameOpCode.StorePrivateWord, 12, immediate: 1)], end),
                new(2, [], [new(17, 3, [], 1), new(18, WarpManagedFrameOpCode.LoadPrivateWord, immediate: 0),
                    new(19, WarpManagedFrameOpCode.LoadPrivateWord, immediate: 1), new(20, WarpIrOpCode.Constant, immediate: 0x80000000),
                    new(21, WarpIrOpCode.Constant, immediate: uint.MaxValue)], new WarpTupleReturnTerminator([18, 19, 20, 21]))]),
            new(1, Service, 3, [nested ? new(0, [], [new(0, WarpIrOpCode.LoadArgument), new(1, WarpIrOpCode.LoadArgument, immediate: 1),
                new(2, WarpIrOpCode.LoadArgument, immediate: 2), new(3, 2, [0, 1, 2], 4)], new WarpTupleReturnTerminator([3, 4, 5, 6])) : RawService()]),
            new(2, Service + "/nested", 3, [RawService()]),
            new(3, "synthetic-guest-successor", 0, [new(0, [], [new(0, WarpIrOpCode.Constant, immediate: 1),
                new(1, WarpIrOpCode.Constant, immediate: 0xBAD0C0DE), new(2, WarpManagedMemoryOpCode.StoreWord, 0, 1)], new WarpReturnTerminator(1))]),
        ];
        WarpLogicalBodyMetadata[] bodies = [new(0, true, [0], countsSourceDepth: false), new(2, false, [1, 0, 0]),
            new(0, true, [0], countsSourceDepth: false), new(0, true, [0], countsSourceDepth: false), new(0, false, [1])];
        var metadata = new WarpLogicalExecutionMetadata(bodies, frameOwners: true, runtimeStateAccess: true,
            privateControllerProjection: new([new(1, 1, 8, 1, 0, 11, Service)], true, returnFences));
        return new("controlled-private-helper-return-fence", 1, 1, root, null, functions, metadata);
    }

    private static WarpBasicBlock RawService() => new(0, [], [new(0, WarpIrOpCode.LoadArgument),
        new(1, WarpIrOpCode.LoadArgument, immediate: 1), new(2, WarpIrOpCode.LoadArgument, immediate: 2),
        new(3, WarpIrOpCode.Constant, immediate: uint.MaxValue), new(4, WarpIrOpCode.Constant),
        new(5, WarpManagedMemoryOpCode.StoreWord, 4, 0)], new WarpTupleReturnTerminator([0, 1, 2, 3]));
}
