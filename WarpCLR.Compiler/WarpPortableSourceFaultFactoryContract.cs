using System.Collections.Immutable;
using System.Security.Cryptography;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceFaultFactoryContract
{
    internal const string Semantics = "warp.source-fault-factory/exact-source-op-effect-descriptor-type-raw-utf16-resource-data-reserved-generation-ticket-raw-utf16-snapshot/0.3";

    private WarpPortableSourceFaultFactoryContract(WarpPortableMethodGraph graph, WarpPortableTypedProgram program,
        WarpPortableSourceHeapSchema schema, ImmutableArray<WarpPortableSourceFaultFactoryRow> rows, WarpPortableSourceExceptionResources resources)
    {
        GraphHash = graph.GraphHash; VerifiedHash = program.VerifiedHash; TypeSchemaHash = schema.SchemaHash;
        CliContractHash = program.CliSizes?.ContractHash; Rows = rows; Resources = resources;
        ContractHash = Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(new
        { Semantics, GraphHash, VerifiedHash, TypeSchemaHash, CliContractHash, Rows, Resources.ContractHash,
            Text = WarpPortableSourceUtf16Identity.Semantics,
            RawRowText = rows.Select(row => new
            {
                Method = WarpPortableSourceUtf16Identity.CodeUnits(row.MethodIdentity),
                Operation = WarpPortableSourceUtf16Identity.CodeUnits(row.OperationIdentity),
                Resource = WarpPortableSourceUtf16Identity.CodeUnits(row.ResourceKey),
                Param = row.ParamName is { } name ? WarpPortableSourceUtf16Identity.CodeUnits(name) : null,
            }),
            Data = WarpPortableSourceExceptionLayout.Semantics, Schema = WarpPortableSourceHeapSchema.Version })));
    }

    internal string GraphHash { get; }
    internal string VerifiedHash { get; }
    internal string TypeSchemaHash { get; }
    internal string? CliContractHash { get; }
    internal string ContractHash { get; }
    internal ImmutableArray<WarpPortableSourceFaultFactoryRow> Rows { get; }
    internal WarpPortableSourceExceptionResources Resources { get; }

    internal static WarpPortableSourceFaultFactoryContract CaptureFinite(WarpPortableMethodGraph graph,
        WarpPortableTypedProgram program, WarpPortableSourceHeapSchema schema)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(program); ArgumentNullException.ThrowIfNull(schema);
        if (!string.Equals(graph.GraphHash, program.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(schema.GraphHash, graph.GraphHash, StringComparison.Ordinal) || !string.Equals(schema.VerifiedHash, program.VerifiedHash, StringComparison.Ordinal))
        {
            throw new WarpVerificationException("WRPCLR2420", "A fault factory contract requires the exact closed typed source/schema graph.", 0);
        }
        var rows = ImmutableArray.CreateBuilder<WarpPortableSourceFaultFactoryRow>();
        foreach (WarpPortableSourceHeapFault fault in schema.Faults)
        {
            string? key = FiniteResource(fault.Kind);
            if (key is not null)
            {
                rows.Add(new((uint)rows.Count + 1, fault.MethodIdentity, fault.SourceOffset, fault.SourceOpCode, fault.EffectIndex,
                    fault.Kind, fault.ExceptionType, "warp.source-cil-implicit/" + unchecked((ushort)fault.SourceOpCode).ToString("X4", System.Globalization.CultureInfo.InvariantCulture),
                    0, key, null));
            }
        }
        AddFiniteMath(graph, program, schema, rows);
        ImmutableArray<WarpPortableSourceFaultFactoryRow> snapshot = rows.OrderBy(row => row.MethodIdentity, StringComparer.Ordinal).ThenBy(row => row.SourceOffset)
            .ThenBy(row => row.EffectIndex).ThenBy(row => row.FaultDescriptor).Select((row, index) => row with { Id = (uint)index + 1 }).ToImmutableArray();
        return new(graph, program, schema, snapshot, WarpPortableSourceExceptionResources.Capture(snapshot.Select(row => row.ResourceKey)));
    }

    private static string? FiniteResource(WarpPortableTypedFaultKind kind) => kind switch
    {
        WarpPortableTypedFaultKind.NullReference => "Arg_NullReferenceException",
        WarpPortableTypedFaultKind.IndexOutOfRange => "Arg_IndexOutOfRangeException",
        WarpPortableTypedFaultKind.Overflow => "Arg_OverflowException",
        WarpPortableTypedFaultKind.DivideByZero => "Arg_DivideByZero",
        WarpPortableTypedFaultKind.ArrayTypeMismatch => "Arg_ArrayTypeMismatchException",
        _ => null,
    };
}
