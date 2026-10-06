namespace WarpCLR.IR;

// This is registry consistency data. An arbitrary owner object is not a Root Source grant.
internal sealed class WarpOrdinaryArrayOwner
{
    private readonly object owner;
    private readonly object authority;
    private uint[][] banks = [];
    private bool held;
    private bool quarantined;

    internal WarpOrdinaryArrayOwner(object issuer, object owner, object hiddenAuthority)
    {
        WarpOrdinaryArrayRegistry.ValidateIssuer(issuer);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(hiddenAuthority);
        this.owner = owner;
        authority = hiddenAuthority;
    }

    internal bool IsHeld => held;
    internal bool IsQuarantined => quarantined;

    internal bool Matches(object exactOwner, object hiddenAuthority) =>
        ReferenceEquals(owner, exactOwner) && ReferenceEquals(authority, hiddenAuthority);

    internal uint[][] CopyBanks(object issuer)
    {
        WarpOrdinaryArrayRegistry.ValidateIssuer(issuer);
        return (uint[][])banks.Clone();
    }

    internal void PublishBanks(object issuer, uint[][] capturedBanks)
    {
        WarpOrdinaryArrayRegistry.ValidateIssuer(issuer);
        banks = capturedBanks;
    }

    internal void PublishHeld(object issuer, bool value)
    {
        WarpOrdinaryArrayRegistry.ValidateIssuer(issuer);
        held = value;
    }

    internal void PublishQuarantine(object issuer)
    {
        WarpOrdinaryArrayRegistry.ValidateIssuer(issuer);
        quarantined = true;
    }
}
