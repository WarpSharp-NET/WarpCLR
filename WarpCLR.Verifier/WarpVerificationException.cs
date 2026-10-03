namespace WarpCLR.Verifier;

public sealed class WarpVerificationException : Exception
{
    public WarpVerificationException()
        : this("WRPCIL0000", "Verification failed.")
    {
    }

    public WarpVerificationException(string message)
        : this("WRPCIL0000", message)
    {
    }

    public WarpVerificationException(string message, Exception innerException)
        : base($"WRPCIL0000: {message}", innerException)
    {
        Code = "WRPCIL0000";
    }

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
