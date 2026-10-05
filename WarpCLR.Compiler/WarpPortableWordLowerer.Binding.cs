using System.Collections.Immutable;
using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class Builder
    {
        private readonly HashSet<int> runtimeFunctions = [];
        private readonly List<WarpPortableWordBody> aliasBodies = [];
        private ImmutableArray<WarpPortableWordBody> plannedBodies = [];
        private WarpPortableGeneratedServiceImporter? runtimeImporter;
        private int bindingPhase;
        internal ImmutableArray<WarpPortableWordBody> PlannedBodies => plannedBodies;
        internal WarpPortableGeneratedServiceImporter RuntimeImporter => runtimeImporter ?? throw new InvalidOperationException("No runtime source binding was prepared.");

        private void PrepareBinding(MethodLowerer[] lowerers)
        {
            if (binding is null) { return; }
            plannedBodies = lowerers.Select(lowerer => lowerer.Body).ToImmutableArray();
            runtimeImporter = new(binding.TypeSchemaHash, ReserveRuntimeFunction, StoreRuntimeFunction);
            services.Add(WarpPortableWordExecutionBinding.Version + "/" + binding.Semantics + "/" + binding.BindingHash + "/" + binding.TypeSchemaHash);
            services.Add(WarpPortableSourceServiceBanks.Semantics);
            bindingPhase = 1;
            var context = new PreparationContext(this);
            try { binding.Prepare(context); }
            finally { context.Close(); bindingPhase = 2; }
        }

        private void CompleteImportedBodies()
        {
            if (binding is null) { return; }
            var context = new PreparationContext(this);
            try { binding.AfterSourceLowering(context); }
            finally { context.Close(); bindingPhase = 3; }
            foreach (WarpPortableGeneratedServiceImport import in runtimeImporter!.Imports) { services.Add(import.Identity); }
            bodies.AddRange(aliasBodies);
        }

        private string? CompleteBinding(WarpControlFlowKernel kernel, string mapsHash, WarpPortableWordEntryProjection entry)
        {
            if (binding is null) { return null; }
            string planHash = binding.Complete(new(graph, program, kernel, bodies.ToImmutableArray(), runtimeImporter!.Imports, entry, mapsHash));
            WarpPortableWordExecutionBinding.RequireHash(planHash);
            bindingPhase = 4;
            return planHash;
        }

        private WarpLogicalExecutionMetadata Execution(WarpLogicalBodyMetadata[] described)
        {
            WarpPortableWordExecutionCapabilities? flags = binding?.Capabilities;
            return new(described, frameOwners: flags?.FrameOwners == true, runtimeStateAccess: flags?.RuntimeStateAccess == true,
                nonlocalStateDispatch: flags?.NonlocalStateDispatch == true, managedExceptionTermination: flags?.ManagedExceptionTermination == true);
        }

        private int ReserveRuntimeFunction(string identity)
        {
            RequireBindingMutation(); ArgumentException.ThrowIfNullOrWhiteSpace(identity);
            if (sources.ContainsKey(identity)) { throw new ArgumentException("A runtime import cannot replace a captured source method.", nameof(identity)); }
            int function = ReserveService(identity); runtimeFunctions.Add(function); return function;
        }

        private void StoreRuntimeFunction(WarpControlFlowFunction function, WarpLogicalBodyMetadata description)
        {
            RequireBindingMutation(); ArgumentNullException.ThrowIfNull(function); ArgumentNullException.ThrowIfNull(description);
            if (!runtimeFunctions.Contains(function.Id) || !string.Equals(functionIds.First(pair => pair.Value == function.Id).Key, function.Name, StringComparison.Ordinal))
            {
                throw new ArgumentException("A runtime body must match its reserved exact identity.", nameof(function));
            }
            if (functions[function.Id] is { } existing && (!WarpPortableWordServiceComparison.Equal(existing, function) ||
                !EqualBodyMetadata(metadata[function.Id]!, description)))
            {
                throw new ArgumentException("A reused runtime body has different IR or logical-frame metadata.", nameof(function));
            }
            functions[function.Id] = function; metadata[function.Id] = description;
        }

        private static bool EqualBodyMetadata(WarpLogicalBodyMetadata first, WarpLogicalBodyMetadata second) =>
            first.PrivateWordCount == second.PrivateWordCount && first.RuntimeHelper == second.RuntimeHelper &&
            first.CountsSourceDepth == second.CountsSourceDepth && first.AliasOwnerFunction == second.AliasOwnerFunction &&
            first.AliasPrefixWords == second.AliasPrefixWords && first.SourceBlockCosts.SequenceEqual(second.SourceBlockCosts);

        internal void RequireBindingMutation()
        {
            if (bindingPhase is not (1 or 2)) { throw new InvalidOperationException("The source binding emission phase is closed."); }
        }

        private sealed class PreparationContext(Builder owner) : WarpPortableWordBindingPreparation
        {
            private bool active = true;
            internal override WarpCLR.Verifier.WarpPortableMethodGraph Graph { get { Check(); return owner.graph; } }
            internal override WarpCLR.Verifier.WarpPortableTypedProgram Program { get { Check(); return owner.program; } }
            internal override ImmutableArray<WarpPortableWordBody> SourceBodies
            {
                get { Check(); return owner.bodies.Count == 0 ? owner.plannedBodies : owner.bodies.ToImmutableArray(); }
            }
            internal override ImmutableArray<WarpControlFlowFunction> CompiledSourceFunctions
            {
                get { Check(); return owner.plannedBodies.Select(body => owner.functions[body.Function - 1]).OfType<WarpControlFlowFunction>().ToImmutableArray(); }
            }
            internal override WarpPortableGeneratedServiceImporter Services { get { Check(); return owner.runtimeImporter!; } }
            internal override int ReserveFunction(string identity) { Check(); return owner.ReserveRuntimeFunction(identity); }
            internal override void InstallFunction(WarpControlFlowFunction function, WarpLogicalBodyMetadata metadata)
            {
                Check();
                if (!metadata.RuntimeHelper) { throw new ArgumentException("Additional source bodies require an exact filter alias mapping.", nameof(metadata)); }
                owner.StoreRuntimeFunction(function, metadata);
            }
            internal override void InstallFilterAlias(WarpControlFlowFunction function, WarpLogicalBodyMetadata metadata, WarpPortableWordBody body)
            {
                Check(); owner.StoreFilterAlias(function, metadata, body);
            }
            internal override WarpLogicalBodyMetadata SourceMetadata(int function)
            {
                Check();
                if (!owner.plannedBodies.Any(body => body.Function - 1 == function) || owner.metadata[function] is not { } description)
                {
                    throw new ArgumentException("The requested source metadata is not compiled yet.", nameof(function));
                }
                return description;
            }
            internal override void RequireService(string identity) { Check(); ArgumentException.ThrowIfNullOrWhiteSpace(identity); owner.services.Add(identity); }
            internal void Close() => active = false;
            private void Check()
            {
                if (!active) { throw new InvalidOperationException("The source binding preparation capability is closed."); }
                owner.RequireBindingMutation();
            }
        }
    }
}
