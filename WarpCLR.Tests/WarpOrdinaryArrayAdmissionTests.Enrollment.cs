using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpOrdinaryArrayAdmissionTests
{
    [TestMethod]
    public void ConflictingEnrollmentDoesNotCaptureAnEarlierUnownedBank()
    {
        uint[] shared = [1], unowned = [2];
        object first = new(), firstAuthority = new(), second = new(), secondAuthority = new();
        WarpOrdinaryArrayOwner a = WarpOrdinaryArrayRegistry.EnrollOwner(first, firstAuthority, [shared]);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(second, secondAuthority, [unowned, shared]));
        WarpOrdinaryArrayOwner b = WarpOrdinaryArrayRegistry.EnrollOwner(second, secondAuthority, [unowned]);
        WarpOrdinaryArrayRegistry.Hold(a, first, firstAuthority);
        WarpOrdinaryArrayRegistry.Hold(b, second, secondAuthority);
        Assert.ThrowsExactly<InvalidOperationException>(() => AcquireRole(shared, 2));
        Assert.ThrowsExactly<InvalidOperationException>(() => AcquireRole(unowned, 0));
    }

    [TestMethod]
    public void ChangedOwnerOrAuthorityCannotMutateAnEnrollment()
    {
        uint[] bank = [7];
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        Assert.AreSame(handle, WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank, bank]));
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Hold(handle, new object(), authority));
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Quarantine(handle, owner, new object()));
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(owner, new object(), [bank]));
        using WarpOrdinaryArrayAdmission ordinary = AcquireRole(bank, 0);
        Assert.AreSame(bank, ordinary.GetInput(0));
    }

    [TestMethod]
    public void FailedAdmissionDoesNotRetainItsEarlierUnownedBank()
    {
        uint[] held = [11], ordinary = [13];
        object first = new(), firstAuthority = new(), second = new(), secondAuthority = new();
        WarpOrdinaryArrayOwner a = WarpOrdinaryArrayRegistry.EnrollOwner(first, firstAuthority, [held]);
        WarpOrdinaryArrayRegistry.Hold(a, first, firstAuthority);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayAdmission.Acquire([ordinary, held], [], [], []));
        WarpOrdinaryArrayOwner b = WarpOrdinaryArrayRegistry.EnrollOwner(second, secondAuthority, [ordinary]);
        WarpOrdinaryArrayRegistry.Hold(b, second, secondAuthority);
        Assert.ThrowsExactly<InvalidOperationException>(() => AcquireRole(ordinary, 1));
    }

    [TestMethod]
    public void InputReferenceSnapshotSurvivesChangesToTheCallerOuterList()
    {
        uint[] first = [19], held = [23];
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [held]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        uint[][] supplied = [first];
        using WarpOrdinaryArrayAdmission ordinary = WarpOrdinaryArrayAdmission.Acquire(supplied, [], [], []);
        supplied[0] = held;
        Assert.AreSame(first, ordinary.GetInput(0));
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayAdmission.Acquire(supplied, [], [], []));
    }

    [TestMethod]
    public void EmptySharedArraysDoNotJoinIndependentOwnerDomains()
    {
        uint[] empty = Array.Empty<uint>();
        object first = new(), firstAuthority = new(), second = new(), secondAuthority = new();
        WarpOrdinaryArrayOwner a = WarpOrdinaryArrayRegistry.EnrollOwner(first, firstAuthority, [empty, empty]);
        WarpOrdinaryArrayOwner b = WarpOrdinaryArrayRegistry.EnrollOwner(second, secondAuthority, [empty]);
        WarpOrdinaryArrayRegistry.Hold(a, first, firstAuthority);
        WarpOrdinaryArrayRegistry.Hold(b, second, secondAuthority);
        WarpOrdinaryArrayRegistry.Quarantine(a, first, firstAuthority);
        using WarpOrdinaryArrayAdmission ordinary = WarpOrdinaryArrayAdmission.Acquire([empty], empty, empty, empty);
        Assert.AreSame(empty, ordinary.GetInput(0));
        WarpOrdinaryArrayRegistry.ReleaseHold(b, second, secondAuthority);
    }

    [TestMethod]
    public void ActiveUnownedAdmissionBlocksEnrollmentUntilItsActualRelease()
    {
        uint[] bank = [29];
        object owner = new(), authority = new();
        using WarpOrdinaryArrayAdmission ordinary = AcquireRole(bank, 1);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]));
        ordinary.Dispose();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        Assert.ThrowsExactly<InvalidOperationException>(() => AcquireRole(bank, 0));
    }
}
