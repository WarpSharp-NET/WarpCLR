using System.Runtime.CompilerServices;

namespace WarpCLR.Runtime.Host;

internal static partial class WarpCoreCLRWordTransaction
{
    private static readonly ConditionalWeakTable<uint[], Gate> Gates = new();
    private static readonly Lock IdentitySync = new();
    private static long nextId;

    internal static Task<Lease> AcquireAsync(uint[] state, uint[] arena, CancellationToken cancellationToken) =>
        AcquireCoreAsync(state, arena, allowQuarantinedArena: false, cancellationToken);

    internal static Task<Lease> AcquireQuarantinedAsync(uint[] state, uint[] arena, WarpCoreCLRQuarantineRecovery recovery,
        CancellationToken cancellationToken)
    {
        recovery.ValidateArena(arena);
        return AcquireCoreAsync(state, arena, allowQuarantinedArena: true, cancellationToken);
    }

    private static async Task<Lease> AcquireCoreAsync(uint[] state, uint[] arena, bool allowQuarantinedArena, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(state, arena) && state.Length != 0) { throw new ArgumentException("Logical state and arena must be disjoint.", nameof(arena)); }
        Gate first = Gates.GetValue(state, static _ => new Gate());
        Gate? second = arena.Length == 0 ? null : Gates.GetValue(arena, static _ => new Gate());
        if (second is not null && first.Id > second.Id) { (first, second) = (second, first); }
        WarpCoreCLRAsyncGate.Lease firstLease = await first.Mutex.AcquireAsync(cancellationToken).ConfigureAwait(false);
        WarpCoreCLRAsyncGate.Lease? secondLease = null;
        try
        {
            if (second is not null) { secondLease = await second.Mutex.AcquireAsync(cancellationToken).ConfigureAwait(false); }
            if (Volatile.Read(ref Gates.GetValue(state, static _ => new Gate()).Quarantined) ||
                !allowQuarantinedArena && arena.Length != 0 && Volatile.Read(ref Gates.GetValue(arena, static _ => new Gate()).Quarantined))
            {
                secondLease?.Dispose();
                throw new WarpHostException("WRPCORECLR3002", "Logical word storage is quarantined after a failed owned transaction.");
            }
            return new Lease(firstLease, secondLease);
        }
        catch { firstLease.Dispose(); throw; }
    }

    internal static void Quarantine(uint[] state, uint[] arena)
    {
        Volatile.Write(ref Gates.GetValue(state, static _ => new Gate()).Quarantined, true);
        if (arena.Length != 0) { Volatile.Write(ref Gates.GetValue(arena, static _ => new Gate()).Quarantined, true); }
    }

    internal sealed class Gate
    {
        internal Gate()
        {
            lock (IdentitySync)
            {
                if (nextId == long.MaxValue) { throw new InvalidOperationException("Word ownership namespace exhausted."); }
                Id = ++nextId;
            }
        }
        internal bool Quarantined;
        internal long Id { get; }
        internal WarpCoreCLRAsyncGate Mutex { get; } = new();
    }

    internal sealed class Lease(WarpCoreCLRAsyncGate.Lease first, WarpCoreCLRAsyncGate.Lease? second) : IDisposable
    {
        private int released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) != 0) { return; }
            second?.Dispose(); first.Dispose();
        }
    }
}
