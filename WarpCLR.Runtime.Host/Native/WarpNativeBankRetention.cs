using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

// Ordinary denial/lifetime accounting only. This never grants Source execution,
// a pointer provenance claim, a controller lease, or permission to publish.
internal sealed partial class WarpNativeBankRetention : IDisposable
{
    internal const string Version = "warp.native-original-bank-retention/exact-input-capture-known-array-scalars-state-positive-free-sticky-unknown/0.1";
    private const int MaximumRetainedUses = 4096;
    private static readonly Lock RetentionGate = new();
    private static readonly HashSet<WarpNativeBankRetention> Retained = [];
    private static int reservations;
    private readonly uint[][] capturedInputs;
    private readonly uint[] knownScalars;
    private readonly WarpOrdinaryArrayAdmission admission;
    private readonly List<ulong> allocations;
    private readonly List<WarpNativeBankRetention> borrowedStates = [];
    private readonly List<Exception> failures = [];
    private readonly int allocationCapacity;
    private ExceptionDispatchInfo? firstFailure;
    private WarpNativeBankRetention? parent;
    private bool allocationAttempted;
    private bool unconfirmed;
    private bool retirementAttempted;
    private bool publicationRequested;
    private bool released;

    private WarpNativeBankRetention(uint[][] inputs, IReadOnlyList<uint> scalars, uint[] state,
        WarpOrdinaryArrayAdmission admission)
    {
        capturedInputs = inputs;
        knownScalars = scalars as uint[] ?? [];
        this.admission = admission;
        Inputs = Array.AsReadOnly(inputs);
        Scalars = scalars;
        State = state;
        allocationCapacity = checked(inputs.Length + 2);
        allocations = new List<ulong>(allocationCapacity);
    }

    internal ReadOnlyCollection<uint[]> Inputs { get; }
    // An opaque list keeps its existing getter contract. Its backing-array
    // provenance is not established by this ordinary bank guard.
    internal IReadOnlyList<uint> Scalars { get; }
    internal uint[] State { get; }
    internal ReadOnlyCollection<Exception> Failures => failures.AsReadOnly();

    internal static WarpNativeBankRetention Acquire(IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint> scalars, uint[] state)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(scalars);
        ArgumentNullException.ThrowIfNull(state);
        Reserve();
        WarpOrdinaryArrayAdmission? acquiredAdmission = null;
        try
        {
            uint[][] captured = CaptureInputs(inputs);
            acquiredAdmission = WarpOrdinaryArrayAdmission.Acquire(captured, scalars as uint[] ?? [], state, []);
            var use = new WarpNativeBankRetention(captured, scalars, state, acquiredAdmission);
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

    private static uint[][] CaptureInputs(IReadOnlyList<uint[]> inputs)
    {
        // This is the sole read of caller outer references. No bank contents
        // are inspected here; all subsequent use consumes this exact capture.
        int count = inputs.Count;
        var captured = new uint[count][];
        for (int index = 0; index < count; index++) { captured[index] = inputs[index]; }
        return captured;
    }

    private static void Reserve()
    {
        lock (RetentionGate)
        {
            if (reservations == MaximumRetainedUses)
            {
                throw new WarpHostException("WRPNATIVE1005", "The retained native bank-use capacity is exhausted.");
            }
            reservations++;
        }
    }

    internal void RequirePublishable()
    {
        ObjectDisposedException.ThrowIf(released || retirementAttempted, this);
        ValidateCapturedBanks();
    }

    private void ValidateCapturedBanks()
    {
        using WarpOrdinaryArrayAdmission check = WarpOrdinaryArrayAdmission.Acquire(capturedInputs, knownScalars, State, []);
        foreach (ref WarpNativeBankRetention state in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(borrowedStates)) { state.ValidateCapturedBanks(); }
    }

    internal void PreparePublication()
    {
        RequirePublishable();
        publicationRequested = true;
    }

    internal void RecordFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        firstFailure ??= ExceptionDispatchInfo.Capture(failure);
        foreach (ref Exception existing in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(failures))
        {
            if (ReferenceEquals(existing, failure)) { return; }
        }
        failures.Add(failure);
    }

    internal void ThrowFirstFailure() => firstFailure?.Throw();

    public void Dispose()
    {
        if (allocationAttempted && !retirementAttempted)
        {
            throw new InvalidOperationException("Native bank admission requires actual allocation retirement receipts.");
        }
        ReleaseIfNoAllocationAttempt();
    }
}
