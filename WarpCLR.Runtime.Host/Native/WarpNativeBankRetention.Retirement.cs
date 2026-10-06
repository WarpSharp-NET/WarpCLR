using System.Diagnostics.CodeAnalysis;

namespace WarpCLR.Runtime.Host.Native;

internal sealed partial class WarpNativeBankRetention
{
    internal void Retire(Action<ulong> free, Action quarantine)
    {
        ArgumentNullException.ThrowIfNull(free);
        ArgumentNullException.ThrowIfNull(quarantine);
        if (retirementAttempted) { return; }
        if (parent is not null)
        {
            throw new InvalidOperationException("An attached state bank cannot retire its parent's native allocation.");
        }
        retirementAttempted = true;
        foreach (ref ulong pointer in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(allocations))
        {
            ulong allocation = pointer;
            Attempt(() => free(allocation), unconfirmedOnFailure: true);
        }
        allocations.Clear();
        if (unconfirmed) { RequestQuarantine(quarantine); }
        else
        {
            if (publicationRequested)
            {
                int failuresBeforeCheck = failures.Count;
                Attempt(ValidateCapturedBanks, unconfirmedOnFailure: false);
                if (failures.Count != failuresBeforeCheck) { RequestQuarantine(quarantine); }
            }
            foreach (ref WarpNativeBankRetention state in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(borrowedStates))
            {
                Attempt(state.ReleaseAfterConfirmedRetirement, unconfirmedOnFailure: false);
            }
            Attempt(ReleaseAfterConfirmedRetirement, unconfirmedOnFailure: false);
        }
    }

    internal void RequestQuarantine(Action quarantine) => Attempt(quarantine, unconfirmedOnFailure: false);

    internal void AttemptAdditionalCleanup(Action cleanup) => Attempt(cleanup, unconfirmedOnFailure: false);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "This exact collector retains every cleanup exception and preserves the first exception object while all remaining physical frees are attempted. A thrown free permanently retains original banks; no failure becomes success.")]
    private void Attempt(Action action, bool unconfirmedOnFailure)
    {
        try { action(); }
        catch (Exception failure)
        {
            if (unconfirmedOnFailure) { unconfirmed = true; }
            RecordFailure(failure);
        }
    }
}
