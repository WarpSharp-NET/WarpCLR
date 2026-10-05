using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests.Architecture;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this internal fixture through reflection.")]
internal sealed class NativeManagedArenaTests
{
    [TestMethod]
    public void ArenaTransportPreservesEveryWordAndOwnsBothUploadAndSnapshot()
    {
        using var transport = new Transport();
        uint[] original = [0, 0x80000000, 0x7FC00001, 0x7FA12345, 0xFFFFFFFF, 0x01234567, 0x89ABCDEF];
        uint[] expected = (uint[])original.Clone();
        using var arena = transport.CreateArena(Target(), original);
        original[0] = 9;
        uint[] snapshot = arena.Snapshot();
        CollectionAssert.AreEqual(expected, snapshot);
        snapshot[1] = 42;
        CollectionAssert.AreEqual(expected, arena.Snapshot());
        Assert.HasCount(1, transport.Uploads);
    }

    [TestMethod]
    public void DifferentProgramsAndBatchResetsKeepTheSameArenaAllocation()
    {
        using var transport = new Transport();
        using var arena = transport.CreateArena(Target(), [0x80000000, 0x7FA12345, 1]);
        ulong arenaPointer = transport.Uploads[0];
        WarpLogicalMachineLayout first = WarpManagedMemoryKernels.CreateWriteReadCount();
        WarpLogicalMachineLayout second = WarpManagedAtomicKernels.Create32()[3];
        transport.OnLaunch = arguments =>
        {
            Assert.AreEqual(arenaPointer, ReadPointer(arguments, 7));
            Assert.AreEqual(3u, ReadWord(arguments, 8));
            IntPtr memory = new(unchecked((long)arenaPointer));
            Marshal.WriteInt32(memory, 2 * sizeof(uint), Marshal.ReadInt32(memory, 2 * sizeof(uint)) + 1);
            ConsumeBudget(arguments);
        };
        using (var execution = Execute(first, transport, arena))
        {
            _ = execution.Resume(first.MaximumBlockCost);
            execution.ResetBatch(first.CreateInitialState(4, 100), 1, 0);
            _ = execution.Resume(first.MaximumBlockCost);
        }
        using (var execution = Execute(second, transport, arena)) { _ = execution.Resume(second.MaximumBlockCost); }
        CollectionAssert.AreEqual(new uint[] { 0x80000000, 0x7FA12345, 4 }, arena.Snapshot());
        Assert.AreEqual(1, transport.Uploads.Count(pointer => pointer == arenaPointer));
        Assert.HasCount(1, transport.Allocations);
    }

