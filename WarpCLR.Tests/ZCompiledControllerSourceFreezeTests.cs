using System.Diagnostics.CodeAnalysis;
using WarpCLR.Compiler;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest discovers and instantiates this internal fixture through reflection.")]
internal sealed class ZCompiledControllerSourceFreezeTests
{
    private const uint Scheduler = 7;
    private const uint OwnerWord = Scheduler + WarpPortableSchedulerLayout.ControllerOwner;

    [TestMethod]
    public void SourceFreezeLeavesWholeBankAndControllerCountersUntouched()
    {
        var fixture = CreateOwned();
        WarpCompiledControllerGrant first = fixture.Controller.TryAcquire()!;
        Assert.IsNotNull(first); Assert.AreEqual(1u, first.Token);
        fixture.Controller.Release(first);
        uint[] before = fixture.Arena.ToArray();
        uint failed = fixture.Controller.FailedClaims;
        object handle = fixture.Controller.FreezeExecutedSource(fixture.Authority);
        Assert.IsNotNull(handle);
        for (int attempt = 0; attempt < 5; attempt++) { Assert.IsNull(fixture.Controller.TryAcquire()); }
        CollectionAssert.AreEqual(before, fixture.Arena);
        Assert.AreEqual(failed, fixture.Controller.FailedClaims);
        fixture.Controller.ResumeAfterExecutedSource(fixture.Authority, handle);
        CollectionAssert.AreEqual(before, fixture.Arena);
        Assert.AreEqual(failed, fixture.Controller.FailedClaims);
        WarpCompiledControllerGrant second = fixture.Controller.TryAcquire()!;
        Assert.IsNotNull(second); Assert.AreEqual(2u, second.Token, "Freeze and suspended claims must not reserve token identities.");
        fixture.Controller.Release(second);
    }

