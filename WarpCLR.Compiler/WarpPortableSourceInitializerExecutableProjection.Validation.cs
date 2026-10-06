using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceInitializerExecutableProjection
{
    internal static void RequireOriginShape(WarpPortableSourceOperationOriginKind kind, int? sourceOffset,
        ushort? sourceOpCode, int? effectIndex)
    {
        bool valid = kind switch
        {
            WarpPortableSourceOperationOriginKind.Instruction => sourceOffset is >= 0 && sourceOpCode is not null && effectIndex is >= 0,
            WarpPortableSourceOperationOriginKind.EntryInvocation => sourceOffset is null && sourceOpCode is null && effectIndex is null,
            _ => false,
        };
        if (!valid) { throw Invalid("An origin is either real original CIL (1) or before-body invocation (2); zero and partial source fields are invalid.", sourceOffset ?? 0); }
    }

    private static void RequireSourceCharge(WarpPortableWordSourceBlock source,
        ImmutableArray<WarpPortableSourceInitializerExecutablePoint> points, int entry)
    {
        if (points.IsEmpty || points.Sum(point => point.ChargedSourceSteps) != 1 ||
            points.Count(point => point.ProgramCounter == entry && point.StartsBlock && point.ChargedSourceSteps == 1) != 1 ||
            points.Any(point => point.ChargedSourceSteps != 0 && point.ProgramCounter != entry ||
                point.Block != source.Block && point.SourceCost != 0 || point.RuntimeHelper || point.InvocationPrelude ||
                point.SourceOffset != source.Instruction.Offset || point.SourceOpCode != unchecked((ushort)source.Instruction.OpCode)))
        {
            throw Invalid("Each executable original CIL step must own exactly one charged block-entry node; every continuation and auxiliary node is uncharged.", source.Instruction.Offset);
        }
    }

    private static void RequireSyntheticCharges(WarpPortableWordLoweredProgram program,
        ImmutableArray<WarpPortableSourceInitializerExecutablePoint> points)
    {
        foreach (WarpPortableSourceInitializerExecutablePoint point in points)
        {
            if (point.ChargedSourceSteps is < 0 or > 1 || point.SourceOffset is null && point.ChargedSourceSteps != 0 ||
                point.RuntimeHelper && (point.SourceCost != 0 || point.MethodIdentity is not null || point.SourceOffset is not null) ||
                point.InvocationPrelude && (point.SourceCost != 0 || point.SourceOffset is not null))
            {
                throw Invalid("A synthetic, wrapper, helper or prelude node cannot acquire a guest CIL charge or source identity.");
            }
        }
        int originals = program.Bodies.Sum(body => body.SourceBlocks.Length);
        if (points.Sum(point => point.ChargedSourceSteps) != originals)
        {
            throw Invalid("The final node census does not match the exact original source-step census, including alias projections.");
        }
    }
}
