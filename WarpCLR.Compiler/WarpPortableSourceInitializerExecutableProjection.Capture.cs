using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceInitializerExecutableProjection
{
    internal static WarpPortableSourceInitializerExecutableProjection Capture(WarpPortableMethodGraph graph,
        WarpPortableSourceHeapSchema schema, WarpPortableWordLoweredProgram finalProgram)
    {
        WarpPortableWordProgramIdentity identity = WarpPortableWordProgramIdentity.Validate(graph, schema, finalProgram);
        WarpPortableSourceInitializerPlan initializers = WarpPortableSourceInitializerPlan.Capture(graph, finalProgram.VerifiedProgram, schema);
        var layout = new WarpLogicalMachineLayout(finalProgram.Kernel);
        var indices = finalProgram.VerifiedProgram.Methods.Select((method, index) => (method.Identity, Index: index + 1))
            .ToDictionary(item => item.Identity, item => item.Index, StringComparer.Ordinal);
        var sources = new Dictionary<(int Function, int Block), WarpPortableWordSourceBlock>();
        var preludes = new Dictionary<(int Function, int Block), WarpPortableWordInvocationPrelude>();
        var bodies = finalProgram.Bodies.ToDictionary(body => body.Function);
        foreach (WarpPortableWordBody body in finalProgram.Bodies)
        {
            foreach (WarpPortableWordSourceBlock source in body.SourceBlocks)
            {
                foreach (int block in source.GeneratedBlocks) { sources.Add((body.Function, block), source); }
            }
            if (body.InvocationPrelude is { } prelude)
            {
                foreach (int block in prelude.GeneratedBlocks) { preludes.Add((body.Function, block), prelude); }
            }
        }
        ImmutableArray<WarpPortableSourceInitializerExecutablePoint> points = layout.Nodes.Select(node =>
        {
            sources.TryGetValue((node.Function, node.Block), out WarpPortableWordSourceBlock? source);
            bool prelude = preludes.ContainsKey((node.Function, node.Block));
            bodies.TryGetValue(node.Function, out WarpPortableWordBody? body);
            int charge = node.StartsBlock ? node.SourceCost : 0;
            string nodeHash = WarpPortableSnapshotIdentity.Hash(new
            {
                Semantics, identity.IdentityHash, identity.KernelIrHash, identity.StructuralLayoutHash,
                node.ProgramCounter, node.Function, node.Block, node.StartsBlock, node.SourceCost, Charge = charge,
                Source = source, Prelude = prelude, node.Continuation,
            });
            return new WarpPortableSourceInitializerExecutablePoint(node.ProgramCounter, node.Function, node.Block,
                node.StartsBlock, node.SourceCost, charge, layout.IsRuntimeHelper(node.Function), layout.GetAliasOwnerFunction(node.Function),
                body?.MethodIdentity, source?.Instruction.Offset, source is null ? null : unchecked((ushort)source.Instruction.OpCode),
                prelude, source?.Instruction.ExceptionMemberships ?? [], nodeHash);
        }).ToImmutableArray();
        var steps = ImmutableArray.CreateBuilder<WarpPortableSourceInitializerExecutableStep>();
        foreach (WarpPortableWordBody body in finalProgram.Bodies.OrderBy(body => body.Function))
        {
            foreach (WarpPortableWordSourceBlock source in body.SourceBlocks)
            {
                ImmutableArray<WarpPortableSourceInitializerExecutablePoint> nodes = points.Where(point => point.Function == body.Function &&
                    source.GeneratedBlocks.Contains(point.Block)).ToImmutableArray();
                int entry = layout.GetBlockEntry(body.Function, source.Block);
                RequireSourceCharge(source, nodes, entry);
                steps.Add(new(body.MethodIdentity, indices[body.MethodIdentity], body.Function,
                    body.AliasOwnerFunction == -1 ? body.Function : body.AliasOwnerFunction, entry, source, nodes));
            }
        }
        RequireSyntheticCharges(finalProgram, points);
        ImmutableArray<WarpPortableSourceInitializerExecutableStep> sourceSteps = steps.ToImmutable();
        ImmutableArray<WarpPortableSourceInitializerExecutableSite> sites = CaptureSites(graph, finalProgram,
            initializers, layout, identity, indices, points, sourceSteps);
        return new(graph, schema, finalProgram, identity, initializers, layout, points, sourceSteps, sites);
    }

    private static ImmutableArray<WarpPortableSourceInitializerExecutableSite> CaptureSites(WarpPortableMethodGraph graph,
        WarpPortableWordLoweredProgram program, WarpPortableSourceInitializerPlan initializers,
        WarpLogicalMachineLayout layout, WarpPortableWordProgramIdentity identity, Dictionary<string, int> indices,
        ImmutableArray<WarpPortableSourceInitializerExecutablePoint> points,
        ImmutableArray<WarpPortableSourceInitializerExecutableStep> steps)
    {
        var originals = program.Bodies.Where(body => body.AliasOwnerFunction == -1).ToDictionary(body => body.MethodIdentity, StringComparer.Ordinal);
        var sites = ImmutableArray.CreateBuilder<WarpPortableSourceInitializerExecutableSite>();
        foreach (WarpPortableSourceInitializerTrigger trigger in initializers.Triggers)
        {
            WarpPortableSourceOperationOrigin origin = WarpPortableSourceOperationOrigin.CaptureInitializer(graph, program.VerifiedProgram, initializers, trigger);
            RequireOriginShape(origin.Kind, origin.SourceOffset, origin.SourceOpCode, origin.EffectIndex);
            if (!originals.TryGetValue(trigger.MethodIdentity, out WarpPortableWordBody? body) ||
                !originals.TryGetValue(trigger.Initializer, out WarpPortableWordBody? initializer))
            {
                throw Invalid("An initializer origin or cctor has no final original executable body.", trigger.SourceOffset ?? 0);
            }
            WarpPortableWordInvocationPrelude? prelude = null;
            int entry;
            ImmutableArray<WarpPortableSourceInitializerExecutablePoint> nodes;
            ImmutableArray<WarpPortableWordRoot> roots;
            if (origin.Kind == WarpPortableSourceOperationOriginKind.EntryInvocation)
            {
                prelude = body.InvocationPrelude ?? throw Invalid("An entry-invocation origin requires the actual final compiler invocation prelude.");
                if (!string.Equals(prelude.InitializerPlanHash, initializers.PlanHash, StringComparison.Ordinal) ||
                    !string.Equals(prelude.Trigger.Initializer, trigger.Initializer, StringComparison.Ordinal) || prelude.DeclaringType != trigger.Type)
                {
                    throw Invalid("The executable prelude differs from the exact captured initialization plan.");
                }
                entry = layout.GetBlockEntry(body.Function, prelude.GeneratedBlocks[0]); roots = prelude.Roots;
                nodes = points.Where(point => point.Function == body.Function && prelude.GeneratedBlocks.Contains(point.Block)).ToImmutableArray();
                if (nodes.IsEmpty || nodes.Any(node => !node.InvocationPrelude || node.ChargedSourceSteps != 0 ||
                    node.SourceOffset is not null || node.SourceOpCode is not null || !node.ExceptionMemberships.IsEmpty))
                {
                    throw Invalid("Invocation-prelude nodes must precede original CIL without any invented charge, opcode or target-body handler membership.");
                }
            }
            else
            {
                WarpPortableSourceInitializerExecutableStep step = steps.First(step => step.Function == body.Function && step.SourceOffset == origin.SourceOffset);
                if (step.SourceOpCode != origin.SourceOpCode || !step.Instruction.Effects.SequenceEqual(origin.Effects) ||
                    !step.Instruction.ExceptionMemberships.SequenceEqual(origin.ExceptionMemberships))
                {
                    throw Invalid("The final executable source step differs from the original typed initializer effect.", step.SourceOffset);
                }
                entry = step.EntryProgramCounter; roots = step.Source.Roots; nodes = step.Points;
            }
            string siteHash = WarpPortableSnapshotIdentity.Hash(new
            {
                Semantics, identity.IdentityHash, identity.KernelIrHash, identity.MapsHash, initializers.PlanHash,
                origin.OriginHash, trigger, CapturedMethodIndex = indices[body.MethodIdentity], body.Function,
                InitializerCapturedMethodIndex = indices[initializer.MethodIdentity], InitializerFunction = initializer.Function,
                EntryProgramCounter = entry, Points = nodes, Roots = roots, Prelude = prelude,
            });
            sites.Add(new(origin, trigger, indices[body.MethodIdentity], body.Function, indices[initializer.MethodIdentity], initializer.Function,
                entry, nodes, roots, prelude, siteHash));
        }
        return sites.ToImmutable();
    }
}
