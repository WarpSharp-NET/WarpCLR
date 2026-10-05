namespace WarpCLR.Runtime.Host;

internal static partial class WarpCoreCLRWordTransaction
{
    internal static async Task<BatchLease> AcquireBatchAsync(uint[][] states, CancellationToken cancellationToken)
    {
        if (states.Distinct(ReferenceEqualityComparer.Instance).Count() != states.Length)
        { throw new ArgumentException("A word batch cannot repeat a mutable logical state.", nameof(states)); }
        Gate[] gates = states.Select(state => Gates.GetValue(state, static _ => new Gate())).OrderBy(gate => gate.Id).ToArray();
        var leases = new List<WarpCoreCLRAsyncGate.Lease>(gates.Length);
        try
        {
            foreach (Gate gate in gates)
            {
                leases.Add(await gate.Mutex.AcquireAsync(cancellationToken).ConfigureAwait(false));
                if (Volatile.Read(ref gate.Quarantined)) { throw new WarpHostException("WRPCORECLR3002", "A logical batch contains quarantined state."); }
            }
            return new BatchLease(leases.ToArray());
        }
        catch { foreach (ref readonly WarpCoreCLRAsyncGate.Lease lease in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(leases)) { lease.Dispose(); } throw; }
    }

    internal sealed class BatchLease(WarpCoreCLRAsyncGate.Lease[] leases) : IDisposable
    {
        private int released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) != 0) { return; }
            foreach (WarpCoreCLRAsyncGate.Lease lease in leases) { lease.Dispose(); }
        }
    }
}
