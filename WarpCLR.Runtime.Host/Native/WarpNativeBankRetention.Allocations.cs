namespace WarpCLR.Runtime.Host.Native;

internal sealed partial class WarpNativeBankRetention
{
    internal ulong Allocate(nuint bytes, Func<nuint, ulong> allocate)
    {
        ArgumentNullException.ThrowIfNull(allocate);
        ObjectDisposedException.ThrowIf(released || retirementAttempted, this);
        if (unconfirmed)
        {
            throw new WarpHostException("WRPNATIVE1006", "An unconfirmed native allocation cannot admit another allocation.");
        }
        if (parent is not null || allocations.Count == allocationCapacity)
        {
            throw new InvalidOperationException("The native allocation is outside this exact retained bank lifetime.");
        }

        allocationAttempted = true;
        // Until the allocator returns a nonnull receipt and it is tracked,
        // even a thrown Allocate cannot establish that no buffer exists.
        bool received = false;
        try
        {
            ulong pointer = allocate(bytes);
            if (pointer == 0)
            {
                throw new WarpHostException("WRPNATIVE1007", "The native allocator returned a null buffer capability.");
            }
            allocations.Add(pointer);
            received = true;
            return pointer;
        }
        finally { if (!received) { unconfirmed = true; } }
    }

    internal void AttachState(WarpNativeBankRetention state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(released || retirementAttempted, this);
        ObjectDisposedException.ThrowIf(state.released || state.retirementAttempted, state);
        if (ReferenceEquals(state, this) || state.parent is not null || state.allocationAttempted ||
            state.borrowedStates.Count != 0 || state.capturedInputs.Length != 0 || state.knownScalars.Length != 0)
        {
            throw new InvalidOperationException("Only a fresh state-bank use may join the exact existing native allocation lifetime.");
        }
        borrowedStates.Add(state);
        state.parent = this;
    }

    internal void ReleaseIfNoAllocationAttempt()
    {
        if (!allocationAttempted && parent is null) { ReleaseAfterConfirmedRetirement(); }
    }

    private void ReleaseAfterConfirmedRetirement()
    {
        lock (RetentionGate)
        {
            if (released) { return; }
            if (!Retained.Contains(this))
            {
                throw new InvalidOperationException("The exact retained native bank use is absent.");
            }
            admission.Dispose();
            Retained.Remove(this);
            reservations--;
            released = true;
            GC.KeepAlive(capturedInputs);
            GC.KeepAlive(Scalars);
            GC.KeepAlive(State);
        }
    }
}
