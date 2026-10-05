using System.Buffers.Binary;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpManagedWideAtomicTests
{
    [TestMethod]
    [DataRow(4u)]
    [DataRow(8u)]
    public void GeneratedProtectedByteServicesMatchExactPackedByteOracle(uint width)
    {
        WarpLogicalMachineLayout layout = WarpPortableAtomicWordKernels.Arena();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        for (uint shift = 0; shift < 4; shift++)
        for (uint operation = 1; operation <= 11; operation++)
        foreach (ulong original in Values())
        foreach (int quantum in new[] { layout.MaximumBlockCost, 4096 })
        {
            uint[] arena = ProtectedArena(out uint result, out uint target);
            byte[] before = Bytes(arena);
            int address = checked((int)(target * 4 + shift));
            WriteOracle(before, address, width, original); BytesToWords(before, arena);
            ulong actualOriginal = width == 4 ? unchecked((uint)original) : original;
            ulong operand = width == 4 ? unchecked((uint)~original) : ~original;
            ulong expected = Expected(WarpManagedWideAtomicKernels.Create64()[(int)operation - 1], actualOriginal, operand, actualOriginal, out ulong updated);
            if (width == 4) { expected = unchecked((uint)expected); updated = unchecked((uint)updated); }
            byte[] expectedArena = (byte[])before.Clone(); WriteOracle(expectedArena, address, width, updated);
            BinaryPrimitives.WriteUInt32LittleEndian(expectedArena.AsSpan((int)result * 4), unchecked((uint)expected));
            BinaryPrimitives.WriteUInt32LittleEndian(expectedArena.AsSpan((int)(result + 1) * 4), (uint)(expected >> 32));
            uint[] state = RunProtected(core, ProtectedArguments(target, shift, width, operation, actualOriginal, operand, result), arena, quantum);
            Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]);
            CollectionAssert.AreEqual(expectedArena, Bytes(arena));
        }
    }

    [TestMethod]
    public void InvalidProtectedGrantsSpansAndOverlappingScratchHaveNoSideEffects()
    {
        WarpLogicalMachineLayout layout = WarpPortableAtomicWordKernels.Arena();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        foreach ((uint controller, uint word, uint shift, uint width, uint operation, uint scratch, uint status) in
            new[] { (2u, 0u, 0u, 8u, 5u, 0u, 1u), (1u, uint.MaxValue, 0u, 8u, 5u, 0u, 2u),
                (1u, 0u, 4u, 8u, 5u, 0u, 3u), (1u, 0u, 0u, 3u, 5u, 0u, 3u), (1u, 0u, 0u, 8u, 12u, 0u, 3u),
                (1u, 0u, 0u, 8u, 5u, 1u, 3u) })
        {
            uint[] arena = ProtectedArena(out uint result, out uint target), before = (uint[])arena.Clone();
            uint[][] arguments = ProtectedArguments(word == 0 ? target : word, shift, width, operation, 0, 1, scratch == 0 ? result : target);
            arguments[1][0] = controller;
            uint[] state = RunProtected(core, arguments, arena, layout.MaximumBlockCost);
            Assert.AreEqual(status, state[WarpLogicalMachineLayout.ResultOffset]); CollectionAssert.AreEqual(before, arena);
        }
    }

    [TestMethod]
    public async Task ActualChildExecutesPackedByteServiceAtBothQuanta()
    {
        WarpLogicalMachineLayout layout = WarpPortableAtomicWordKernels.Arena();
        WarpCoreCLRWorkerKernel child = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var owner = child.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = child.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        for (uint shift = 0; shift < 4; shift++)
        foreach (int quantum in new[] { layout.MaximumBlockCost, 4096 })
        {
            uint[] arena = ProtectedArena(out uint result, out uint target);
            byte[] bytes = Bytes(arena); WriteOracle(bytes, checked((int)(target * 4 + shift)), 8, ulong.MaxValue); BytesToWords(bytes, arena);
            uint[] state = layout.CreateInitialState(32, 1000000);
            uint[][] arguments = ProtectedArguments(target, shift, 8, 6, 0, 0, result);
            int calls = 0;
            do
            {
                await lease.ExecuteManagedQuantumAsync(arguments, [], 0, state, 32, quantum, arena, CancellationToken.None).ConfigureAwait(false);
                Assert.IsLessThan(10000, ++calls);
            } while (state[0] == WarpLogicalMachineLayout.Runnable);
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]); Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(0UL, Pair(arena, (int)result));
            Assert.AreEqual(0UL, BinaryPrimitives.ReadUInt64LittleEndian(Bytes(arena).AsSpan(checked((int)(target * 4 + shift)))));
            Assert.AreEqual(1u, arena[WarpPortableSchedulerLayout.ControllerOwner]);
        }
    }

    private static uint[] ProtectedArena(out uint result, out uint target)
    {
        var schema = new WarpPortableSchedulerSchema(1, 1, 4096, 32, 1000000, 0, 16, [new(0, 0, [])], []);
        uint[] metadata = schema.CreateArena(101);
        uint[] arena = new uint[metadata.Length + 32]; metadata.CopyTo(arena, 0);
        arena[WarpPortableSchedulerLayout.ArenaWords] = (uint)arena.Length;
        arena[WarpPortableSchedulerLayout.ControllerOwner] = 1;
        result = (uint)metadata.Length; target = result + 4;
        arena.AsSpan(metadata.Length).Fill(0xA1B2C3D4);
        return arena;
    }

    private static uint[][] ProtectedArguments(uint target, uint shift, uint width, uint operation, ulong comparand, ulong value, uint result) =>
        [[0], [1], [1], [0], [target], [shift], [width], [operation], [unchecked((uint)comparand)], [(uint)(comparand >> 32)],
            [unchecked((uint)value)], [(uint)(value >> 32)], [result]];

    private static uint[] RunProtected(CoreCLRResumableKernel core, uint[][] arguments, uint[] arena, int quantum)
    {
        uint[] state = core.Layout.CreateInitialState(32, 1000000);
        int calls = 0;
        do
        {
            core.ExecuteManagedQuantum(arguments, [], 0, state, 32, quantum, arena);
            Assert.IsLessThan(10000, ++calls);
        } while (state[0] == WarpLogicalMachineLayout.Runnable);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]);
        return state;
    }

    private static byte[] Bytes(uint[] words)
    {
        byte[] bytes = new byte[words.Length * 4];
        for (int index = 0; index < words.Length; index++) { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), words[index]); }
        return bytes;
    }

    private static void BytesToWords(byte[] bytes, uint[] words)
    {
        for (int index = 0; index < words.Length; index++) { words[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * 4)); }
    }

    private static void WriteOracle(byte[] bytes, int address, uint width, ulong value)
    {
        if (width == 8) { BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(address), value); }
        else { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(address), unchecked((uint)value)); }
    }
}
