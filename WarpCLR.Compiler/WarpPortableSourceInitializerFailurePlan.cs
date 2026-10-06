using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceInitializerFailurePlan
{
    internal const string Semantics = "warp.source-initializer-failure/captured-instruction-or-invocation-origin-raw-type-name-message-reserved-wrapper-original-inner-cached-owner-no-private-grant/0.1";

    private WarpPortableSourceInitializerFailurePlan(WarpPortableMethodGraph graph, WarpPortableTypedProgram program,
        WarpPortableSourceInitializerPlan initializers, WarpPortableSourceExceptionResources resources,
        ImmutableArray<WarpPortableSourceInitializerFailureType> types, ImmutableArray<WarpPortableSourceInitializerFailureRow> rows, uint stringType)
    {
        GraphHash = graph.GraphHash; VerifiedHash = program.VerifiedHash; TypeSchemaHash = initializers.TypeSchemaHash;
        InitializerPlanHash = initializers.PlanHash; Resources = resources; Types = types; Rows = rows; StringType = stringType;
        RootCount = checked((uint)types.Length * 3);
        WordCount = checked(WarpPortableSourceInitializerFailureLayout.HeaderWords + (uint)types.Length * WarpPortableSourceInitializerFailureLayout.TypeWords +
            (uint)rows.Length * WarpPortableSourceInitializerFailureLayout.RowWords + (uint)types.Sum(type => (long)type.TypeName.Length + type.Message.Length));
        WarpCompilationAdmission.Require(GraphHash, WarpCompilationResourceKind.VerifierWorkspaceSlots,
            WordCount, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
        PlanHash = WarpPortableSnapshotIdentity.Hash(new
        {
            Semantics, GraphHash, VerifiedHash, TypeSchemaHash, InitializerPlanHash, resources.ContractHash, Types, StringType, RootCount, WordCount,
            Rows = rows.Select(row => new
            {
                row.Id, row.TypeRecord, row.CapturedMethodIndex, row.Origin.Kind, row.Origin.MethodIdentity,
                row.Origin.SourceOffset, row.Origin.SourceOpCode, row.Origin.EffectIndex, row.Origin.CapturedSourceHash,
                row.Origin.OriginHash, row.Origin.Effects, row.Origin.ExceptionMemberships,
            }),
            Origin = WarpPortableSourceOperationOrigin.Semantics, Message = WarpPortableSourceInitializerMessage.Semantics,
            Layout = WarpPortableSourceInitializerFailureLayout.Semantics,
            RequiredAuthority = "exact-private-state-arena-program-prelude-or-source-site-activation-root-epoch-ticket-before-take-or-raise;compilation-and-preparation-mint-no-grant",
            Failure = "source-ordinary-exception-including-explicit-OOM-SO-TIE-retains-original-inner;source-resource-termination-is-not-wrapped",
        });
    }

    internal string GraphHash { get; }
    internal string VerifiedHash { get; }
    internal string TypeSchemaHash { get; }
    internal string InitializerPlanHash { get; }
    internal string PlanHash { get; }
    internal WarpPortableSourceExceptionResources Resources { get; }
    internal ImmutableArray<WarpPortableSourceInitializerFailureType> Types { get; }
    internal ImmutableArray<WarpPortableSourceInitializerFailureRow> Rows { get; }
    internal uint StringType { get; }
    internal uint RootCount { get; }
    internal uint WordCount { get; }

    internal static WarpPortableSourceInitializerFailurePlan Capture(WarpPortableMethodGraph graph,
        WarpPortableTypedProgram program, WarpPortableSourceHeapSchema schema)
    {
        WarpPortableSourceInitializerPlan initializers = WarpPortableSourceInitializerPlan.Capture(graph, program, schema);
        WarpPortableSourceExceptionResources resources = WarpPortableSourceExceptionResources.Capture([WarpPortableSourceInitializerMessage.ResourceKey]);
        ImmutableArray<WarpPortableSourceInitializerFailureType> types = initializers.Types.Where(type => initializers.Triggers.Any(trigger => trigger.Type == type.Type))
            .Select((type, index) =>
            {
                ImmutableArray<ushort> message = WarpPortableSourceInitializerMessage.Materialize(resources.Text(WarpPortableSourceInitializerMessage.ResourceKey), type.ExceptionTypeName);
                string hash = WarpPortableSnapshotIdentity.Hash(new
                {
                    Semantics, type.Type, type.DeclaringType, type.Initializer, type.WrapperType, type.DefaultHResult,
                    type.ExceptionTypeName, Message = message, resources.ContractHash,
                });
                return new WarpPortableSourceInitializerFailureType((uint)index + 1, type.Type, type.WrapperType, type.DefaultHResult,
                    type.ExceptionTypeName, message, hash);
            }).ToImmutableArray();
        ImmutableArray<WarpPortableSourceInitializerFailureRow> rows = initializers.Triggers.Select((trigger, index) =>
            new WarpPortableSourceInitializerFailureRow((uint)index + 1, types.First(type => type.Type == trigger.Type).Id,
                checked((uint)program.Methods.IndexOf(program.Methods.First(method => string.Equals(method.Identity, trigger.MethodIdentity, StringComparison.Ordinal))) + 1),
                WarpPortableSourceOperationOrigin.CaptureInitializer(graph, program, initializers, trigger))).ToImmutableArray();
        return new(graph, program, initializers, resources, types, rows, schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(string))));
    }
}
