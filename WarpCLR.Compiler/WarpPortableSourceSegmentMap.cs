using System.Collections.Frozen;
using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// This finite compiler inventory is consistency data. It cannot issue an event,
// interpret a source-boundary number as authority, or release a controller.
internal sealed partial class WarpPortableSourceSegmentMap
{
    internal const string Version = "warp.source-segments/compiler-sealed-original-instruction-separate-entry-invocation-guarded-end-candidates-only/0.2";
    private readonly FrozenDictionary<(int Function, int Block), WarpPortableSourceSegment> byBlock;

    private WarpPortableSourceSegmentMap(WarpPortableWordProgramIdentity identity, WarpLogicalMachineLayout layout,
        ImmutableArray<WarpPortableSourceSegment> segments, WarpPortableSourceInvocation invocation)
    {
        CompilerIdentity = identity.IdentityHash; IrHash = identity.KernelIrHash; Layout = layout; Segments = segments;
        byBlock = segments.ToFrozenDictionary(segment => (segment.Function, segment.Block));
        Invocation = invocation;
        MapHash = WarpPortableSnapshotIdentity.Hash(new
        {
            Version, CompilerIdentity, IrHash,
            Segments = segments.Select(segment => new { Segment = segment, segment.OriginKind,
                segment.CapturedMethodIdentity, segment.CapturedMethodIndex, segment.GuardedFrontiers }),
            Invocation = new { Invocation = invocation, invocation.OriginKind, invocation.SourceOffset, invocation.SourceOpCode,
                invocation.EffectIndex, invocation.Effects, invocation.ExceptionMemberships },
        });
    }

    internal string CompilerIdentity { get; }
    internal string IrHash { get; }
    internal string MapHash { get; }
    internal WarpLogicalMachineLayout Layout { get; }
    internal ImmutableArray<WarpPortableSourceSegment> Segments { get; }
    internal WarpPortableSourceInvocation Invocation { get; }
    internal WarpPortableSourceSegment Find(int function, int block) => byBlock[(function, block)];

    internal void RequireExactSegment(WarpPortableSourceSegment segment)
    {
        if (!byBlock.TryGetValue((segment.Function, segment.Block), out WarpPortableSourceSegment? bound) || !ReferenceEquals(segment, bound))
        { throw new InvalidOperationException("Only the exact immutable compiler segment is a candidate."); }
    }

