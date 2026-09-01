namespace WarpCLR.Runtime.Host;

public sealed class WarpHostException : Exception
{
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
