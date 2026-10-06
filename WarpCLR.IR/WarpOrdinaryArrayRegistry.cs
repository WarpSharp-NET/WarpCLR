using System.Runtime.CompilerServices;

namespace WarpCLR.IR;

internal static partial class WarpOrdinaryArrayRegistry
{
    internal const string Version = "warp.ordinary-array-exclusion/exact-nonempty-banks-held-sticky-quarantine-retained-use/0.1";
    private static readonly Lock RegistryGate = new();
    private static readonly object Issuer = new();
    private static readonly ConditionalWeakTable<uint[], BankRecord> Banks = new();
    private static readonly ConditionalWeakTable<object, WarpOrdinaryArrayOwner> Owners = new();

    internal static WarpOrdinaryArrayOwner EnrollOwner(object owner, object hiddenAuthority, uint[][] banks)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(hiddenAuthority);
        uint[][] captured = CaptureBanks(banks);
        lock (RegistryGate)
        {
            bool fresh = !Owners.TryGetValue(owner, out WarpOrdinaryArrayOwner? candidate);
            candidate ??= new WarpOrdinaryArrayOwner(Issuer, owner, hiddenAuthority);
            if (!candidate.Matches(owner, hiddenAuthority) || candidate.IsQuarantined)
            {
                throw new InvalidOperationException("The exact owner authority changed or this owner is permanently quarantined.");
            }
            (BankRecord[] records, bool[] absent) = CaptureRecords(captured);
            foreach (BankRecord record in records)
            {
                if (record.Owner is not null && !ReferenceEquals(record.Owner, candidate) || record.ActiveUses != 0)
                {
                    throw new InvalidOperationException("Every enrolled bank must be free of another owner and active ordinary use.");
                }
            }
            uint[][] allBanks = CaptureBanks(candidate.CopyBanks(Issuer).Concat(captured).ToArray());
            PublishEnrollment(owner, candidate, captured, records, absent, allBanks, fresh);
            return candidate;
        }
    }

    internal static void Hold(WarpOrdinaryArrayOwner handle, object owner, object hiddenAuthority)
    {
        lock (RegistryGate)
        {
            RequireOwner(handle, owner, hiddenAuthority);
            if (handle.IsQuarantined)
            {
                throw new InvalidOperationException("A quarantined owner cannot acquire a new hold.");
            }
            foreach (uint[] bank in handle.CopyBanks(Issuer))
            {
                if (Banks.GetValue(bank, static _ => throw new InvalidOperationException("An enrolled bank is absent.")).ActiveUses != 0)
                {
                    throw new InvalidOperationException("A hold cannot replace an active ordinary use.");
                }
            }
            handle.PublishHeld(Issuer, true);
        }
    }

    internal static void ReleaseHold(WarpOrdinaryArrayOwner handle, object owner, object hiddenAuthority)
    {
        lock (RegistryGate)
        {
            RequireOwner(handle, owner, hiddenAuthority);
            if (handle.IsQuarantined || !handle.IsHeld)
            {
                throw new InvalidOperationException("Only an exact live nonquarantined hold may be released.");
            }
            handle.PublishHeld(Issuer, false);
        }
    }

    internal static void Quarantine(WarpOrdinaryArrayOwner handle, object owner, object hiddenAuthority)
    {
        lock (RegistryGate)
        {
            RequireOwner(handle, owner, hiddenAuthority);
            // Existing uses remain counted; quarantine never stops or disposes them.
            handle.PublishQuarantine(Issuer);
        }
    }

    internal static void ValidateIssuer(object issuer)
    {
        if (!ReferenceEquals(issuer, Issuer))
        {
            throw new InvalidOperationException("The private ordinary-array registry issuer is required.");
        }
    }

    private static void RequireOwner(WarpOrdinaryArrayOwner handle, object owner, object hiddenAuthority)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(hiddenAuthority);
        if (!handle.Matches(owner, hiddenAuthority) || !Owners.TryGetValue(owner, out WarpOrdinaryArrayOwner? current) ||
            !ReferenceEquals(current, handle))
        {
            throw new InvalidOperationException("The owner handle and hidden authority are not this exact registered enrollment.");
        }
    }

    private static uint[][] CaptureBanks(uint[][] banks)
    {
        ArgumentNullException.ThrowIfNull(banks);
        uint[][] captured = (uint[][])banks.Clone();
        var distinct = new HashSet<uint[]>(ReferenceEqualityComparer.Instance);
        foreach (uint[] bank in captured)
        {
            ArgumentNullException.ThrowIfNull(bank, nameof(banks));
            if (bank.Length != 0) { distinct.Add(bank); }
        }
        return distinct.ToArray();
    }

    private static (BankRecord[] Records, bool[] Absent) CaptureRecords(uint[][] banks)
    {
        var records = new BankRecord[banks.Length];
        var absent = new bool[banks.Length];
        for (int index = 0; index < banks.Length; index++)
        {
            absent[index] = !Banks.TryGetValue(banks[index], out BankRecord? record);
            records[index] = record ?? new BankRecord(Issuer);
        }
        return (records, absent);
    }

    private sealed class BankRecord
    {
        internal BankRecord(object issuer) => ValidateIssuer(issuer);
        internal WarpOrdinaryArrayOwner? Owner { get; set; }
        internal int ActiveUses { get; set; }
    }
}
