namespace WarpCLR.Runtime.Host;

// An ordinary authenticated worker binding receipt, never a Root Source grant.
internal sealed class WarpCoreCLRInputBinding : IAsyncDisposable
{
    private readonly object issuer;
    private readonly WarpCoreCLRWorkerProcess process;
    private readonly uint tag;
    private readonly byte[] identity;
    private readonly WarpCoreCLRTransferAdmission.Lease retention;
    private readonly Lock sync = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private uint[][] originalInputs;
    private uint[] originalScalars;
    private int uses;
    private bool closing;
    private Task? disposal;

    internal WarpCoreCLRInputBinding(object issuer, WarpCoreCLRWorkerProcess process, uint tag, byte[] identity,
        WarpCoreCLRTransferAdmission.Lease retention, uint[][] capturedInputs, uint[] scalars)
    {
        WarpCoreCLRWorkerProcess.ValidateInputBindingIssuer(issuer);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(retention);
        ArgumentNullException.ThrowIfNull(capturedInputs);
        ArgumentNullException.ThrowIfNull(scalars);
        if (tag == 0 || identity.Length != 32) { throw new ArgumentException("An ordinary input binding requires its exact admitted tag and SHA256 digest.", nameof(identity)); }
        this.issuer = issuer; this.process = process; this.tag = tag; this.retention = retention;
        this.identity = (byte[])identity.Clone();
        originalInputs = (uint[][])capturedInputs.Clone();
        originalScalars = scalars;
    }

    internal uint Tag => tag;
    internal byte[] Identity => (byte[])identity.Clone();
    internal WarpCoreCLRWorkerProcess Process => process;
    internal bool IsDisposed { get { lock (sync) { return closing; } } }

    internal BatchUse AcquireBatchUse(WarpCoreCLRWorkerProcess exactProcess)
    {
        lock (sync)
        {
            if (!ReferenceEquals(process, exactProcess) || closing)
            { throw new InvalidOperationException("An ordinary batch requires this exact live input binding and process."); }
            if (uses == int.MaxValue) { throw new InvalidOperationException("The input-binding use census cannot overflow."); }
            var use = new BatchUse(issuer, this);
            uses++;
            return use;
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource begin;
        Task result;
        lock (sync)
        {
            if (disposal is not null) { return new(disposal); }
            closing = true;
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            result = DisposeCoreAsync(begin.Task, uses == 0 ? Task.CompletedTask : drained.Task);
            disposal = result;
        }
        // No worker gate, stream access or asynchronous unbind runs under the binding lock.
        begin.SetResult();
        return new(result);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003:Avoid awaiting foreign Tasks", Justification = "The only awaited gates are binding-owned RunContinuationsAsynchronously completion sources: begin is released after leaving the binding lock, and the original batch-use census releases drained. ConfigureAwait(false) has no synchronization-context or thread-affinity dependency. Skipping either exact gate would unbind and release original banks before active batches finish.")]
    private async Task DisposeCoreAsync(Task begin, Task waitForUses)
    {
        await begin.ConfigureAwait(false);
        await waitForUses.ConfigureAwait(false);
        try { await process.UnbindInputsAsync(this).ConfigureAwait(false); }
        finally
        {
            try { retention.Dispose(); }
            finally { lock (sync) { originalInputs = []; originalScalars = []; } }
        }
    }

    private void ReleaseUse()
    {
        lock (sync)
        {
            if (uses <= 0) { throw new InvalidOperationException("An input-binding use lost its live census."); }
            uses--;
            if (uses == 0 && closing) { drained.TrySetResult(); }
        }
    }

    internal sealed class BatchUse : IDisposable
    {
        private readonly WarpCoreCLRInputBinding binding;
        private readonly uint[][] originalInputs;
        private readonly uint[] originalScalars;
        private int released;

        internal BatchUse(object issuer, WarpCoreCLRInputBinding binding)
        {
            WarpCoreCLRWorkerProcess.ValidateInputBindingIssuer(issuer);
            ArgumentNullException.ThrowIfNull(binding);
            this.binding = binding;
            originalInputs = binding.originalInputs;
            originalScalars = binding.originalScalars;
        }

        internal int InputCount { get { RequireLive(); return originalInputs.Length; } }
        internal uint[] Scalars { get { RequireLive(); return originalScalars; } }

        internal uint[] GetInput(int index)
        {
            RequireLive();
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, originalInputs.Length);
            return originalInputs[index];
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) { binding.ReleaseUse(); }
        }

        private void RequireLive() => ObjectDisposedException.ThrowIf(Volatile.Read(ref released) != 0, this);
    }
}
