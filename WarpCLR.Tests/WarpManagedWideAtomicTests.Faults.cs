using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;
using WarpCLR.Compiler;

namespace WarpCLR.Tests;

internal sealed partial class WarpManagedWideAtomicTests
{
    [TestMethod]
    public async Task ActualChildReportsAlignmentAndBoundsWithoutCommittingAtomicEffects()
    {
        foreach (WarpLogicalMachineLayout layout in WarpManagedWideAtomicKernels.Create64())
        {
            WarpCoreCLRWorkerKernel child = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
            await using var owner = child.ConfigureAwait(false);
            WarpCoreCLRWorkerLease lease = child.TryAcquireLease()!;
            await using var leaseOwner = lease.ConfigureAwait(false);
            foreach (int quantum in new[] { layout.MaximumBlockCost, 4096 })
            foreach ((uint address, uint fault) in new[] { (1u, 9u), (3u, 4u), (uint.MaxValue, 4u) })
            {
                uint[] state = layout.CreateInitialState(1, 1000);
                uint[] arena = [0x12345678, 0x9ABCDEF0, 0x7FA12345, 0x80000000], before = (uint[])arena.Clone();
                await lease.ExecuteManagedQuantumAsync(Arguments(layout, address, ulong.MaxValue, 0), [], 0,
                    state, 1, quantum, arena, CancellationToken.None).ConfigureAwait(false);
                Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[0]); Assert.AreEqual(fault, state[1]);
                CollectionAssert.AreEqual(before, arena);
                Assert.AreEqual(0UL, Pair(state, WarpLogicalMachineLayout.ResultOffset));
                WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 1);
            }
        }
    }

    [TestMethod]
    public void AlignmentFaultIsRejectedForLayoutsWithoutWideAtomicCapability()
    {
        WarpLogicalMachineLayout layout = WarpManagedAtomicKernels.Create32()[0];
        uint[] state = layout.CreateInitialState(1, 1000);
        state[0] = WarpLogicalMachineLayout.Faulted;
        state[WarpLogicalMachineLayout.FaultKindOffset] = WarpLogicalMachineLayout.AtomicAlignmentFault;
        WarpHostException error = Assert.ThrowsExactly<WarpHostException>(() => WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 1));
        Assert.AreEqual("WRPNATIVE1007", error.Code, StringComparer.Ordinal);
    }
}
