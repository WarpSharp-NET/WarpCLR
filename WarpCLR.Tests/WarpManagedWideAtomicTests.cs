using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this internal fixture.")]
internal sealed partial class WarpManagedWideAtomicTests
{
    [TestMethod]
    public void EachAlignedOperationPreservesBothWordsAndWrappingResults()
    {
        foreach (WarpLogicalMachineLayout layout in WarpManagedWideAtomicKernels.Create64())
        {
            CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
            foreach (ulong original in Values())
            foreach (ulong operand in Values())
            foreach (int quantum in new[] { layout.MaximumBlockCost, 4096 })
            {
                ulong expected = Expected(layout, original, operand, original, out ulong updated);
                uint[] arena = [unchecked((uint)original), (uint)(original >> 32), 0xBADC0FFE];
                uint[] state = Run(core, Arguments(layout, 0, operand, original), arena, quantum);
                Assert.AreEqual(expected, Pair(state, WarpLogicalMachineLayout.ResultOffset), layout.Kernel.Name);
                Assert.AreEqual(updated, Pair(arena, 0), layout.Kernel.Name);
                Assert.AreEqual(0xBADC0FFEu, arena[2]);
            }
        }
    }

    [TestMethod]
    public void CompareExchangeUsesFullComparandAndReturnsOneOriginalPair()
    {
        WarpLogicalMachineLayout layout = Layout("compare-exchange");
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        const ulong original = 0xFEDCBA9876543210, replacement = 0x123456789ABCDEF0;
        foreach (ulong mismatch in new[] { original ^ (1UL << 63), original ^ 1, 0UL })
        {
            uint[] arena = [unchecked((uint)original), (uint)(original >> 32)];
            uint[] state = Run(core, Arguments(layout, 0, replacement, mismatch), arena, 4096);
            Assert.AreEqual(original, Pair(state, WarpLogicalMachineLayout.ResultOffset));
            Assert.AreEqual(original, Pair(arena, 0));
        }
    }

    [TestMethod]
    public void BoundsAndAlignmentFaultBeforeAnyMemorySideEffect()
    {
        foreach (WarpLogicalMachineLayout layout in WarpManagedWideAtomicKernels.Create64())
        {
            CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
            foreach ((uint index, uint fault) in new[] { (1u, 9u), (3u, 4u), (uint.MaxValue, 4u) })
            {
                uint[] arena = [9, 10, 11, 12], before = (uint[])arena.Clone();
                uint[] state = layout.CreateInitialState(1, 100);
                core.ExecuteManagedQuantum(Arguments(layout, index, ulong.MaxValue, 10), [], 0, state, 1, 4096, arena);
                Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[0]); Assert.AreEqual(fault, state[1]);
                CollectionAssert.AreEqual(before, arena);
                Assert.AreEqual(0UL, Pair(state, WarpLogicalMachineLayout.ResultOffset));
            }
            uint[] shortState = layout.CreateInitialState(1, 100);
            core.ExecuteManagedQuantum(Arguments(layout, 0, 1, 0), [], 0, shortState, 1, 4096, [9]);
            Assert.AreEqual(WarpLogicalMachineLayout.ManagedMemoryBoundsFault, shortState[1]);
        }
    }

    [TestMethod]
    public async Task ConcurrentFullWidthAddsReturnEveryUniqueValueAcrossCarryAndOverflow()
    {
        WarpLogicalMachineLayout layout = Layout("add");
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        const ulong initial = ulong.MaxValue - 511;
        uint[] arena = [unchecked((uint)initial), (uint)(initial >> 32)];
        var seen = new ConcurrentBag<ulong>();
        Task[] tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int count = 0; count < 256; count++)
            {
                uint[] state = Run(core, [[0], [1], [0]], arena, layout.MaximumBlockCost);
                seen.Add(Pair(state, WarpLogicalMachineLayout.ResultOffset));
            }
        })).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        Assert.AreEqual(unchecked(initial + 1024), Pair(arena, 0));
        CollectionAssert.AreEqual(Enumerable.Range(1, 1024).Select(index => unchecked(initial + (ulong)index)).Order().ToArray(), seen.Order().ToArray());
    }

    [TestMethod]
    public async Task ConcurrentExchangesNeverReturnAPairAssembledFromDifferentEvents()
    {
        WarpLogicalMachineLayout layout = Layout("exchange");
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[] arena = [0, uint.MaxValue];
        Task[] tasks = Enumerable.Range(1, 4).Select(worker => Task.Run(() =>
        {
            for (uint index = 0; index < 256; index++)
            {
                uint low = (uint)worker * 256 + index;
                uint[] state = Run(core, [[0], [low], [~low]], arena, 4096);
                Assert.AreEqual(~state[WarpLogicalMachineLayout.ResultOffset], state[WarpLogicalMachineLayout.ResultHighOffset]);
            }
        })).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        Assert.AreEqual(~arena[0], arena[1]);
    }

    private static WarpLogicalMachineLayout Layout(string name) => WarpManagedWideAtomicKernels.Create64()
        .First(layout => layout.Kernel.Name.EndsWith("/" + name, StringComparison.Ordinal));

    private static uint[] Run(CoreCLRResumableKernel core, uint[][] arguments, uint[] arena, int quantum)
    {
        uint[] state = core.Layout.CreateInitialState(1, 1000);
        core.ExecuteManagedQuantum(arguments, [], 0, state, 1, quantum, arena);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]);
        return state;
    }

    private static ulong Pair(uint[] values, int offset) => values[offset] | ((ulong)values[offset + 1] << 32);

    private static ulong[] Values() => [0, 1, uint.MaxValue, 1UL << 32, 1UL << 63, ulong.MaxValue, 0x7FF0123456789ABC, 0xFEDCBA9876543210];

    private static uint[][] Arguments(WarpLogicalMachineLayout layout, uint address, ulong operand, ulong comparand) =>
        layout.Kernel.InputBufferCount == 1 ? [[address]] : layout.Kernel.InputBufferCount == 3 ?
            [[address], [unchecked((uint)operand)], [(uint)(operand >> 32)]] :
            [[address], [unchecked((uint)comparand)], [(uint)(comparand >> 32)], [unchecked((uint)operand)], [(uint)(operand >> 32)]];

    private static ulong Expected(WarpLogicalMachineLayout layout, ulong original, ulong operand, ulong comparand, out ulong updated)
    {
        WarpIrOpCode code = layout.Kernel.Instructions[^1].OpCode;
        updated = code switch
        {
            WarpManagedWideAtomicOpCode.StoreSequential or WarpManagedWideAtomicOpCode.StoreRelease or WarpManagedWideAtomicOpCode.Exchange => operand,
            WarpManagedWideAtomicOpCode.CompareExchange => original == comparand ? operand : original,
            WarpManagedWideAtomicOpCode.Add => unchecked(original + operand),
            WarpManagedWideAtomicOpCode.Increment => unchecked(original + 1),
            WarpManagedWideAtomicOpCode.Decrement => unchecked(original - 1),
            WarpManagedWideAtomicOpCode.And => original & operand,
            WarpManagedWideAtomicOpCode.Or => original | operand,
            _ => original,
        };
        return code is WarpManagedWideAtomicOpCode.Add or WarpManagedWideAtomicOpCode.Increment or WarpManagedWideAtomicOpCode.Decrement or
            WarpManagedWideAtomicOpCode.StoreSequential or WarpManagedWideAtomicOpCode.StoreRelease ? updated : original;
    }
}
