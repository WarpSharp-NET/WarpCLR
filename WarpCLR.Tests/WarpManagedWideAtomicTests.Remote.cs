using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpManagedWideAtomicTests
{
    [TestMethod]
    public async Task ActualChildRetainsFullWidthOperationsAtBothQuanta()
    {
        foreach (WarpLogicalMachineLayout layout in WarpManagedWideAtomicKernels.Create64())
        {
            WarpCoreCLRWorkerKernel child = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
            await using var owner = child.ConfigureAwait(false);
            WarpCoreCLRWorkerLease lease = child.TryAcquireLease()!;
            await using var leaseOwner = lease.ConfigureAwait(false);
            Assert.AreNotEqual(Environment.ProcessId, lease.ProcessId);
            Assert.IsFalse(AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.ManifestModule.ModuleVersionId == lease.CompiledModule));
            foreach (int quantum in new[] { layout.MaximumBlockCost, 4096 })
            foreach (ulong original in Values())
            {
                ulong operand = original ^ ulong.MaxValue;
                ulong expected = Expected(layout, original, operand, original ^ (1UL << 63), out ulong updated);
                uint[] state = layout.CreateInitialState(1, 1000);
                uint[] arena = [unchecked((uint)original), (uint)(original >> 32)];
                await lease.ExecuteManagedQuantumAsync(Arguments(layout, 0, operand, original ^ (1UL << 63)), [], 0,
                    state, 1, quantum, arena, CancellationToken.None).ConfigureAwait(false);
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]);
                Assert.AreEqual(expected, Pair(state, WarpLogicalMachineLayout.ResultOffset)); Assert.AreEqual(updated, Pair(arena, 0));
            }
        }
    }

    [TestMethod]
    public void CodecPreservesAtomicOpcodeWidthOperandOrderAndNonlocalMetadata()
    {
        foreach (WarpLogicalMachineLayout layout in WarpManagedWideAtomicKernels.Create64().Concat(WarpExceptionMachineHookKernels.Create()))
        {
            WarpControlFlowKernel original = layout.Kernel;
            WarpControlFlowKernel copy = WarpCoreCLRBinaryPlanCodec.Deserialize(WarpCoreCLRBinaryPlanCodec.Serialize(original), WarpIrHash.Compute(original));
            Assert.AreEqual(WarpIrHash.Compute(original), WarpIrHash.Compute(copy), StringComparer.Ordinal);
            Assert.AreEqual(original.Execution?.NonlocalStateDispatch, copy.Execution?.NonlocalStateDispatch);
            foreach ((WarpIrInstruction left, WarpIrInstruction right) in original.Instructions.Zip(copy.Instructions))
            {
                Assert.AreEqual(left.OpCode, right.OpCode); Assert.AreEqual(left.ResultWordCount, right.ResultWordCount);
                CollectionAssert.AreEqual(left.Arguments.ToArray(), right.Arguments.ToArray());
            }
            Assert.ThrowsExactly<NotSupportedException>(() => WarpCoreCLRPlanCodec.Serialize(original));
        }
        WarpControlFlowKernel sc = SameIdentity(WarpManagedWideAtomicOpCode.LoadSequential);
        WarpControlFlowKernel acquire = SameIdentity(WarpManagedWideAtomicOpCode.LoadAcquire);
        Assert.AreNotEqual(WarpIrHash.Compute(sc), WarpIrHash.Compute(acquire), StringComparer.Ordinal);
        Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel("bad-wide-result", 1, 0,
            [new WarpBasicBlock(0, [], [new(0, WarpIrOpCode.LoadInput), new(1, WarpManagedWideAtomicOpCode.LoadSequential, 0)], new WarpReturnTerminator(1))]));
    }

    [TestMethod]
    public async Task ActualChildCompilesAndExecutesAuthenticatedNonlocalDispatch()
    {
        foreach (WarpLogicalMachineLayout layout in new[] { WarpExceptionMachineHookKernels.CreateUnwindTransfer(), WarpExceptionMachineHookKernels.CreateTerminalTransfer() })
        {
            WarpCoreCLRWorkerKernel child = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
            await using var owner = child.ConfigureAwait(false);
            WarpCoreCLRWorkerLease lease = child.TryAcquireLease()!;
            await using var leaseOwner = lease.ConfigureAwait(false);
            foreach (int quantum in new[] { layout.MaximumBlockCost, 4096 })
            {
                uint[] state = layout.CreateInitialState(4, 1000);
                int calls = 0;
                do
                {
                    await lease.ExecuteManagedQuantumAsync([[123]], [], 0, state, 4, quantum, [], CancellationToken.None).ConfigureAwait(false);
                    Assert.IsLessThan(1000, ++calls);
                } while (state[0] == WarpLogicalMachineLayout.Runnable);
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]);
                Assert.AreEqual(layout.Kernel.Name.EndsWith("terminal", StringComparison.Ordinal) ? 0x7FA12345u : 99u, state[WarpLogicalMachineLayout.ResultOffset]);
            }
        }
    }

    private static WarpControlFlowKernel SameIdentity(WarpIrOpCode code) => new("identical-name", 1, 0,
        [new WarpBasicBlock(0, [], [new(0, WarpIrOpCode.LoadInput), new WarpIrInstruction(1, code, 0, [], 2)], new WarpTupleReturnTerminator([1, 2]))]);
}
