using System.Runtime.ExceptionServices;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

// These retained ordinary uses only deny ownership changes. They never issue
// a Source checkpoint, nonce, private grant, or release authorization.
internal sealed class WarpNativeOriginalArenaUse
{
    private const int MaximumRetainedUses = 4096;
    private static readonly Lock RetentionGate = new();
    private static readonly HashSet<WarpNativeOriginalArenaUse> Retained = [];
    private static int reservations;
    private readonly uint[] original;
    private readonly WarpOrdinaryArrayAdmission admission;
    private readonly List<ExceptionDispatchInfo> failures = [];
    private bool released;

    private WarpNativeOriginalArenaUse(uint[] original, WarpOrdinaryArrayAdmission admission)
    {
        this.original = original;
        this.admission = admission;
    }

    internal static WarpNativeOriginalArenaUse Acquire(uint[] original)
    {
        ArgumentNullException.ThrowIfNull(original);
        lock (RetentionGate)
        {
            if (reservations == MaximumRetainedUses)
            {
                throw new WarpHostException("WRPNATIVE1005", "The retained native arena-use capacity is exhausted.");
            }
            reservations++;
        }
        WarpOrdinaryArrayAdmission? acquiredAdmission = null;
        try
        {
            acquiredAdmission = WarpOrdinaryArrayAdmission.Acquire([], [], [], original);
            var use = new WarpNativeOriginalArenaUse(original, acquiredAdmission);
            lock (RetentionGate) { Retained.Add(use); }
            return use;
        }
        catch
        {
            acquiredAdmission?.Dispose();
            lock (RetentionGate) { reservations--; }
            throw;
        }
    }

    // Only the arena calls this after no allocation was attempted, or after
    // the actual transport Free returned successfully. A thrown Free never
    // reaches this method, even if the driver happened to free before throwing.
    internal void ReleaseAfterConfirmedRetirement()
    {
        lock (RetentionGate)
        {
            if (released) { return; }
            if (!Retained.Contains(this))
            {
                throw new InvalidOperationException("The exact retained native arena use is absent.");
            }
            admission.Dispose();
            Retained.Remove(this);
            reservations--;
            released = true;
            GC.KeepAlive(original);
        }
    }

    internal void RecordFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        lock (RetentionGate) { failures.Add(ExceptionDispatchInfo.Capture(failure)); }
    }
}
