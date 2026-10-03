using System.Globalization;

namespace WarpCLR.IR;

public sealed class WarpCompilationResourceException : Exception
{
    public WarpCompilationResourceException()
        : this("Compilation resource admission failed.")
    {
    }

    public WarpCompilationResourceException(string message)
        : base(message)
    {
    }

    public WarpCompilationResourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public WarpCompilationResourceException(string entryIdentity, WarpCompilationResourceKind resource, long requested, long limit)
        : base(string.Create(CultureInfo.InvariantCulture,
            $"Compilation rejected '{entryIdentity}' before dispatch: {resource} usage {requested} exceeds the admitted limit {limit}."))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryIdentity);
        ArgumentOutOfRangeException.ThrowIfNegative(requested);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        if (!Enum.IsDefined(resource))
        {
            throw new ArgumentOutOfRangeException(nameof(resource));
        }

        EntryIdentity = entryIdentity;
        Resource = resource;
        Requested = requested;
        Limit = limit;
    }

    public string EntryIdentity { get; } = "<unknown>";

    public WarpCompilationResourceKind Resource { get; }

    public long Requested { get; }

    public long Limit { get; }
}
