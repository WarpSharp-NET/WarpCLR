using System.Collections.Immutable;
using System.Security.Cryptography;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed class WarpPortableSourceFrameSchema
{
    internal const string Semantics = "warp.source-frame-views/exact-body-argument-local-constructor-storage-original-alias-prefix-separate-tail-raw-utf16-snapshot/0.4";

    private WarpPortableSourceFrameSchema(WarpPortableSourceHeapSchema schema, WarpPortableWordLoweredProgram program,
        ImmutableArray<WarpPortableSourceFrameBody> bodies)
    {
        TypeSchemaHash = schema.SchemaHash; GraphHash = program.GraphHash; VerifiedHash = program.VerifiedHash;
        MapsHash = program.MapsHash; LoweredHash = program.LoweredHash; Bodies = bodies;
        FrameSchemaHash = Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(new
        { Semantics, TypeSchemaHash, GraphHash, VerifiedHash, MapsHash, LoweredHash, Bodies })));
    }

    internal string TypeSchemaHash { get; }
    internal string GraphHash { get; }
    internal string VerifiedHash { get; }
    internal string MapsHash { get; }
    internal string LoweredHash { get; }
    internal string FrameSchemaHash { get; }
    internal ImmutableArray<WarpPortableSourceFrameBody> Bodies { get; }

    internal static WarpPortableSourceFrameBody DescribePlannedBody(WarpPortableSourceHeapSchema schema,
        WarpPortableTypedProgram program, WarpPortableWordBody body)
    {
        ArgumentNullException.ThrowIfNull(schema); ArgumentNullException.ThrowIfNull(program); ArgumentNullException.ThrowIfNull(body);
        if (!string.Equals(schema.GraphHash, program.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(schema.VerifiedHash, program.VerifiedHash, StringComparison.Ordinal) || body.Function <= 0 ||
            body.AliasOwnerFunction != -1 || body.AliasPrefixWords != 0)
        {
            throw new WarpVerificationException("WRPCLR2400", "Planned frame views need the exact original source body, excluding aliases.", 0);
        }
        var types = program.Types.ToDictionary(type => type.Identity, StringComparer.Ordinal);
        var views = new HashSet<WarpPortableSourceFrameView>();
        AddSlots(schema, types, views, body.Arguments, "argument", body.PrivateWordCount);
        AddSlots(schema, types, views, body.Locals, "local", body.PrivateWordCount);
        AddSlots(schema, types, views, body.PrivateTemporaries.Select(temporary =>
            new WarpPortableWordStorageSlot(temporary.Index, temporary.WordOffset, temporary.Type)).ToImmutableArray(), "temporary", body.PrivateWordCount);
        return new((uint)body.Function, (uint)body.PrivateWordCount, views.OrderBy(view => view.ByteOffset).ThenBy(view => view.ByteSpan)
            .ThenBy(view => view.ElementType).ThenBy(view => view.Storage, StringComparer.Ordinal).ThenBy(view => view.Slot).ToImmutableArray());
    }

    internal static WarpPortableSourceFrameSchema Create(WarpPortableSourceHeapSchema schema, WarpPortableWordLoweredProgram program)
    {
        ArgumentNullException.ThrowIfNull(schema); ArgumentNullException.ThrowIfNull(program);
        if (!string.Equals(schema.GraphHash, program.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(schema.VerifiedHash, program.VerifiedHash, StringComparison.Ordinal))
        {
            throw new WarpVerificationException("WRPCLR2400", "The frame view schema belongs to a different typed source closure.", 0);
        }
        var types = program.VerifiedProgram.Types.ToDictionary(type => type.Identity, StringComparer.Ordinal);
        var bodies = ImmutableArray.CreateBuilder<WarpPortableSourceFrameBody>();
        WarpLogicalExecutionMetadata execution = program.Kernel.Execution ??
            throw new WarpVerificationException("WRPCLR2400", "Source frame views require compiled logical body metadata.", 0);
        foreach (WarpPortableWordBody body in program.Bodies.OrderBy(body => body.Function))
        {
            if (body.Function <= 0 || body.Function >= execution.Bodies.Count ||
                execution.Bodies[body.Function].RuntimeHelper || execution.Bodies[body.Function].PrivateWordCount != body.PrivateWordCount)
            {
                throw new WarpVerificationException("WRPCLR2400", "A frame view has no exact compiled source body/private bank.", 0);
            }
            var views = new HashSet<WarpPortableSourceFrameView>();
            WarpLogicalBodyMetadata description = execution.Bodies[body.Function];
            if (body.AliasOwnerFunction != description.AliasOwnerFunction || body.AliasPrefixWords != description.AliasPrefixWords)
            {
                throw new WarpVerificationException("WRPCLR2400", "The frame owner/prefix mapping differs from its exact word root schema.", 0);
            }
            if (description.AliasOwnerFunction == -1)
            {
                AddSlots(schema, types, views, body.Arguments, "argument", body.PrivateWordCount);
                AddSlots(schema, types, views, body.Locals, "local", body.PrivateWordCount);
                AddSlots(schema, types, views, body.PrivateTemporaries.Select(temporary =>
                    new WarpPortableWordStorageSlot(temporary.Index, temporary.WordOffset, temporary.Type)).ToImmutableArray(), "temporary", body.PrivateWordCount);
            }
            else if (description.AliasPrefixWords != body.StoragePrefixWords)
            {
                throw new WarpVerificationException("WRPCLR2400", "A filter frame must alias the exact original argument/local prefix.", 0);
            }
            bodies.Add(new((uint)body.Function, (uint)body.PrivateWordCount, views.OrderBy(view => view.ByteOffset).ThenBy(view => view.ByteSpan)
                .ThenBy(view => view.ElementType).ThenBy(view => view.Storage, StringComparer.Ordinal).ThenBy(view => view.Slot).ToImmutableArray()));
        }
        ImmutableArray<WarpPortableSourceFrameBody> snapshot = bodies.ToImmutable();
        WarpCompilationAdmission.Require(program.GraphHash, WarpCompilationResourceKind.VerifierWorkspaceSlots,
            checked(schema.MetadataWordCount + (long)snapshot.Length * WarpPortableSourceMemoryLayout.FrameWords +
                snapshot.Sum(body => (long)body.Views.Length * WarpPortableSourceMemoryLayout.FrameViewWords)),
            WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
        return new(schema, program, snapshot);
    }

    private static void AddSlots(WarpPortableSourceHeapSchema schema, Dictionary<string, WarpPortableTypedType> types,
        HashSet<WarpPortableSourceFrameView> views, ImmutableArray<WarpPortableWordStorageSlot> slots, string storage, int privateWords)
    {
        foreach (WarpPortableWordStorageSlot slot in slots)
        {
            if (slot.WordOffset < 0 || slot.WordOffset > privateWords || slot.Type.WordCount > privateWords - slot.WordOffset)
            {
                throw new WarpVerificationException("WRPCLR2400", "A captured argument/local extends beyond the compiled private bank.", 0);
            }
            if (slot.Type.Category == WarpPortableStackCategory.ManagedByref) { continue; }
            AddView(schema, types, views, slot.Type.Identity, checked((uint)slot.WordOffset * 4), storage, slot.Index, 0);
        }
    }

    private static void AddView(WarpPortableSourceHeapSchema schema, Dictionary<string, WarpPortableTypedType> types,
        HashSet<WarpPortableSourceFrameView> views, string identity, uint offset, string storage, int slot, int depth)
    {
        if (depth > 64) { throw new WarpVerificationException("WRPCLR2400", "A frame view exceeds bounded value-field nesting.", 0); }
        WarpPortableTypedType type = types[identity];
        views.Add(new(offset, (uint)type.ByteSize, schema.TypeId(identity), storage, slot));
        WarpCompilationAdmission.Require(schema.GraphHash, WarpCompilationResourceKind.VerifierWorkspaceSlots,
            checked((long)views.Count * WarpPortableSourceMemoryLayout.FrameViewWords), WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
        if (type.Category != WarpPortableStackCategory.Value) { return; }
        foreach (WarpPortableTypedField field in type.Fields.Where(field => !field.IsStatic))
        {
            AddView(schema, types, views, field.TypeIdentity, checked(offset + (uint)field.ByteOffset), storage, slot, depth + 1);
        }
    }
}