    [TestMethod]
    public void SourceAuthorityRejectionPrecedesCallerAndArenaInspection()
    {
        // An empty bank makes any owner-word inspection fail independently.
        // These are actual controllers, with no fake compiled claim implementation.
        var ordinary = new WarpCompiledController([], Scheduler);
        var authority = new object();
        var owned = new WarpCompiledController([], Scheduler, authority);
        Assert.ThrowsExactly<InvalidOperationException>(() => ordinary.FreezeExecutedSource(null));
        Assert.ThrowsExactly<InvalidOperationException>(() => ordinary.FreezeExecutedSource(authority));
        Assert.ThrowsExactly<InvalidOperationException>(() => ordinary.ResumeAfterExecutedSource(authority, new object()));
        Assert.ThrowsExactly<InvalidOperationException>(() => owned.FreezeExecutedSource(null));
        Assert.ThrowsExactly<InvalidOperationException>(() => owned.FreezeExecutedSource(new object()));
        Assert.ThrowsExactly<InvalidOperationException>(() => owned.ResumeAfterExecutedSource(new object(), new object()));
        Assert.ThrowsExactly<InvalidOperationException>(() => owned.ResumeAfterExecutedSource(authority, null));
        Assert.ThrowsExactly<InvalidOperationException>(() => owned.ResumeAfterExecutedSource(authority, new object()));
        Assert.AreEqual(0u, ordinary.FailedClaims); Assert.AreEqual(0u, owned.FailedClaims);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SourceFreezeRejectsActiveOccupiedAndRemoteOwnership(bool keepCapturedGrant)
    {
        var fixture = CreateOwned();
        WarpCompiledControllerGrant grant = fixture.Controller.TryAcquire()!;
        Assert.IsNotNull(grant);
        uint[] granted = fixture.Arena.ToArray();
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Controller.FreezeExecutedSource(fixture.Authority));
        CollectionAssert.AreEqual(granted, fixture.Arena);
        fixture.Controller.Release(grant);
        fixture.Arena[OwnerWord] = 23;
        uint[] occupied = fixture.Arena.ToArray();
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Controller.FreezeExecutedSource(fixture.Authority));
        CollectionAssert.AreEqual(occupied, fixture.Arena);
        fixture.Arena[OwnerWord] = 0;
        grant = fixture.Controller.TryAcquire()!;
        Assert.AreEqual(2u, grant.Token);
        fixture.Controller.SuspendForRemote(grant, keepCapturedGrant);
        uint[] remote = fixture.Arena.ToArray();
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Controller.FreezeExecutedSource(fixture.Authority));
        CollectionAssert.AreEqual(remote, fixture.Arena);
        fixture.Controller.ResumeAfterRemoteCommit(keepCapturedGrant ? grant : null);
        if (keepCapturedGrant) { fixture.Controller.Release(grant); }
        object handle = fixture.Controller.FreezeExecutedSource(fixture.Authority);
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Controller.FreezeExecutedSource(fixture.Authority));
        fixture.Controller.ResumeAfterExecutedSource(fixture.Authority, handle);
        Assert.AreEqual(0u, fixture.Controller.FailedClaims);
    }

    [TestMethod]
    public void SourceFreezeRequiresItsCurrentIssuerHandleAndCannotUseLegacyResume()
    {
        var fixture = CreateOwned();
        var peer = CreateOwned(fixture.Authority);
        object handle = fixture.Controller.FreezeExecutedSource(fixture.Authority);
        object peerHandle = peer.Controller.FreezeExecutedSource(peer.Authority);
        uint[] before = fixture.Arena.ToArray();
        uint[] peerBefore = peer.Arena.ToArray();
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Controller.ResumeAfterRemoteCommit(null));
        var legacyGrant = new WarpCompiledControllerGrant(fixture.Controller, 1);
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Controller.ResumeAfterRemoteCommit(legacyGrant));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Controller.ResumeAfterExecutedSource(new object(), handle));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Controller.ResumeAfterExecutedSource(fixture.Authority, null));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Controller.ResumeAfterExecutedSource(fixture.Authority, new object()));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Controller.ResumeAfterExecutedSource(fixture.Authority, peerHandle));
        Assert.ThrowsExactly<InvalidOperationException>(() => peer.Controller.ResumeAfterExecutedSource(peer.Authority, handle));
        Assert.IsNull(fixture.Controller.TryAcquire()); Assert.IsNull(peer.Controller.TryAcquire());
        CollectionAssert.AreEqual(before, fixture.Arena); CollectionAssert.AreEqual(peerBefore, peer.Arena);
        fixture.Controller.ResumeAfterExecutedSource(fixture.Authority, handle);
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Controller.ResumeAfterExecutedSource(fixture.Authority, handle));
        object next = fixture.Controller.FreezeExecutedSource(fixture.Authority);
        Assert.AreNotSame(handle, next);
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Controller.ResumeAfterExecutedSource(fixture.Authority, handle));
        Assert.IsNull(fixture.Controller.TryAcquire());
        fixture.Controller.ResumeAfterExecutedSource(fixture.Authority, next);
        peer.Controller.ResumeAfterExecutedSource(peer.Authority, peerHandle);
        CollectionAssert.AreEqual(before, fixture.Arena); CollectionAssert.AreEqual(peerBefore, peer.Arena);
        Assert.AreEqual(0u, fixture.Controller.FailedClaims); Assert.AreEqual(0u, peer.Controller.FailedClaims);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void LegacyRemoteBehaviorIsPreservedWithoutSourceFreeze(bool sourceEnabled, bool keepCapturedGrant)
    {
        uint[] arena = CreateBank();
        WarpCompiledController controller = sourceEnabled ? new(arena, Scheduler, new object()) : new(arena, Scheduler);
        WarpCompiledControllerGrant grant = controller.TryAcquire()!;
        Assert.IsNotNull(grant);
        controller.SuspendForRemote(grant, keepCapturedGrant);
        uint[] before = arena.ToArray();
        Assert.IsNull(controller.TryAcquire()); Assert.AreEqual(0u, controller.FailedClaims);
        controller.ResumeAfterRemoteCommit(keepCapturedGrant ? grant : null);
        CollectionAssert.AreEqual(before, arena);
        if (keepCapturedGrant) { controller.Release(grant); }
        WarpCompiledControllerGrant next = controller.TryAcquire()!;
        Assert.IsNotNull(next); Assert.AreEqual(2u, next.Token);
        controller.Release(next);
    }

    [TestMethod]
    public void SourceResumeRejectsAnOccupiedWordBeforeUnsuspending()
    {
        var fixture = CreateOwned();
        object handle = fixture.Controller.FreezeExecutedSource(fixture.Authority);
        fixture.Arena[OwnerWord] = 29;
        uint[] occupied = fixture.Arena.ToArray();
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Controller.ResumeAfterExecutedSource(fixture.Authority, handle));
        Assert.IsNull(fixture.Controller.TryAcquire());
        CollectionAssert.AreEqual(occupied, fixture.Arena);
        Assert.AreEqual(0u, fixture.Controller.FailedClaims);
        fixture.Arena[OwnerWord] = 0;
        uint[] zeroOwner = fixture.Arena.ToArray();
        fixture.Controller.ResumeAfterExecutedSource(fixture.Authority, handle);
        CollectionAssert.AreEqual(zeroOwner, fixture.Arena);
        WarpCompiledControllerGrant grant = fixture.Controller.TryAcquire()!;
        Assert.IsNotNull(grant); Assert.AreEqual(1u, grant.Token);
        fixture.Controller.Release(grant);
    }

    // These standalone internal controller fixtures prove only the primitive.
    // They never replace a Context's readonly controller or issue a Source grant.
    private static (uint[] Arena, WarpCompiledController Controller, object Authority) CreateOwned(object? authority = null)
    {
        uint[] arena = CreateBank();
        authority ??= new object();
        return (arena, new(arena, Scheduler, authority), authority);
    }

    private static uint[] CreateBank()
    {
        uint[] arena = Enumerable.Range(0, 64).Select(static value => checked(0xA5000000u + (uint)value)).ToArray();
        arena[OwnerWord] = 0;
        return arena;
    }
}
