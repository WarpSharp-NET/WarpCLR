namespace WarpCLR.Verifier;

public sealed class WarpVerificationException : Exception
{
    public WarpVerificationException(string code, string message, int? ilOffset = null)
        : base($"{code}: {message}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        Code = code;
        IlOffset = ilOffset;
    }

    public string Code { get; }

    public int? IlOffset { get; }
}
