using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCompiledController
{
    private readonly WarpCompiledWordService compareExchange;
    private readonly uint[] arena;
    private readonly uint word;
    private readonly Lock gate = new();
    private bool suspended;
    private WarpCompiledControllerGrant? active;
    private uint nextToken;
    private uint failedClaims;

    internal WarpCompiledController(uint[] arena, uint scheduler)
    {
        this.arena = arena;
        word = checked(scheduler + WarpPortableSchedulerLayout.ControllerOwner);
        IReadOnlyList<WarpLogicalMachineLayout> operations = WarpManagedAtomicKernels.Create32();
        compareExchange = new(operations[2]);
        compareExchange.Prepare();
    }

    internal uint FailedClaims => Volatile.Read(ref failedClaims);
    internal string CleanupKernelHash => compareExchange.KernelHash;

    internal WarpCompiledControllerGrant? TryAcquire()
    {
        lock (gate) { return suspended ? null : TryAcquireCore(); }
    }

    private WarpCompiledControllerGrant? TryAcquireCore()
    {
        uint token = ReserveToken();
        if (token == 0) { return null; }
        WarpCompiledServiceContinuation claim = compareExchange.Start([word, 0, token]);
        if (!claim.Resume(arena, compareExchange.Layout.MaximumBlockCost))
        {
            throw new InvalidOperationException("A single atomic controller claim must complete in one admitted quantum.");
        }
        if (claim.Result != 0)
        {
            Interlocked.Increment(ref failedClaims);
            return null;
        }
        var grant = new WarpCompiledControllerGrant(this, token);
        active = grant;
        return grant;
    }

    internal uint ReserveToken()
    {
        uint current = Volatile.Read(ref nextToken);
        if (current == uint.MaxValue) { throw new InvalidOperationException("Controller grant identities cannot wrap."); }
        uint token = checked(current + 1);
        return Interlocked.CompareExchange(ref nextToken, token, current) == current ? token : 0;
    }

    internal void AcknowledgeRemoteRelease(WarpCompiledControllerGrant grant)
    {
        lock (gate)
        {
            if (!suspended || !ReferenceEquals(grant.Controller, this) || !ReferenceEquals(active, grant) || grant.Released || arena[word] != 0)
            { throw new InvalidOperationException("Only the exact captured grant may acknowledge its generated remote release."); }
            grant.Released = true;
            active = null;
        }
    }

    internal void SuspendForRemote(WarpCompiledControllerGrant preparation, bool keepCapturedGrant)
    {
        lock (gate)
        {
            Validate(preparation);
            if (suspended) { throw new InvalidOperationException("The controller already has a remote ownership domain."); }
            if (!keepCapturedGrant) { ReleaseCore(preparation); }
            suspended = true;
        }
    }

    internal void ResumeAfterRemoteCommit(WarpCompiledControllerGrant? capturedGrant)
    {
        lock (gate)
        {
            if (!suspended || capturedGrant is null && (active is not null || arena[word] != 0))
            { throw new InvalidOperationException("The exact remote ownership domain changed its controller grant."); }
            if (capturedGrant is not null) { Validate(capturedGrant); }
            suspended = false;
        }
    }

    internal void Validate(WarpCompiledControllerGrant grant)
    {
        if (!ReferenceEquals(grant.Controller, this) || !ReferenceEquals(active, grant) ||
            arena[word] != grant.Token || grant.Released)
        {
            throw new InvalidOperationException("The controller grant is stale or belongs to another context.");
        }
    }

    internal void Release(WarpCompiledControllerGrant grant)
    {
        lock (gate)
        {
            if (suspended) { throw new InvalidOperationException("An owned remote command must commit or authenticate cleanup before grant release."); }
            ReleaseCore(grant);
        }
    }

    private void ReleaseCore(WarpCompiledControllerGrant grant)
    {
        Validate(grant);
        grant.Released = true;
        active = null;
        WarpCompiledServiceContinuation release = compareExchange.Start([word, grant.Token, 0]);
        if (!release.Resume(arena, compareExchange.Layout.MaximumBlockCost) || release.Result != grant.Token)
        {
            throw new InvalidOperationException("A controller release must compare-exchange its exact owner in one admitted quantum.");
        }
    }
}
