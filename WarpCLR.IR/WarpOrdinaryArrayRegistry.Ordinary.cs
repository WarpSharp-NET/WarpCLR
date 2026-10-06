namespace WarpCLR.IR;

internal static partial class WarpOrdinaryArrayRegistry
{
    internal static WarpOrdinaryArrayAdmission AcquireOrdinary(uint[][] inputs, uint[] scalars, uint[] state, uint[] arena)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(scalars);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(arena);
        uint[][] capturedInputs = (uint[][])inputs.Clone();
        uint[][] banks = CaptureBanks(capturedInputs.Concat([scalars, state, arena]).ToArray());
        lock (RegistryGate)
        {
            (BankRecord[] records, bool[] absent) = CaptureRecords(banks);
            foreach (BankRecord record in records)
            {
                if (record.Owner is { } owner && (owner.IsHeld || owner.IsQuarantined))
                {
                    throw new InvalidOperationException("Held or quarantined arrays cannot enter any ordinary bank role.");
                }
                if (record.ActiveUses == int.MaxValue)
                {
                    throw new InvalidOperationException("The ordinary-use census cannot overflow.");
                }
            }
            var use = new OrdinaryUse(Issuer, records);
            var admission = new WarpOrdinaryArrayAdmission(Issuer, capturedInputs, use);
            PublishRecords(banks, records, absent);
            foreach (BankRecord record in records) { record.ActiveUses++; }
            return admission;
        }
    }

    private sealed class OrdinaryUse : IDisposable
    {
        private readonly BankRecord[] records;
        private bool released;

        internal OrdinaryUse(object issuer, BankRecord[] records)
        {
            ValidateIssuer(issuer);
            this.records = records;
        }

        public void Dispose()
        {
            lock (RegistryGate)
            {
                if (released) { return; }
                foreach (BankRecord record in records)
                {
                    if (record.ActiveUses <= 0)
                    {
                        throw new InvalidOperationException("An ordinary use lost its exact active bank census.");
                    }
                }
                foreach (BankRecord record in records) { record.ActiveUses--; }
                released = true;
            }
        }
    }
}
