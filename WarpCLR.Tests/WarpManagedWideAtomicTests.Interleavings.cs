using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests;

internal sealed partial class WarpManagedWideAtomicTests
{
    [TestMethod]
    public async Task OverlappingPacked32And64EventsAreLinearizableUnderExclusiveGeneratedGrants()
    {
        CoreCLRResumableKernel operation = CoreCLRResumableKernel.Compile(WarpPortableAtomicWordKernels.Arena());
        CoreCLRResumableKernel cas = CoreCLRResumableKernel.Compile(WarpManagedAtomicKernels.Create32()[2]);
        CoreCLRResumableKernel ordinal = CoreCLRResumableKernel.Compile(WarpWordArenaServiceLowerer.Lower(
            typeof(WarpPackedAtomicWitnessServices).GetMethod(nameof(WarpPackedAtomicWitnessServices.AppendOrdinal), BindingFlags.Public | BindingFlags.Static)!));
        uint[] arena = ProtectedArena(out uint result, out uint target);
        arena[WarpPortableSchedulerLayout.ControllerOwner] = 0; arena[result + 2] = 0;
        byte[] expectedArena = Bytes(arena);
        var events = new ConcurrentBag<PackedEvent>();
        Task[] workers = Enumerable.Range(1, 4).Select(actor => Task.Run(async () =>
        {
            for (uint index = 0; index < 24; index++)
            {
                uint token = (uint)actor;
                await ClaimGeneratedAsync(cas, arena, token).ConfigureAwait(false);
                uint width = actor % 2 == 0 ? 4u : 8u;
                uint byteOffset = width == 8 ? 1u : actor == 2 ? 4u : 7u;
                uint code = new uint[] { 2, 3, 4, 5, 8, 9, 1, 10, 11 }[index % 9];
                ulong value = 0x7654321000000000UL | ((ulong)token << 28) | index;
                uint[][] inputs = ProtectedArguments(target + (byteOffset >> 2), byteOffset & 3, width, code, value, value, result);
                inputs[1][0] = token;
                uint[] state = RunProtected(operation, inputs, arena, operation.Layout.MaximumBlockCost);
                Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]);
                uint[] numbered = RunProtected(ordinal, [[WarpPortableSchedulerLayout.ControllerOwner], [token], [result + 2]], arena, 4096);
                events.Add(new(numbered[WarpLogicalMachineLayout.ResultOffset], width, byteOffset, code, value, Pair(arena, (int)result)));
                Assert.AreEqual(token, Run(cas, [[WarpPortableSchedulerLayout.ControllerOwner], [token], [0]], arena, 4096)[WarpLogicalMachineLayout.ResultOffset]);
            }
        })).ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);
        VerifyPackedHistory(events, expectedArena, arena, target, result);
    }

    [TestMethod]
    public async Task FullWidthReleaseAndAcquirePublishOrdinaryDataWithBothFlagWords()
    {
        CoreCLRResumableKernel release = CoreCLRResumableKernel.Compile(Layout("store-release"));
        CoreCLRResumableKernel acquire = CoreCLRResumableKernel.Compile(Layout("load-acquire"));
        uint[] arena = [0, 0, 0];
        const ulong flag = 0xFEDCBA9876543210;
        Task publisher = Task.Run(() =>
        {
            arena[2] = 0xAABBCCDD;
            Run(release, [[0], [unchecked((uint)flag)], [(uint)(flag >> 32)]], arena, 4096);
        });
        ulong observed = 0;
        for (int attempt = 0; attempt < 100000 && observed == 0; attempt++)
        {
            observed = Pair(Run(acquire, [[0]], arena, 4096), WarpLogicalMachineLayout.ResultOffset);
            if (observed == 0) { await Task.Yield(); }
        }
        Assert.AreEqual(flag, observed); Assert.AreEqual(0xAABBCCDDu, arena[2]);
        await publisher.ConfigureAwait(false);
    }

    private static async Task ClaimGeneratedAsync(CoreCLRResumableKernel cas, uint[] arena, uint token)
    {
        for (int attempt = 0; attempt < 100000; attempt++)
        {
            uint[] state = Run(cas, [[WarpPortableSchedulerLayout.ControllerOwner], [0], [token]], arena, 4096);
            if (state[WarpLogicalMachineLayout.ResultOffset] == 0) { return; }
            await Task.Yield();
        }
        Assert.Fail("Generated controller acquisition exhausted its finite witness attempts.");
    }

    private static void VerifyPackedHistory(ConcurrentBag<PackedEvent> events, byte[] expected, uint[] arena, uint target, uint result)
    {
        uint next = 1;
        foreach (PackedEvent item in events.OrderBy(item => item.Ordinal))
        {
            Assert.AreEqual(next++, item.Ordinal);
            int address = checked((int)(target * 4 + item.ByteOffset));
            ulong original = item.Width == 8 ? BinaryPrimitives.ReadUInt64LittleEndian(expected.AsSpan(address)) : BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(address));
            ulong value = item.Width == 8 ? item.Value : unchecked((uint)item.Value);
            ulong observed = Expected(WarpManagedWideAtomicKernels.Create64()[(int)item.Operation - 1], original, value, value, out ulong updated);
            if (item.Width == 4) { observed = unchecked((uint)observed); updated = unchecked((uint)updated); }
            Assert.AreEqual(observed, item.Observed);
            WriteOracle(expected, address, item.Width, updated);
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan((int)result * 4), unchecked((uint)observed));
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan((int)(result + 1) * 4), (uint)(observed >> 32));
        }
        Assert.AreEqual(97u, next);
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan((int)(result + 2) * 4), 96);
        CollectionAssert.AreEqual(expected, Bytes(arena));
    }

    private sealed record PackedEvent(uint Ordinal, uint Width, uint ByteOffset, uint Operation, ulong Value, ulong Observed);
}
