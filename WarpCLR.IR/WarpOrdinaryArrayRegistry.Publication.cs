namespace WarpCLR.IR;

internal static partial class WarpOrdinaryArrayRegistry
{
    private static void PublishEnrollment(object ownerIdentity, WarpOrdinaryArrayOwner owner, uint[][] banks,
        BankRecord[] records, bool[] absent, uint[][] allBanks, bool freshOwner)
    {
        bool ownerAdded = false;
        try
        {
            PublishRecords(banks, records, absent);
            if (freshOwner) { Owners.Add(ownerIdentity, owner); ownerAdded = true; }
        }
        catch
        {
            RollBackRecords(banks, records, absent);
            if (ownerAdded) { Owners.Remove(ownerIdentity); }
            throw;
        }
        foreach (BankRecord record in records) { record.Owner = owner; }
        owner.PublishBanks(Issuer, allBanks);
    }

    private static void PublishRecords(uint[][] banks, BankRecord[] records, bool[] absent)
    {
        try
        {
            for (int index = 0; index < banks.Length; index++)
            {
                if (absent[index]) { Banks.Add(banks[index], records[index]); }
            }
        }
        catch
        {
            RollBackRecords(banks, records, absent);
            throw;
        }
    }

    private static void RollBackRecords(uint[][] banks, BankRecord[] records, bool[] absent)
    {
        for (int index = 0; index < banks.Length; index++)
        {
            if (absent[index] && Banks.TryGetValue(banks[index], out BankRecord? actual) && ReferenceEquals(actual, records[index]))
            {
                Banks.Remove(banks[index]);
            }
        }
    }
}
