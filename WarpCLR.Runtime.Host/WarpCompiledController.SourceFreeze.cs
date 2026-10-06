namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledController
{
    internal const string SourceFreezeSemantics =
        "warp.compiled-controller/source-freeze-exact-owner-and-current-handle-no-bank-effects/0.1";

    private readonly object? sourceOwnerAuthority;
    private object? sourceFreeze;

    internal WarpCompiledController(uint[] arena, uint scheduler, object sourceOwnerAuthority)
        : this(arena, scheduler)
    {
        ArgumentNullException.ThrowIfNull(sourceOwnerAuthority);
        this.sourceOwnerAuthority = sourceOwnerAuthority;
    }

    // The Context caller holds executionIdentityGate. Complete executed Source
    // checkpoint and census validation remain with it; this freezes only the controller.
    internal object FreezeExecutedSource(object? authority)
    {
        lock (gate)
        {
            RequireSourceOwnerAuthority(authority);
            if (suspended || sourceFreeze is not null || active is not null || arena[word] != 0)
            { throw new InvalidOperationException("A Source freeze requires an unsuspended controller with no grant or owner word."); }
            object handle = new();
            sourceFreeze = handle;
            suspended = true;
            return handle;
        }
    }

    internal void ResumeAfterExecutedSource(object? authority, object? handle)
    {
        lock (gate)
        {
            RequireSourceOwnerAuthority(authority);
            if (sourceFreeze is null || !ReferenceEquals(sourceFreeze, handle))
            { throw new InvalidOperationException("Only the current exact issued Source freeze handle may resume its controller."); }
            if (!suspended || active is not null || arena[word] != 0)
            { throw new InvalidOperationException("The frozen Source controller acquired an unexpected grant or owner word."); }
            sourceFreeze = null;
            suspended = false;
        }
    }

    private void RequireSourceOwnerAuthority(object? authority)
    {
        if (sourceOwnerAuthority is null || !ReferenceEquals(sourceOwnerAuthority, authority))
        { throw new InvalidOperationException("Only the exact Context owner authority may freeze or resume its Source controller."); }
    }
}
