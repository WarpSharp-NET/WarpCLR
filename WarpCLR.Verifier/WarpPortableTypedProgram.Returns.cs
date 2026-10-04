using System.Collections.Immutable;
using System.Reflection;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedProgram
{
    private sealed partial class Builder
    {
        private void ResolveReturnSummaries()
        {
            foreach (WarpPortableMethodGraphMethod method in graph.Methods)
            {
                if (types.Get(method.ReturnType).Category == WarpPortableStackCategory.ManagedByref)
                {
                    summaries.Add(method.Identity, InitialSummary(method));
                }
            }

            if (summaries.Count == 0) { return; }
            for (int iteration = 0; iteration <= graph.Methods.Length; iteration++)
            {
                bool changed = false;
                foreach (WarpPortableMethodGraphMethod method in graph.Methods.Where(method => summaries.ContainsKey(method.Identity)))
                {
                    if (method.SourceMethod.IsAbstract)
                    {
                        WarpPortableTypedReturnSummary abstractSummary = DispatchSummary(method);
                        WarpPortableTypedReturnSummary prior = summaries[method.Identity];
                        changed |= abstractSummary.ReadOnly != prior.ReadOnly || !abstractSummary.Origins.SequenceEqual(prior.Origins);
                        summaries[method.Identity] = abstractSummary; continue;
                    }

                    if (method.Instructions.IsEmpty) { continue; }
                    var verifier = new WarpPortableTypedMethodVerifier(graph, method, types, methods, summaries, validateLifetimes: false);
                    WarpCompilationAdmission.Require(method.Identity, WarpCompilationResourceKind.VerifierWorkspaceSlots,
                        verifier.Workspace, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
                    WarpPortableTypedReturnSummary current = verifier.Verify().ReturnSummary;
                    WarpPortableTypedReturnSummary previous = summaries[method.Identity];
                    changed |= current.ReadOnly != previous.ReadOnly || !current.Origins.SequenceEqual(previous.Origins);
                    summaries[method.Identity] = current;
                }

                if (!changed) { return; }
            }

            throw new WarpVerificationException("WRPCLR2201", "Recursive byref-return summaries did not converge within the bounded method graph.", 0);
        }

        private WarpPortableTypedReturnSummary DispatchSummary(WarpPortableMethodGraphMethod slot)
        {
            WarpPortableTypedReturnSummary[] targets = graph.Dispatches.Where(dispatch => string.Equals(dispatch.Slot, slot.Identity, StringComparison.Ordinal))
                .Select(dispatch => summaries[dispatch.Target]).ToArray();
            ImmutableArray<WarpPortableTypedProvenance> origins = WarpPortableTypedMethodVerifier.SortOrigins(targets.SelectMany(target => target.Origins)
                .Select(origin => origin.Kind == WarpPortableProvenanceKind.Argument ? origin with { OwnerMethod = slot.Identity } : origin));
            return new(origins, targets.Any(target => target.ReadOnly));
        }

        private WarpPortableTypedReturnSummary InitialSummary(WarpPortableMethodGraphMethod method)
        {
            string element = types.Get(method.ReturnType).ElementType!;
            var origins = new List<WarpPortableTypedProvenance>();
            ImmutableArray<string> arguments = method.SourceMethod.IsStatic ? method.ParameterTypes :
                [types.Get(method.SourceMethod.DeclaringType!.IsValueType ? method.SourceMethod.DeclaringType.MakeByRefType() : method.SourceMethod.DeclaringType).Identity, .. method.ParameterTypes];
            for (int index = 0; index < arguments.Length; index++)
            {
                if (types.Get(arguments[index]).Category == WarpPortableStackCategory.ManagedByref &&
                    string.Equals(types.Get(arguments[index]).ElementType, element, StringComparison.Ordinal))
                {
                    string owner = types.Get(arguments[index]).ElementType!;
                    origins.Add(new(WarpPortableProvenanceKind.Argument, method.Identity, index, owner, 0, types.Get(element).ByteSize));
                }
            }

            origins.Add(new(WarpPortableProvenanceKind.HeapInterior, string.Empty, -1, element, 0, types.Get(element).ByteSize));
            origins.Add(new(WarpPortableProvenanceKind.StaticStorage, string.Empty, -1, element, 0, types.Get(element).ByteSize));
            return new(WarpPortableTypedMethodVerifier.SortOrigins(origins), method.SourceMethod is MethodInfo function && WarpPortableTypedMethodVerifier.ReadOnlyParameter(function.ReturnParameter));
        }
    }
}
