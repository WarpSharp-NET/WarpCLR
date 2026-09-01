using System.Collections.ObjectModel;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Device;

public enum WarpFieldKind
{
    UInt32,
    ManagedReference,
}

public sealed record WarpFieldLayout(
    string Name,
    WarpFieldKind Kind,
    int Offset);

public sealed class WarpTypeLayout
{
    private readonly ReadOnlyDictionary<string, WarpFieldLayout> fieldsByName;

    public WarpTypeLayout(
        string identity,
        int size,
        int alignment,
        IEnumerable<WarpFieldLayout> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        ArgumentNullException.ThrowIfNull(fields);
        if (alignment < WarpDeviceAbi.ScopedMinimumAlignment ||
            alignment > 256 ||
            (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(alignment),
                "Type alignment must be a power of two from 4 through 256.");
        }

        WarpFieldLayout[] fieldArray = fields.OrderBy(field => field.Offset).ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var offsets = new HashSet<int>();
        foreach (WarpFieldLayout field in fieldArray)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field.Name);
            if (!Enum.IsDefined(field.Kind))
            {
                throw new ArgumentException($"Field '{field.Name}' has an unregistered type.", nameof(fields));
            }

            if (!names.Add(field.Name))
            {
                throw new ArgumentException($"Field name '{field.Name}' is duplicated.", nameof(fields));
            }

            if (field.Offset < 0 ||
                field.Offset % WarpDeviceAbi.LogicalReferenceSize != 0 ||
                field.Offset > size - WarpDeviceAbi.LogicalReferenceSize)
            {
                throw new ArgumentException($"Field '{field.Name}' has an invalid offset.", nameof(fields));
            }

            if (!offsets.Add(field.Offset))
            {
                throw new ArgumentException($"Field '{field.Name}' overlaps another field.", nameof(fields));
            }
        }

        Identity = identity;
        Size = size;
        Alignment = alignment;
        Fields = Array.AsReadOnly(fieldArray);
        ReferenceFieldOffsets = Array.AsReadOnly(
            fieldArray
                .Where(field => field.Kind == WarpFieldKind.ManagedReference)
                .Select(field => field.Offset)
                .ToArray());
        fieldsByName = new ReadOnlyDictionary<string, WarpFieldLayout>(
            fieldArray.ToDictionary(field => field.Name, StringComparer.Ordinal));
    }

    public string Identity { get; }

    public int Size { get; }

    public int Alignment { get; }

    public bool IsSealed => true;

    public ReadOnlyCollection<WarpFieldLayout> Fields { get; }

    public ReadOnlyCollection<int> ReferenceFieldOffsets { get; }

    internal WarpFieldLayout GetField(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return fieldsByName.TryGetValue(name, out WarpFieldLayout? field)
            ? field
            : throw new WarpDeviceRuntimeException(
                "WRPDEV1002",
                $"Type '{Identity}' does not define field '{name}'.");
    }
}
