using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    internal const string Version = "warp.portable-typed-word-cil-source-initializer-invocation-raw-utf16-snapshot/0.3";

    internal static WarpPortableWordLoweredProgram Lower(WarpPortableMethodGraph graph, WarpPortableTypedProgram program)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(program);
        if (!string.Equals(graph.GraphHash, program.GraphHash, StringComparison.Ordinal))
        {
            throw new WarpVerificationException("WRPCLR2300", "The typed program belongs to a different captured closure.", 0);
        }
        return new Builder(graph, program).Lower();
    }

    internal static WarpPortableWordLoweredProgram Lower(WarpPortableMethodGraph graph, WarpPortableTypedProgram program,
        WarpPortableSourceHeapSchema schema, WarpPortableWordExecutionBinding binding)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(schema); ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(graph.GraphHash, program.GraphHash, StringComparison.Ordinal))
        {
            throw new WarpVerificationException("WRPCLR2300", "The typed program belongs to a different captured closure.", 0);
        }
        binding.RequireClosure(graph, program, schema);
        return new Builder(graph, program, binding).Lower();
    }

    private sealed partial class Builder
    {
        private readonly WarpPortableMethodGraph graph;
        private readonly WarpPortableTypedProgram program;
        private readonly Dictionary<string, WarpPortableMethodGraphMethod> sources;
        private readonly Dictionary<string, WarpPortableTypedMethod> methods;
        private readonly Dictionary<string, WarpPortableTypedType> types;
        private readonly Dictionary<string, int> functionIds = new(StringComparer.Ordinal);
        private readonly List<WarpControlFlowFunction?> functions = [];
        private readonly List<WarpLogicalBodyMetadata?> metadata = [];
        private readonly List<WarpPortableWordBody> bodies = [];
        private readonly HashSet<string> services = new(StringComparer.Ordinal);
        private readonly WarpPortableWordExecutionBinding? binding;

        internal WarpPortableMethodGraph Graph => graph;
        internal WarpPortableTypedProgram Program => program;
        internal Dictionary<string, WarpPortableMethodGraphMethod> Sources => sources;
        internal Dictionary<string, WarpPortableTypedType> Types => types;
        internal Dictionary<string, int> FunctionIds => functionIds;
        internal HashSet<string> Services => services;
        internal WarpPortableWordExecutionBinding? Binding => binding;
        private int PhysicalScalarArgumentCount => binding?.Capabilities.PrivateController == true ? 1 : 0;

        internal Builder(WarpPortableMethodGraph graph, WarpPortableTypedProgram program, WarpPortableWordExecutionBinding? binding = null)
        {
            this.graph = graph; this.program = program; this.binding = binding;
            sources = graph.Methods.ToDictionary(method => method.Identity, StringComparer.Ordinal);
            methods = program.Methods.ToDictionary(method => method.Identity, StringComparer.Ordinal);
            types = program.Types.ToDictionary(type => type.Identity, StringComparer.Ordinal);
        }

        internal WarpPortableWordLoweredProgram Lower()
        {
            services.Add(WarpPortableWordMapHash.Version);
            services.Add(WarpPortableSourceOperationMetadata.Version);
            services.Add(WarpPortableWordPrivateTemporary.Semantics);
            if (program.CliSizes is { } cliSizes)
            {
                services.Add(WarpPortableCliSizeContract.Semantics + "/" + cliSizes.ContractHash);
                services.Add(WarpPortableCliNativeInteger.Semantics + "/" + cliSizes.ContractHash);
            }
            DiscoverSourceFunctions();
            string[] identities = functionIds.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToArray();
            MethodLowerer[] lowerers = identities.Select(identity => new MethodLowerer(this, sources[identity], methods[identity], functionIds[identity])).ToArray();
            PrepareBinding(lowerers);
            foreach (MethodLowerer lowering in lowerers)
            {
                WarpControlFlowFunction function = lowering.Lower();
                functions[function.Id] = function;
                metadata[function.Id] = lowering.Metadata;
                bodies.Add(lowering.Body);
            }
            CompleteImportedBodies();
            WarpBasicBlock entry = Entry();
            WarpControlFlowFunction[] closed = functions.Select(function => function ?? throw new InvalidOperationException("A lowered function is missing.")).ToArray();
            WarpLogicalBodyMetadata[] described = [new(0, true, [0]), .. metadata.Select(body => body ?? throw new InvalidOperationException("A lowered body map is missing."))];
            int inputs = Math.Max(1, methods[graph.EntryIdentity].ArgumentTypes.Sum(identity => types[identity].WordCount));
            string serviceIdentity = WarpPortableSnapshotIdentity.Hash(services.Order(StringComparer.Ordinal).ToArray());
            WarpPortableWordEntryProjection entryProjection = EntryProjection();
            string mapsHash = WarpPortableWordMapHash.Compute(bodies, entryProjection);
            WarpLogicalExecutionMetadata execution = Execution(described);
            var kernel = new WarpControlFlowKernel(Version + "/" + program.VerifiedHash + "/" + serviceIdentity + "/" + mapsHash, inputs, PhysicalScalarArgumentCount,
                [entry], null, closed, execution);
            string? planHash = CompleteBinding(kernel, mapsHash, entryProjection);
            if (planHash is not null)
            {
                services.Add(WarpPortableWordExecutionBinding.Version + "/plan/" + planHash);
                serviceIdentity = WarpPortableSnapshotIdentity.Hash(services.Order(StringComparer.Ordinal).ToArray());
                kernel = new(Version + "/" + program.VerifiedHash + "/" + serviceIdentity + "/" + mapsHash, inputs, PhysicalScalarArgumentCount, [entry], null, closed, execution);
            }
            string hash = WarpPortableSnapshotIdentity.Hash(new { Version, program.VerifiedHash, IrHash = WarpIrHash.Compute(kernel) });
            var lowered = new WarpPortableWordLoweredProgram(program, hash, mapsHash, kernel, bodies.ToImmutableArray(), services.Order(StringComparer.Ordinal).ToImmutableArray(), entryProjection)
                { ExecutionBindingHash = binding?.BindingHash, ExecutionPlanHash = planHash };
            WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, program);
            ExceptionAttachment? attachment = null;
            if (exceptionPlan is not null)
            {
                AttachmentRegistrations.Add(lowered, new(graph, schema, exceptionPlan));
                try { attachment = ExceptionAttachment.CaptureBoundProgram(lowered); }
                finally { AttachmentRegistrations.Remove(lowered); }
            }
            return lowered.SealCompilerIdentity(graph, schema, attachment);
        }

        internal static WarpVerificationException Error(string method, string message, int offset) => new("WRPCLR2300", method + ": " + message, offset);
    }
}
