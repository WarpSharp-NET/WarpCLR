using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest constructs this internal fixture through reflected discovery under DiscoverInternals.")]
internal sealed class ZNativeArenaOriginalLifetimeTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HeldOrQuarantinedArenaFailsBeforeNativeAllocation(bool quarantine)
    {
        uint[] bank = [0x80000000, 0x7FA12345, uint.MaxValue];
        uint[] before = (uint[])bank.Clone();
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        if (quarantine) { WarpOrdinaryArrayRegistry.Quarantine(handle, owner, authority); }
        using var transport = new Transport();
        Assert.ThrowsExactly<InvalidOperationException>(() => transport.CreateArena(bank));
        Assert.AreEqual(0, transport.AllocationAttempts);
        Assert.AreEqual(0, transport.Uploads);
        CollectionAssert.AreEqual(before, bank);
        if (!quarantine) { WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority); }
    }

    [TestMethod]
    public void OriginalBankRemainsAdmittedUntilEveryActualArenaLeaseDrains()
    {
        uint[] bank = [0, 0x80000000, 0x7FA12345, 0xFFC01234, uint.MaxValue];
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        using var transport = new Transport();
        var arena = transport.CreateArena(bank);
        WarpNativeManagedArena.Lease first = arena.Acquire(Target(), transport.Operations);
        WarpNativeManagedArena.Lease second = arena.Acquire(Target(), transport.Operations);
        CollectionAssert.AreEqual(bank, arena.Snapshot());
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Hold(handle, owner, authority));
        arena.Dispose();
        first.Dispose();
        Assert.AreEqual(0, transport.FreeAttempts);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Hold(handle, owner, authority));
        second.Dispose();
        Assert.AreEqual(1, transport.FreeAttempts);
        Assert.AreEqual(1, transport.Released);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
        arena.Dispose();
        second.Dispose();
        Assert.AreEqual(1, transport.FreeAttempts);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FailedFreeRetainsOriginalBankAndNeverRetriesUnknownRetirement(bool freesBeforeThrowing)
    {
        uint[] bank = [0x89ABCDEF];
        using var transport = new Transport { FreesBeforeThrowing = freesBeforeThrowing };
        var arena = transport.CreateArena(bank);
        var failure = new WarpHostException("WRPNATIVE1007", "Exact failed retirement.");
        transport.FreeFailure = failure;
        Assert.AreSame(failure, Assert.ThrowsExactly<WarpHostException>(arena.Dispose));
        Assert.IsTrue(transport.Quarantined);
        Assert.AreEqual(1, transport.FreeAttempts);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(new object(), new object(), [bank]));
        arena.Dispose();
        arena.CloseContext();
        Assert.AreEqual(1, transport.FreeAttempts);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UploadFailurePreservesOriginalExceptionAndReleasesOnlyAfterSuccessfulFree(bool freeFails)
    {
        uint[] bank = [0x01234567, 0x7FA12345];
        var original = new InvalidOperationException("Exact upload failure.");
        using var transport = new Transport { UploadFailure = original };
        if (freeFails) { transport.FreeFailure = new WarpHostException("WRPNATIVE1007", "Exact cleanup failure."); }
        Assert.AreSame(original, Assert.ThrowsExactly<InvalidOperationException>(() => transport.CreateArena(bank)));
        Assert.AreEqual(1, transport.FreeAttempts);
        if (freeFails)
        {
            Assert.IsTrue(transport.Quarantined);
            Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(new object(), new object(), [bank]));
        }
        else
        {
            object owner = new(), authority = new();
            WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
            WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
            WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
            Assert.IsEmpty(transport.Blocks);
        }
    }

    [TestMethod]
    public void AllocationExceptionRetainsUnknownUseAndPreservesTheExactFailure()
    {
        uint[] bank = [17];
        var original = new InvalidOperationException("No successful allocator receipt.");
        using var transport = new Transport { AllocationFailure = original };
        Assert.AreSame(original, Assert.ThrowsExactly<InvalidOperationException>(() => transport.CreateArena(bank)));
        Assert.AreEqual(1, transport.AllocationAttempts);
        Assert.AreEqual(0, transport.FreeAttempts);
        Assert.IsTrue(transport.Quarantined);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(new object(), new object(), [bank]));
    }

    [TestMethod]
    public void PreallocationAdmissionFailureDoesNotRetainTheOriginalBank()
    {
        uint[] bank = [17, 19];
        using var transport = new Transport();
        Assert.AreEqual("WRPNATIVE1005", Assert.ThrowsExactly<WarpHostException>(() => transport.CreateArena(bank, memory: 4)).Code, StringComparer.Ordinal);
        Assert.AreEqual(0, transport.AllocationAttempts);
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
    }

    [TestMethod]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "This fixture intentionally observes the first Dispose failure and verifies exact exception identity and bank ownership. Its one explicit Dispose call is the behavior under test; a using or finally retry would change the retirement attempt being proved.")]
    public void ReleaseCallbackCannotMaskPhysicalRetirementFailure()
    {
        uint[] bank = [23];
        using var transport = new Transport();
        var arena = transport.CreateArena(bank);
        var original = new WarpHostException("WRPNATIVE1007", "Exact failed free.");
        transport.FreeFailure = original;
        transport.ReleaseFailure = new InvalidOperationException("Secondary release callback failure.");
        WarpHostException? observed = null;
        try { arena.Dispose(); }
        catch (WarpHostException error) { observed = error; }
        Assert.AreSame(original, observed);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(new object(), new object(), [bank]));
    }

    [TestMethod]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "This fixture intentionally observes the first Dispose failure and verifies exact exception identity and bank ownership. Its one explicit Dispose call is the behavior under test; a using or finally retry would change the retirement attempt being proved.")]
    public void ReleaseCallbackFailureAfterPositiveFreeDoesNotRetainOriginalBank()
    {
        uint[] bank = [29];
        using var transport = new Transport();
        var arena = transport.CreateArena(bank);
        var original = new InvalidOperationException("Release callback failure after confirmed free.");
        transport.ReleaseFailure = original;
        InvalidOperationException? observed = null;
        try { arena.Dispose(); }
        catch (InvalidOperationException error) { observed = error; }
        Assert.AreSame(original, observed);
        Assert.IsEmpty(transport.Blocks);
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
    }

    [TestMethod]
    public void FailedConstructorOriginalSurvivesFullGarbageCollection()
    {
        WeakReference<uint[]> reference = FailConstructionWithoutCallerBank();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.IsTrue(reference.TryGetTarget(out uint[]? bank));
        Assert.IsNotNull(bank);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(new object(), new object(), [bank]));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<uint[]> FailConstructionWithoutCallerBank()
    {
        uint[] bank = [31];
        using var transport = new Transport { AllocationFailure = new InvalidOperationException("Unknown allocation outcome.") };
        var reference = new WeakReference<uint[]>(bank);
        Assert.ThrowsExactly<InvalidOperationException>(() => transport.CreateArena(bank));
        return reference;
    }

    private static WarpNativeTarget Target(ulong memory = 1UL << 32) =>
        new(WarpBackendKind.NVPTX, "sm_80", "original-arena-transport-fixture", "no-device-execution", 256, int.MaxValue, memory, 4096);

    // CPU memory transport only: these tests exercise ownership and cleanup,
    // and neither load a device driver nor execute backend machine code.
    private sealed class Transport : IDisposable
    {
        internal Transport() => Operations = new(Allocate, Upload, Readback, static (_, _, _) => { }, static () => { },
            Free, () => Quarantined = true, this);

        internal WarpMachineMemoryOperations Operations { get; }
        internal Dictionary<ulong, WarpNativeBlock> Blocks { get; } = [];
        internal Exception? AllocationFailure { get; init; }
        internal Exception? UploadFailure { get; init; }
        internal Exception? FreeFailure { get; set; }
        internal Exception? ReleaseFailure { get; set; }
        internal bool FreesBeforeThrowing { get; init; }
        internal int AllocationAttempts { get; private set; }
        internal int FreeAttempts { get; private set; }
        internal int Uploads { get; private set; }
        internal int Released { get; private set; }
        internal bool Quarantined { get; private set; }

        internal WarpNativeManagedArena CreateArena(uint[] bank, ulong memory = 1UL << 32) =>
            new(Target(memory), Operations, bank, static action => action(), static action => action(), _ =>
            {
                Released++;
                if (ReleaseFailure is not null) { throw ReleaseFailure; }
            });

        private ulong Allocate(nuint bytes)
        {
            AllocationAttempts++;
            if (AllocationFailure is not null) { throw AllocationFailure; }
            var block = new WarpNativeBlock(checked((int)bytes));
            ulong pointer = unchecked((ulong)block.Pointer.ToInt64());
            Blocks.Add(pointer, block);
            return pointer;
        }

        private void Upload(ulong destination, IntPtr source, nuint bytes)
        {
            Uploads++;
            if (UploadFailure is not null) { throw UploadFailure; }
            Copy(new IntPtr(unchecked((long)destination)), source, bytes);
        }

        private static void Readback(IntPtr destination, ulong source, nuint bytes) =>
            Copy(destination, new IntPtr(unchecked((long)source)), bytes);

        private static void Copy(IntPtr destination, IntPtr source, nuint bytes)
        {
            byte[] content = new byte[checked((int)bytes)];
            Marshal.Copy(source, content, 0, content.Length);
            Marshal.Copy(content, 0, destination, content.Length);
        }

        private void Free(ulong pointer)
        {
            FreeAttempts++;
            if (FreeFailure is not null && !FreesBeforeThrowing) { throw FreeFailure; }
            Blocks[pointer].Dispose();
            Blocks.Remove(pointer);
            if (FreeFailure is not null) { throw FreeFailure; }
        }

        public void Dispose()
        {
            foreach (WarpNativeBlock block in Blocks.Values) { block.Dispose(); }
            Blocks.Clear();
        }
    }
}
