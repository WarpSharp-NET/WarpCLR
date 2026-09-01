namespace WarpCLR.Runtime.Device;

public sealed class WarpDeviceRuntimeException : Exception
{
    public WarpDeviceRuntimeException(string code, string message)
        : base($"{code}: {message}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}
