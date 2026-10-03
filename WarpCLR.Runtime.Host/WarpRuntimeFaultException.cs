using System.Globalization;

namespace WarpCLR.Runtime.Host;

public sealed class WarpRuntimeFaultException : Exception
{
    public WarpRuntimeFaultException()
        : this("<unknown>", 0, WarpRuntimeFaultKind.RuntimeFailure, new InvalidOperationException("The logical-worker fault has no supplied detail."))
    {
    }

    public WarpRuntimeFaultException(string message)
        : base(message)
    {
        EntryIdentity = "<unknown>";
        Kind = WarpRuntimeFaultKind.RuntimeFailure;
    }

    public WarpRuntimeFaultException(string message, Exception innerException)
        : base(message, innerException)
    {
        EntryIdentity = "<unknown>";
        Kind = WarpRuntimeFaultKind.RuntimeFailure;
    }

    internal WarpRuntimeFaultException(string entryIdentity, int workerIndex, WarpRuntimeFaultKind kind, Exception innerException)
        : base(string.Create(CultureInfo.InvariantCulture, $"Entry '{entryIdentity}' failed at logical worker {workerIndex}: {kind}."), innerException)
    {
        EntryIdentity = entryIdentity;
        WorkerIndex = workerIndex;
        Kind = kind;
    }

    public string EntryIdentity { get; }

    public int WorkerIndex { get; }

    public WarpRuntimeFaultKind Kind { get; }
}
