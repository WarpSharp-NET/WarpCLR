namespace WarpCLR.Runtime.Host;

public sealed class WarpHostException : Exception
{
    public WarpHostException()
        : this("WRPHOST0000", "The runtime operation failed.")
    {
    }

    public WarpHostException(string message)
        : this("WRPHOST0000", message)
    {
    }

    public WarpHostException(string message, Exception innerException)
        : this("WRPHOST0000", message, innerException)
    {
    }

    public WarpHostException(string code, string message)
        : base($"{code}: {message}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public WarpHostException(string code, string message, Exception innerException)
        : base($"{code}: {message}", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}
