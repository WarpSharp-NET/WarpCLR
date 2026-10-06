using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using WarpCLR.IR;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest constructs this internal fixture through reflected discovery under DiscoverInternals.")]
internal sealed class ZNativePinAdmissionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HeldOrQuarantinedOriginalCannotEnterNativePin(bool quarantine)
    {
        uint[] bank = [0x80000000, 0x7FC00001, 0xFFFFFFFF];
        uint[] before = (uint[])bank.Clone();
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        if (quarantine) { WarpOrdinaryArrayRegistry.Quarantine(handle, owner, authority); }
        Assert.ThrowsExactly<InvalidOperationException>(() => new WarpPinnedUInt32(bank));
        CollectionAssert.AreEqual(before, bank);
        if (!quarantine) { WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority); }
    }

    [TestMethod]
    public void EveryPinRetainsItsOriginalUseUntilThatActualPinIsDisposed()
    {
        uint[] bank = [0x01234567, 0x89ABCDEF];
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        using var first = new WarpPinnedUInt32(bank);
        using var second = new WarpPinnedUInt32(bank);
        Assert.AreSame(bank, first.Data);
        Assert.AreSame(bank, second.Data);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Hold(handle, owner, authority));
        first.Dispose();
        first.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = first.Pointer);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Hold(handle, owner, authority));
        second.Dispose();
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        Assert.ThrowsExactly<InvalidOperationException>(() => new WarpPinnedUInt32(bank));
        WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
        using var after = new WarpPinnedUInt32(bank);
        Assert.AreSame(bank, after.Data);
    }

    [TestMethod]
    public void NativePinAndOrdinaryInputAliasMustBothRetireBeforeOwnerHold()
    {
        uint[] bank = [0x89ABCDEF, 0x7FA12345];
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        using var ordinary = WarpOrdinaryArrayAdmission.Acquire([bank], [], [], []);
        using var pin = new WarpPinnedUInt32(bank);
        Assert.AreSame(bank, ordinary.GetInput(0));
        Assert.AreSame(bank, pin.Data);
        pin.Dispose();
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Hold(handle, owner, authority));
        ordinary.Dispose();
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        Assert.ThrowsExactly<InvalidOperationException>(() => new WarpPinnedUInt32(bank));
        WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
    }

    [TestMethod]
    public void NativePointerPreservesAllOriginalWordsWithoutNumericConversion()
    {
        uint[] bank = [0, 0x80000000, 0x7FA12345, 0xFFC01234, uint.MaxValue];
        using var pin = new WarpPinnedUInt32(bank);
        for (int index = 0; index < bank.Length; index++)
        {
            Assert.AreEqual(bank[index], unchecked((uint)Marshal.ReadInt32(pin.Pointer, index * sizeof(uint))));
        }
        Marshal.WriteInt32(pin.Pointer, 2 * sizeof(uint), unchecked((int)0x89ABCDEFu));
        Assert.AreEqual(0x89ABCDEFu, bank[2]);
    }

    [TestMethod]
    public void ActiveUnenrolledPinPreventsOwnerEnrollmentUntilItIsActuallyReleased()
    {
        uint[] bank = [19];
        object owner = new(), authority = new();
        using var pin = new WarpPinnedUInt32(bank);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]));
        pin.Dispose();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        Assert.ThrowsExactly<InvalidOperationException>(() => new WarpPinnedUInt32(bank));
        WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
    }

    [TestMethod]
    public void EmptyPinUsesNonNullStorageWithoutJoiningUnrelatedOwnerDomains()
    {
        uint[] empty = Array.Empty<uint>();
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [empty]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        using var pin = new WarpPinnedUInt32(empty);
        Assert.AreNotSame(empty, pin.Data);
        Assert.HasCount(1, pin.Data);
        Assert.AreNotEqual(IntPtr.Zero, pin.Pointer);
        Assert.AreEqual(0u, pin.Data[0]);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(new object(), new object(), [pin.Data]));
        WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
    }
}
