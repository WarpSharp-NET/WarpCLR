using System.Buffers.Binary;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpManagedWideAtomicTests
{
    [TestMethod]
    public async Task ProtectedStateServiceAddressesTheExecutingPrivateBankAndDistinctArenaScratch()
    {
        WarpLogicalMachineLayout layout = StateLayout();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        WarpCoreCLRWorkerKernel child = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var owner = child.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = child.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        foreach (int quantum in new[] { layout.MaximumBlockCost, 4096 })
        for (uint shift = 0; shift < 4; shift++)
        {
            uint[] arena = ProtectedArena(out uint result, out _), remoteArena = (uint[])arena.Clone();
            uint[] local = layout.CreateInitialState(32, 1000000), remote = (uint[])local.Clone();
            uint target = (uint)(WarpLogicalMachineLayout.HeaderWords + layout.PrivateOffset + 1);
            byte[] bytes = Bytes(local);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(checked((int)(target * 4 + shift))), ulong.MaxValue);
            BytesToWords(bytes, local); local.CopyTo(remote, 0);
            uint[][] inputs = ProtectedArguments(target, shift, 8, 6, 0, 0, result);
            int calls = 0;
            do
            {
                core.ExecuteManagedQuantum(inputs, [], 0, local, 32, quantum, arena);
                await lease.ExecuteManagedQuantumAsync(inputs, [], 0, remote, 32, quantum, remoteArena, CancellationToken.None).ConfigureAwait(false);
                CollectionAssert.AreEqual(local, remote); CollectionAssert.AreEqual(arena, remoteArena);
                Assert.IsLessThan(10000, ++calls);
            } while (local[0] == WarpLogicalMachineLayout.Runnable);
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, local[0]); Assert.AreEqual(0u, local[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(0UL, Pair(arena, (int)result));
            Assert.AreEqual(0UL, BinaryPrimitives.ReadUInt64LittleEndian(Bytes(local).AsSpan(checked((int)(target * 4 + shift)))));
        }
    }

    private static WarpLogicalMachineLayout StateLayout()
    {
        WarpControlFlowKernel source = WarpPortableAtomicWordKernels.State().Kernel;
        WarpLogicalExecutionMetadata original = source.Execution!;
        var execution = new WarpLogicalExecutionMetadata(original.Bodies.Select((body, index) =>
            new WarpLogicalBodyMetadata(index == 0 ? 16 : body.PrivateWordCount, body.RuntimeHelper, body.SourceBlockCosts)),
            original.RecursiveCalls, original.FrameOwners, original.RuntimeStateAccess, original.NonlocalStateDispatch);
        return new(new WarpControlFlowKernel(source.Name + "/private-bank-witness", source.InputBufferCount, source.ScalarArgumentCount,
            source.Blocks, source.Reduction, source.Functions, execution));
    }
}
