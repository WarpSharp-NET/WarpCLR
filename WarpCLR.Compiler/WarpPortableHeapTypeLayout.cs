using System.Collections.ObjectModel;

namespace WarpCLR.Compiler;

internal sealed class WarpPortableHeapTypeLayout
{
    public WarpPortableHeapTypeLayout(uint id, string identity, uint kind, uint payloadWords, IEnumerable<uint> assignableTo,
        IEnumerable<WarpPortableHeapReferenceLayout>? references = null, uint elementType = 0, uint staticWords = 0,
        IEnumerable<WarpPortableHeapReferenceLayout>? staticReferences = null)
    {
        ArgumentNullException.ThrowIfNull(assignableTo);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        Id = id;
        Identity = identity;
        Kind = kind;
        PayloadWords = payloadWords;
        ElementType = elementType;
        StaticWords = staticWords;
        AssignableTo = Array.AsReadOnly(assignableTo.Distinct().Order().ToArray());
        References = Array.AsReadOnly(references?.OrderBy(item => item.Offset).ToArray() ?? []);
        StaticReferences = Array.AsReadOnly(staticReferences?.OrderBy(item => item.Offset).ToArray() ?? []);
    }

    public uint Id { get; }
    public string Identity { get; }
    public uint Kind { get; }
    public uint PayloadWords { get; }
    public uint ElementType { get; }
    public uint StaticWords { get; }
    public ReadOnlyCollection<uint> AssignableTo { get; }
    public ReadOnlyCollection<WarpPortableHeapReferenceLayout> References { get; }
    public ReadOnlyCollection<WarpPortableHeapReferenceLayout> StaticReferences { get; }
}
