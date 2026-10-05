using System.Collections.Immutable;
using System.Security.Cryptography;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceHeapSchema
{
    internal const string Version = "warp.source-heap/typed-byte-layout-dense-type-dispatch-fault-views-array-shapes-exception-data-raw-utf16-snapshot/0.5";
    private readonly WarpPortableHeapSchema runtime;

    private WarpPortableSourceHeapSchema(WarpPortableMethodGraph graph, WarpPortableTypedProgram typed,
        ImmutableArray<WarpPortableSourceHeapType> types, ImmutableArray<WarpPortableSourceHeapField> fields,
        ImmutableArray<WarpPortableSourceHeapFault> faults, ImmutableArray<WarpPortableSourceHeapServiceFault> serviceFaults,
        ImmutableArray<WarpPortableSourceMemoryView> views, ImmutableArray<WarpPortableSourceNullableLayout> nullableLayouts,
        ImmutableArray<WarpPortableSourceExceptionType> exceptionTypes, ImmutableDictionary<string, Type> sourceTypes)
    {
        GraphHash = graph.GraphHash; VerifiedHash = typed.VerifiedHash; Types = types; Fields = fields; Faults = faults; ServiceFaults = serviceFaults; MemoryViews = views; NullableLayouts = nullableLayouts;
        ExceptionTypes = exceptionTypes; SourceTypes = sourceTypes;
        runtime = new(types.Select(type => new WarpPortableHeapTypeLayout(type.Id, type.Identity, type.Kind,
            type.PayloadWords, type.AssignableTo, type.References, type.ElementType, type.StaticWords, type.StaticReferences)));
        SchemaHash = Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(new
        {
            Version, GraphHash, VerifiedHash, Types, Fields, Faults, ServiceFaults, MemoryViews, NullableLayouts, ExceptionTypes, graph.Dispatches,
            Heap = WarpPortableHeapLayout.Semantics, Exceptions = WarpPortableSourceExceptionLayout.Semantics,
            Delegates = WarpPortableSourceDelegateLayout.Semantics, TypeObjects = WarpPortableSourceTypeObjectLayout.Semantics,
            ByteViews = WarpPortableSourceMemoryLayout.Semantics,
            Arrays = WarpPortableSourceArrayLayout.Semantics,
        })));
    }

    internal string GraphHash { get; }
    internal string VerifiedHash { get; }
    internal string SchemaHash { get; }
    internal ImmutableArray<WarpPortableSourceHeapType> Types { get; }
    internal ImmutableArray<WarpPortableSourceHeapField> Fields { get; }
    internal ImmutableArray<WarpPortableSourceHeapFault> Faults { get; }
    internal ImmutableArray<WarpPortableSourceHeapServiceFault> ServiceFaults { get; }
    internal ImmutableArray<WarpPortableSourceMemoryView> MemoryViews { get; }
    internal ImmutableArray<WarpPortableSourceNullableLayout> NullableLayouts { get; }
    internal ImmutableArray<WarpPortableSourceExceptionType> ExceptionTypes { get; }
    // Compile-time captured CLR metadata only; no host object execution or grant.
    internal ImmutableDictionary<string, Type> SourceTypes { get; }
    internal long MetadataWordCount => checked(WarpPortableHeapLayout.HeaderWords + (long)Types.Length * WarpPortableHeapLayout.TypeWords +
        (long)Types.Length * Types.Length + Types.Sum(type => type.StaticWords + (long)(type.References.Length + type.StaticReferences.Length) * 2) +
        WarpPortableSourceMemoryLayout.HeaderWords + (long)Types.Length * WarpPortableSourceMemoryLayout.TypeWords +
        (long)MemoryViews.Length * WarpPortableSourceMemoryLayout.ViewWords + (long)NullableLayouts.Length * WarpPortableSourceMemoryLayout.NullableWords +
        (long)Types.Length * WarpPortableSourceExceptionLayout.ExceptionTypeWords);

    internal uint TypeId(string identity) => Types.First(type => string.Equals(type.Identity, identity, StringComparison.Ordinal)).Id;

    internal uint[] CreateArena(uint context, uint payloadWords, uint maximumObjects, uint maximumRoots,
        uint maximumWorkers, uint quotaWords) => AttachMemoryViews(runtime.CreateArena(context, payloadWords, maximumObjects, maximumRoots, maximumWorkers, quotaWords));

    internal uint[] CreateArena(uint context, uint payloadWords, uint maximumObjects, uint maximumRoots,
        uint maximumWorkers, uint quotaWords, WarpPortableSourceFrameSchema frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (!string.Equals(SchemaHash, frames.TypeSchemaHash, StringComparison.Ordinal))
        {
            throw new WarpVerificationException("WRPCLR2400", "The source frame views belong to a different heap schema.", 0);
        }
        return AttachMemoryViews(runtime.CreateArena(context, payloadWords, maximumObjects, maximumRoots, maximumWorkers, quotaWords), frames);
    }

    internal static WarpPortableSourceHeapSchema Create(WarpPortableMethodGraph graph, WarpPortableTypedProgram typed)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(typed);
        if (!string.Equals(graph.GraphHash, typed.GraphHash, StringComparison.Ordinal))
        {
            throw new WarpVerificationException("WRPCLR2400", "The source heap schema belongs to a different verified closure.", 0);
        }
        return new Builder(graph, typed).Build();
    }

    private sealed partial class Builder
    {
        private readonly WarpPortableMethodGraph graph;
        private readonly WarpPortableTypedProgram program;
        private readonly Dictionary<string, WarpPortableTypedType> storage;
        private readonly Dictionary<string, Type> sources;
        private readonly Dictionary<string, uint> typeIds;
        private readonly Dictionary<string, WarpPortableMethodGraphType> metadata;
        private readonly Dictionary<string, WarpPortableMethodGraphField> fieldMetadata;

        internal Builder(WarpPortableMethodGraph graph, WarpPortableTypedProgram program)
        {
            this.graph = graph; this.program = program;
            storage = program.Types.ToDictionary(type => type.Identity, StringComparer.Ordinal);
            sources = SourceTypes();
            typeIds = sources.Where(pair => HeapType(pair.Value)).OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select((pair, index) => (pair.Key, Id: checked((uint)index + 1))).ToDictionary(pair => pair.Key, pair => pair.Id, StringComparer.Ordinal);
            metadata = graph.Types.ToDictionary(type => type.Identity, StringComparer.Ordinal);
            fieldMetadata = graph.Fields.ToDictionary(field => field.Identity, StringComparer.Ordinal);
        }

        internal WarpPortableSourceHeapSchema Build()
        {
            ImmutableArray<WarpPortableSourceHeapType> types = typeIds.OrderBy(pair => pair.Value).Select(pair => Layout(pair.Key, pair.Value)).ToImmutableArray();
            ImmutableArray<WarpPortableSourceMemoryView> views = MemoryViews(types);
            ImmutableArray<WarpPortableSourceNullableLayout> nullableLayouts = NullableLayouts();
            RequireMetadataBudget(types, views.Length, nullableLayouts.Length);
            return new(graph, program, types, FieldMaps(), FaultMaps(), ServiceFaultMaps(), views, nullableLayouts, ExceptionTypes(),
                sources.ToImmutableDictionary(StringComparer.Ordinal));
        }

        private static bool HeapType(Type source) => source != typeof(void) && !source.IsByRef && !source.IsPointer &&
            source != typeof(IntPtr) && source != typeof(UIntPtr);

        private uint Id(Type source) => typeIds[WarpPortableMethodGraphIdentity.Type(source)];
    }
}