    [TestMethod]
    public void ArenaDisposalRejectsNewOwnersAndWaitsForEveryExistingLease()
    {
        using var transport = new Transport();
        var arena = transport.CreateArena(Target(), [1, 2]);
        WarpNativeManagedArena.Lease first = arena.Acquire(Target(), transport.Operations);
        WarpNativeManagedArena.Lease second = arena.Acquire(Target(), transport.Operations);
        arena.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => arena.Acquire(Target(), transport.Operations));
        Assert.ThrowsExactly<ObjectDisposedException>(() => arena.Snapshot());
        first.EnsureUsable();
        first.Dispose();
        first.Dispose();
        Assert.HasCount(1, transport.Allocations);
        second.EnsureUsable();
        second.Dispose();
        Assert.HasCount(0, transport.Allocations);
        Assert.AreEqual(1, transport.ReleasedArenas);
        int cleanupCalls = transport.CleanupContexts;
        arena.Dispose();
        Assert.AreEqual(cleanupCalls, transport.CleanupContexts);
    }

    [TestMethod]
    public void ForeignArenaContextFailsBeforeExecutionAllocatesAnything()
    {
        using var owner = new Transport();
        using var other = new Transport();
        using var arena = owner.CreateArena(Target(), [1]);
        WarpLogicalMachineLayout layout = WarpManagedMemoryKernels.CreateWriteReadCount();
        WarpHostException failure = Assert.ThrowsExactly<WarpHostException>(() => Execute(layout, other, arena));
        Assert.AreEqual("WRPNATIVE1004", failure.Code, StringComparer.Ordinal);
        Assert.HasCount(0, other.Allocations);
        WarpNativeTarget different = Target(device: "other-device");
        Assert.ThrowsExactly<WarpHostException>(() => arena.Acquire(different, owner.Operations));
        Assert.HasCount(1, owner.Allocations);
    }

    [TestMethod]
    public void ClosingTheOwningContextRevokesOutstandingLeasesBeforeDriverUnload()
    {
        using var transport = new Transport();
        var arena = transport.CreateArena(Target(), [1, 2]);
        WarpNativeManagedArena.Lease first = arena.Acquire(Target(), transport.Operations);
        WarpNativeManagedArena.Lease second = arena.Acquire(Target(), transport.Operations);
        arena.CloseContext();
        Assert.IsEmpty(transport.Allocations);
        Assert.AreEqual(1, transport.ReleasedArenas);
        Assert.ThrowsExactly<ObjectDisposedException>(first.EnsureUsable);
        Assert.ThrowsExactly<ObjectDisposedException>(second.EnsureUsable);
        int cleanupContexts = transport.CleanupContexts;
        first.Dispose();
        second.Dispose();
        arena.Dispose();
        arena.CloseContext();
        Assert.AreEqual(cleanupContexts, transport.CleanupContexts);
    }

    [TestMethod]
    public void ArenaAbiAndTotalMemoryAreAdmittedBeforeAllocatingWorkerState()
    {
        using var transport = new Transport();
        WarpLogicalMachineLayout layout = WarpManagedMemoryKernels.CreateWriteReadCount();
        uint[] state = layout.CreateInitialState(4, 100);
        ulong required = checked((ulong)(state.Length + 8 + 2) * sizeof(uint));
        WarpNativeTarget small = Target(memory: required - 1);
        using var arena = transport.CreateArena(small, new uint[8]);
        WarpNativeImage image = Image(layout, small);
        Assert.AreEqual("WRPNATIVE1008", Assert.ThrowsExactly<WarpHostException>(() =>
            new WarpNativeMachineExecution(image, state, [[0], [0]], [], 1, 0, 4, transport.Operations)).Code, StringComparer.Ordinal);
        Assert.AreEqual("WRPNATIVE1005", Assert.ThrowsExactly<WarpHostException>(() =>
            new WarpNativeMachineExecution(image, state, [[0], [0]], [], 1, 0, 4, transport.Operations, arena)).Code, StringComparer.Ordinal);
        Assert.HasCount(1, transport.Allocations);
        WarpLogicalMachineLayout unbound = WarpManagedSourceHookKernels.CreateBoundaries();
        Assert.AreEqual("WRPNATIVE1008", Assert.ThrowsExactly<WarpHostException>(() =>
            new WarpNativeMachineExecution(Image(unbound), unbound.CreateInitialState(1, 100), [[0]], [], 1, 0, 1,
                transport.Operations, arena)).Code, StringComparer.Ordinal);
    }

    [TestMethod]
    public void ManagedArgumentsPreserveFullPointerWidthAfterOddScalarWords()
    {
        const ulong arenaPointer = 0xFEDCBA9876543210;
        using var arguments = new WarpNativeKernelArguments(0x123456789ABCDEF0, [0x89ABCDEF01234567],
            [0x80000001], 3, 2, 4, 100, arenaPointer, 0x80000003, managedArena: true);
        Assert.AreEqual(0x123456789ABCDEF0UL, ReadPointer(arguments.Pointer, 0));
        Assert.AreEqual(0x89ABCDEF01234567UL, ReadPointer(arguments.Pointer, 1));
        Assert.AreEqual(0x80000001u, ReadWord(arguments.Pointer, 2));
        Assert.AreEqual(arenaPointer, ReadPointer(arguments.Pointer, 7));
        Assert.AreEqual(0x80000003u, ReadWord(arguments.Pointer, 8));
        Assert.AreEqual(56UL, WarpNativeArgumentLayout.GetPackedBytes(1, 1, machine: true, managedArena: true));
        Assert.ThrowsExactly<WarpHostException>(() => WarpNativeArgumentLayout.Validate(Target(arguments: 55), 1, 1,
            machine: true, managedArena: true));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpNativeKernelArguments(1, [], [], 0, 0, 1, 1,
            arenaPointer, 1));
    }

    [TestMethod]
    public void AStartedNativeFailureQuarantinesEveryExecutionSharingTheArena()
    {
        using var transport = new Transport();
        using var arena = transport.CreateArena(Target(), [1]);
        WarpLogicalMachineLayout layout = WarpManagedMemoryKernels.CreateWriteReadCount();
        using var first = Execute(layout, transport, arena);
        using var second = Execute(layout, transport, arena);
        transport.OnSynchronize = () => throw new WarpHostException("WRPNATIVE1007", "Transport failure.");
        Assert.ThrowsExactly<WarpHostException>(() => first.Resume(layout.MaximumBlockCost));
        Assert.IsTrue(transport.Quarantined);
        Assert.AreEqual("WRPNATIVE1006", Assert.ThrowsExactly<WarpHostException>(() =>
            second.Resume(layout.MaximumBlockCost)).Code, StringComparer.Ordinal);
        Assert.ThrowsExactly<WarpHostException>(() => arena.Snapshot());
        Assert.AreEqual(1, transport.Launches);
    }

    [TestMethod]
    public void SnapshotAndResetTransferFailuresQuarantineTheSharedArena()
    {
        foreach (bool snapshot in new[] { false, true })
        {
            using var transport = new Transport();
            using var arena = transport.CreateArena(Target(), [1]);
            WarpLogicalMachineLayout layout = WarpManagedMemoryKernels.CreateWriteReadCount();
            using var execution = Execute(layout, transport, arena);
            if (snapshot)
            {
                transport.OnReadback = () => throw new WarpHostException("WRPNATIVE1007", "Readback failure.");
                Assert.ThrowsExactly<WarpHostException>(() => arena.Snapshot());
            }
            else
            {
                transport.OnUpload = () => throw new WarpHostException("WRPNATIVE1007", "Reset failure.");
                Assert.ThrowsExactly<WarpHostException>(() => execution.ResetBatch(layout.CreateInitialState(4, 100), 1, 0));
            }
            Assert.ThrowsExactly<WarpHostException>(() => execution.Resume(layout.MaximumBlockCost));
            Assert.ThrowsExactly<WarpHostException>(() => arena.Acquire(Target(), transport.Operations));
            Assert.IsTrue(transport.Quarantined);
            Assert.AreEqual(0, transport.Launches);
        }
    }

    [TestMethod]
    public void CleanupFailureStillDrainsAllWorkerAllocationsAndTheArenaLease()
    {
        using var transport = new Transport();
        var arena = transport.CreateArena(Target(), [1, 2]);
        WarpLogicalMachineLayout layout = WarpManagedMemoryKernels.CreateWriteReadCount();
        var execution = Execute(layout, transport, arena);
        arena.Dispose();
        transport.FailFirstFree = true;
        WarpHostException failure = Assert.ThrowsExactly<WarpHostException>(execution.Dispose);
        Assert.AreEqual("WRPNATIVE1007", failure.Code, StringComparer.Ordinal);
        Assert.HasCount(0, transport.Allocations);
        Assert.AreEqual(1, transport.ReleasedArenas);
        Assert.IsTrue(transport.Quarantined);
        execution.Dispose();
        arena.Dispose();
    }

    [TestMethod]
    public void CompletedLaunchCancellationDrainsExecutionWithoutReuploadingTheArena()
    {
        using var transport = new Transport();
        using var arena = transport.CreateArena(Target(), [0x7FA12345]);
        WarpLogicalMachineLayout layout = WarpManagedMemoryKernels.CreateWriteReadCount();
        using var cancel = new CancellationTokenSource();
        transport.OnLaunch = ConsumeBudget;
        transport.OnSynchronize = cancel.Cancel;
        using (var execution = Execute(layout, transport, arena))
        {
            Assert.ThrowsExactly<OperationCanceledException>(() => execution.Resume(layout.MaximumBlockCost, cancel.Token));
            Assert.ThrowsExactly<OperationCanceledException>(() => execution.Resume(layout.MaximumBlockCost));
        }
        CollectionAssert.AreEqual(new uint[] { 0x7FA12345 }, arena.Snapshot());
        Assert.HasCount(1, transport.Allocations);
        Assert.IsFalse(transport.Quarantined);
    }

    private static WarpNativeMachineExecution Execute(WarpLogicalMachineLayout layout, Transport transport,
        WarpNativeManagedArena arena) => new(Image(layout), layout.CreateInitialState(4, 100), [[0], [0]], [], 1, 0, 4,
            transport.Operations, arena);

    private static WarpNativeImage Image(WarpLogicalMachineLayout layout, WarpNativeTarget? target = null) =>
        new(target ?? Target(), WarpNativeImageFormat.Ptx, WarpPortableMachineEmitter.EntryPoint, [1], layout.Kernel.Name,
            "orchestration-fixture", layout.Kernel.InputBufferCount, layout.Kernel.ScalarArgumentCount, layout);

    private static WarpNativeTarget Target(ulong memory = 1UL << 32, string device = "transport-fixture", uint arguments = 4096) =>
        new(WarpBackendKind.NVPTX, "sm_80", device, "no-device-execution", 256, int.MaxValue, memory, arguments);

    private static ulong ReadPointer(IntPtr arguments, int index) => unchecked((ulong)Marshal.ReadInt64(Marshal.ReadIntPtr(arguments, index * IntPtr.Size)));
    private static uint ReadWord(IntPtr arguments, int index) => unchecked((uint)Marshal.ReadInt32(Marshal.ReadIntPtr(arguments, index * IntPtr.Size)));

    private static void ConsumeBudget(IntPtr arguments)
    {
        IntPtr state = new(unchecked((long)ReadPointer(arguments, 0)));
        int offset = WarpLogicalMachineLayout.RemainingStepsLowOffset * sizeof(uint);
        Marshal.WriteInt64(state, offset, Marshal.ReadInt64(state, offset) - 1);
    }

    // This transport fixture proves ownership and orchestration. It does not execute a GPU or prove backend semantics.
    private sealed class Transport : IDisposable
    {
        public Transport() => Operations = new(Allocate, Upload, Readback,
            (arguments, _, _) => { Launches++; OnLaunch?.Invoke(arguments); },
            () => OnSynchronize?.Invoke(), Free, () => Quarantined = true, this);

        public WarpMachineMemoryOperations Operations { get; }
        public Dictionary<ulong, WarpNativeBlock> Allocations { get; } = [];
        public List<ulong> Uploads { get; } = [];
        public Action<IntPtr>? OnLaunch { get; set; }
        public Action? OnUpload { get; set; }
        public Action? OnReadback { get; set; }
        public Action? OnSynchronize { get; set; }
        public bool FailFirstFree { get; set; }
        public bool Quarantined { get; private set; }
        public int Launches { get; private set; }
        public int ReleasedArenas { get; private set; }
        public int CleanupContexts { get; private set; }

        public WarpNativeManagedArena CreateArena(WarpNativeTarget target, uint[] words) => new(target, Operations, words,
            action => action(), action => { CleanupContexts++; action(); }, _ => ReleasedArenas++);

        private ulong Allocate(nuint bytes)
        {
            var block = new WarpNativeBlock(checked((int)bytes));
            ulong pointer = unchecked((ulong)block.Pointer.ToInt64());
            Allocations.Add(pointer, block);
            return pointer;
        }

        private void Upload(ulong destination, IntPtr source, nuint bytes)
        {
            OnUpload?.Invoke();
            Uploads.Add(destination);
            Copy(new IntPtr(unchecked((long)destination)), source, bytes);
        }

        private void Readback(IntPtr destination, ulong source, nuint bytes)
        {
            OnReadback?.Invoke();
            Copy(destination, new IntPtr(unchecked((long)source)), bytes);
        }

        private static void Copy(IntPtr destination, IntPtr source, nuint bytes)
        {
            byte[] data = new byte[checked((int)bytes)];
            Marshal.Copy(source, data, 0, data.Length);
            Marshal.Copy(data, 0, destination, data.Length);
        }

        private void Free(ulong pointer)
        {
            Allocations[pointer].Dispose();
            Allocations.Remove(pointer);
            if (FailFirstFree)
            {
                FailFirstFree = false;
                throw new WarpHostException("WRPNATIVE1007", "Cleanup transport failure.");
            }
        }

        public void Dispose()
        {
            foreach (WarpNativeBlock allocation in Allocations.Values) { allocation.Dispose(); }
            Allocations.Clear();
        }
    }
}