    internal static WarpPortableSourceSegmentMap Capture(WarpPortableMethodGraph graph,
        WarpPortableSourceHeapSchema schema, WarpPortableWordLoweredProgram program)
    {
        WarpPortableWordProgramIdentity identity = WarpPortableWordProgramIdentity.Validate(graph, schema, program);
        var layout = new WarpLogicalMachineLayout(program.Kernel);
        var segments = ImmutableArray.CreateBuilder<WarpPortableSourceSegment>();
        long workspace = 0;
        foreach (WarpPortableWordBody body in program.Bodies.OrderBy(body => body.Function))
        {
            foreach (WarpPortableWordSourceBlock source in body.SourceBlocks.OrderBy(block => block.Block))
            {
                WarpPortableSourceSegment captured = CaptureSegment(layout, body, source, segments.Count);
                string original = body.AliasOwnerFunction == -1 ? body.MethodIdentity :
                    program.Bodies.First(owner => owner.Function == body.AliasOwnerFunction).MethodIdentity;
                int capturedIndex = program.VerifiedProgram.Methods.Select((method, index) => (method, index)).First(item =>
                    string.Equals(item.method.Identity, original, StringComparison.Ordinal)).index + 1;
                captured = captured with { CapturedMethodIdentity = original, CapturedMethodIndex = capturedIndex };
                workspace += captured.LocalProgramCounters.Length + captured.HelperFunctions.Length + captured.Frontiers.Length +
                    captured.Storage.EntryOperands.Length + captured.Storage.ExitOperands.Length + captured.GuardedFrontiers.Length;
                WarpCompilationAdmission.Require(program.Kernel.Name, WarpCompilationResourceKind.VerifierWorkspaceSlots,
                    workspace, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
                segments.Add(captured);
            }
        }
        return new(identity, layout, segments.ToImmutable(), CaptureInvocation(graph, program, layout));
    }

    private static WarpPortableSourceSegment CaptureSegment(WarpLogicalMachineLayout layout, WarpPortableWordBody body,
        WarpPortableWordSourceBlock source, int id)
    {
        var owned = source.GeneratedBlocks.Append(source.Block).ToFrozenSet();
        WarpLogicalMachineNode[] local = layout.Nodes.Where(node => node.Function == body.Function && owned.Contains(node.Block)).ToArray();
        if (local.Length == 0 || local.Count(node => node.StartsBlock && node.SourceCost != 0) != 1 ||
            layout.IsRuntimeHelper(body.Function) || local.Any(node => node.SourceCost > 1))
        { throw new InvalidOperationException("A source segment must own exactly one original charge and its compiler-generated local blocks."); }
        ImmutableArray<int> helpers = Helpers(layout, local);
        ImmutableArray<WarpPortableSourceSegmentFrontier> frontiers = Frontiers(layout, body.Function, owned, local, helpers);
        bool arena = source.Operation.RequiresLease || source.Instruction.Effects.Any(effect => effect is
            WarpPortableTypedEffect.ReadMemory or WarpPortableTypedEffect.WriteMemory or WarpPortableTypedEffect.Allocate or
            WarpPortableTypedEffect.TypeInitialize or WarpPortableTypedEffect.AtomicSequential or WarpPortableTypedEffect.Acquire or WarpPortableTypedEffect.Release) ||
            local.Concat(layout.Nodes.Where(node => helpers.Contains(node.Function))).Any(UsesArena);
        string roots = WarpPortableSnapshotIdentity.Hash(new { source.Roots, source.ReturnedRoots, source.Operation });
        return new(id, body.Function, source.Block, source.Instruction.Offset, source.Instruction.OpCode,
            layout.GetBlockEntry(body.Function, source.Block), body.MethodIdentity, arena, !source.Instruction.Faults.IsEmpty,
            local.Select(node => node.ProgramCounter).ToImmutableArray(), helpers, frontiers, roots, source, Storage(body, source))
        { GuardedFrontiers = GuardedFrontiers(layout, local, helpers) };
    }

    private static bool UsesArena(WarpLogicalMachineNode node) => node.Instructions.Any(instruction =>
        WarpManagedMemoryOpCode.RequiresArena(instruction.OpCode) || WarpManagedWideAtomicOpCode.IsAtomic(instruction.OpCode));

    private static WarpPortableSourceSegmentStorage Storage(WarpPortableWordBody body, WarpPortableWordSourceBlock source) =>
        new(body.PrivateWordCount, body.EvaluationWordOffset, body.AliasOwnerFunction, body.AliasPrefixWords,
            body.Arguments, body.Locals, body.PrivateTemporaries, Operands(body, source.Instruction.EntryStack), Operands(body, source.Instruction.ExitStack));

    private static ImmutableArray<WarpPortableSourceOperand> Operands(WarpPortableWordBody body, ImmutableArray<WarpPortableTypedValue> values)
    {
        var result = ImmutableArray.CreateBuilder<WarpPortableSourceOperand>(values.Length); int offset = body.EvaluationWordOffset;
        for (int index = 0; index < values.Length; index++)
        {
            if (values[index].WordCount < 0 || values[index].WordCount > body.PrivateWordCount - offset)
            { throw new InvalidOperationException("A typed operand extends beyond its exact compiler private bank."); }
            result.Add(new(index, offset, values[index])); offset = checked(offset + values[index].WordCount);
        }
        return result.MoveToImmutable();
    }
}
