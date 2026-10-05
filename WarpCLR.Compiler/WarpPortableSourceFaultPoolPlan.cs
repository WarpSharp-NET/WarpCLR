using System.Collections.Immutable;
using System.Security.Cryptography;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceFaultPoolPlan
{
    private WarpPortableSourceFaultPoolPlan(WarpPortableSourceFaultFactoryContract contract,
        ImmutableArray<WarpPortableSourceFaultPoolRow> rows, ImmutableArray<WarpPortableSourceFaultPoolResource> resources,
        uint reportsPerRow, uint stringType)
    {
        Contract = contract; Rows = rows; Resources = resources; ReportsPerRow = reportsPerRow; StringType = stringType;
        WarpCompilationAdmission.Require(contract.GraphHash, WarpCompilationResourceKind.ValueSlots,
            (long)rows.Length * reportsPerRow, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
        PreparedCount = checked((uint)rows.Length * reportsPerRow); RootCount = checked(PreparedCount + (uint)resources.Length);
        WordCount = checked(WarpPortableSourceFaultFactoryLayout.HeaderWords + (uint)rows.Length * WarpPortableSourceFaultFactoryLayout.RowWords +
            (uint)resources.Length * WarpPortableSourceFaultFactoryLayout.ResourceWords + PreparedCount * WarpPortableSourceFaultFactoryLayout.PreparedWords +
            (uint)resources.Sum(resource => (long)resource.Text.Length));
        WarpCompilationAdmission.Require(contract.GraphHash, WarpCompilationResourceKind.VerifierWorkspaceSlots,
            WordCount, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
        PlanHash = Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(new
        { Semantics = WarpPortableSourceFaultFactoryLayout.Semantics, contract.ContractHash, contract.GraphHash, contract.VerifiedHash,
            contract.TypeSchemaHash, contract.CliContractHash, Rows, Resources, ReportsPerRow, PreparedCount, RootCount, WordCount, StringType,
            Text = WarpPortableSourceUtf16Identity.Semantics,
            RawResources = resources.Select(resource => new
            {
                resource.Id, Identity = WarpPortableSourceUtf16Identity.CodeUnits(resource.Identity),
                Text = WarpPortableSourceUtf16Identity.CodeUnits(resource.Text), resource.InternedLiteral,
            }),
            Data = WarpPortableSourceExceptionLayout.Semantics })));
    }

    internal WarpPortableSourceFaultFactoryContract Contract { get; }
    internal ImmutableArray<WarpPortableSourceFaultPoolRow> Rows { get; }
    internal ImmutableArray<WarpPortableSourceFaultPoolResource> Resources { get; }
    internal uint ReportsPerRow { get; }
    internal uint PreparedCount { get; }
    internal uint RootCount { get; }
    internal uint WordCount { get; }
    internal uint StringType { get; }
    internal string PlanHash { get; }

    internal static WarpPortableSourceFaultPoolPlan Create(WarpPortableSourceFaultFactoryContract contract,
        WarpPortableSourceHeapSchema schema, IEnumerable<WarpPortableWordBody> sourceBodies, uint reportsPerRow)
    {
        ArgumentNullException.ThrowIfNull(contract); ArgumentNullException.ThrowIfNull(schema); ArgumentNullException.ThrowIfNull(sourceBodies);
        ArgumentOutOfRangeException.ThrowIfZero(reportsPerRow);
        if (!string.Equals(contract.TypeSchemaHash, schema.SchemaHash, StringComparison.Ordinal) ||
            !string.Equals(contract.GraphHash, schema.GraphHash, StringComparison.Ordinal) || !string.Equals(contract.VerifiedHash, schema.VerifiedHash, StringComparison.Ordinal))
        {
            throw Invalid("A reserved fault pool requires the exact captured type/data contract.");
        }
        WarpPortableWordBody[] bodies = WarpCompilationAdmission.Materialize(sourceBodies, contract.GraphHash,
            WarpCompilationResourceKind.Functions, WarpCompilationAdmission.MaximumFunctionsPerEntry);
        var functions = new Dictionary<string, uint>(StringComparer.Ordinal); var ids = new HashSet<int>();
        foreach (WarpPortableWordBody body in bodies.Where(body => body.AliasOwnerFunction == -1))
        {
            if (body.Function <= 0 || !ids.Add(body.Function) || !functions.TryAdd(body.MethodIdentity, (uint)body.Function))
            {
                throw Invalid("Fault sites require unique original logical source-function mappings.");
            }
        }
        ImmutableArray<WarpPortableSourceFaultPoolResource> resources = CaptureResources(contract);
        var rows = ImmutableArray.CreateBuilder<WarpPortableSourceFaultPoolRow>(contract.Rows.Length);
        foreach (WarpPortableSourceFaultFactoryRow row in contract.Rows)
        {
            if (!functions.TryGetValue(row.MethodIdentity, out uint function)) { throw Invalid("A fault row has no original compiled source-function reservation.", row.SourceOffset); }
            uint message = resources.First(resource => string.Equals(resource.Identity, "resource:" + row.ResourceKey, StringComparison.Ordinal)).Id;
            uint param = row.ParamName is null ? 0 : resources.First(resource => string.Equals(resource.Identity, "literal:" + row.ParamName, StringComparison.Ordinal)).Id;
            rows.Add(new(row, function, message, param));
        }
        return new(contract, rows.MoveToImmutable(), resources, reportsPerRow, schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(string))));
    }

    private static ImmutableArray<WarpPortableSourceFaultPoolResource> CaptureResources(WarpPortableSourceFaultFactoryContract contract)
    {
        var data = new Dictionary<string, (string Text, bool Literal)>(StringComparer.Ordinal);
        foreach (WarpPortableSourceFaultFactoryRow row in contract.Rows)
        {
            data.TryAdd("resource:" + row.ResourceKey, (contract.Resources.Text(row.ResourceKey), false));
            if (row.ParamName is { } param) { data.TryAdd("literal:" + param, (param, true)); }
        }
        return data.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select((pair, index) =>
            new WarpPortableSourceFaultPoolResource((uint)index + 1, pair.Key, pair.Value.Text, pair.Value.Literal)).ToImmutableArray();
    }

    private static WarpVerificationException Invalid(string message, int offset = 0) => new("WRPCLR2420", message, offset);
}
