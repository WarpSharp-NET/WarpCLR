using System.Diagnostics.CodeAnalysis;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest constructs this internal fixture through reflected discovery under DiscoverInternals.")]
internal sealed partial class WarpOrdinaryArrayAdmissionTests
{
    [TestMethod]
    public void UnissuedConstructorsRejectBeforeInspectingTheirArguments()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new WarpOrdinaryArrayOwner(new object(), null!, null!));
        Assert.ThrowsExactly<InvalidOperationException>(() => new WarpOrdinaryArrayAdmission(new object(), null!, null!));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void HeldBankIsRejectedInEveryOrdinaryRoleWithoutWordChanges(int role)
    {
        uint[] bank = [0x80000000, 0x7FC00001, 0xFFFFFFFF, 1];
        uint[] before = (uint[])bank.Clone();
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        Assert.ThrowsExactly<InvalidOperationException>(() => AcquireRole(bank, role));
        CollectionAssert.AreEqual(before, bank);
        WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
        using WarpOrdinaryArrayAdmission ordinary = AcquireRole(bank, role);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void TerminalQuarantineRejectsEveryRoleAndCannotBeReleasedOrExtended(int role)
    {
        uint[] bank = [17];
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        WarpOrdinaryArrayRegistry.Quarantine(handle, owner, authority);
        WarpOrdinaryArrayRegistry.Quarantine(handle, owner, authority);
        Assert.ThrowsExactly<InvalidOperationException>(() => AcquireRole(bank, role));
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority));
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Hold(handle, owner, authority));
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [[31]]));
        Assert.AreEqual(17u, bank[0]);
    }

    [TestMethod]
    public void OverlappingRolesAndReadersRetainExactlyTheirLiveUsesThroughIdempotentDisposal()
    {
        uint[] first = [3], second = [5];
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [first, second]);
        using WarpOrdinaryArrayAdmission a = WarpOrdinaryArrayAdmission.Acquire([first, second, first], first, second, first);
        using WarpOrdinaryArrayAdmission b = AcquireRole(first, 0);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Hold(handle, owner, authority));
        a.Dispose();
        a.Dispose();
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Hold(handle, owner, authority));
        b.Dispose();
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        Assert.ThrowsExactly<InvalidOperationException>(() => AcquireRole(second, 1));
        WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
        using WarpOrdinaryArrayAdmission after = AcquireRole(second, 2);
    }

    [TestMethod]
    public void QuarantineImmediatelyDeniesNewUsesWhileAnExistingReaderRemainsLive()
    {
        uint[] bank = [0xDEADBEEF];
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        using WarpOrdinaryArrayAdmission ordinary = AcquireRole(bank, 0);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Hold(handle, owner, authority));
        WarpOrdinaryArrayRegistry.Quarantine(handle, owner, authority);
        Assert.AreSame(bank, ordinary.GetInput(0));
        Assert.ThrowsExactly<InvalidOperationException>(() => AcquireRole(bank, 3));
        ordinary.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => ordinary.GetInput(0));
        Assert.ThrowsExactly<InvalidOperationException>(() => AcquireRole(bank, 0));
        Assert.AreEqual(0xDEADBEEFu, bank[0]);
    }

    private static WarpOrdinaryArrayAdmission AcquireRole(uint[] bank, int role) => role switch
    {
        0 => WarpOrdinaryArrayAdmission.Acquire([bank], [], [], []),
        1 => WarpOrdinaryArrayAdmission.Acquire([], bank, [], []),
        2 => WarpOrdinaryArrayAdmission.Acquire([], [], bank, []),
        3 => WarpOrdinaryArrayAdmission.Acquire([], [], [], bank),
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
