using System.Security.Cryptography;
using System.Text.Json;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// The fixed compiler composition permits only the audited frame source domain
// plus instructions owned by the concrete shared EH implementation. It is not
// a callback/waiver or a grant for null throw operands or implicit factories.
internal sealed class WarpPortableClosedFrameExceptionBinding : WarpPortableWordExecutionBinding
{
    internal const string CompositionSemantics = "warp.source-frame-exception-composition/exact-concrete-instruction-ownership-common-importer-planned-frames-layout-temp-retirement/0.1";
    private readonly WarpPortableClosedFrameSourceBinding frames;
    private readonly WarpPortableExceptionSourceBinding exceptions;

    internal WarpPortableClosedFrameExceptionBinding(WarpPortableClosedFrameSourcePlan plan,
        WarpPortableExceptionSourceBinding exceptions)
        : base(plan?.Graph.GraphHash ?? throw new ArgumentNullException(nameof(plan)), plan.Program.VerifiedHash,
            plan.Schema.SchemaHash, CompositionSemantics, Identity(plan, exceptions), CapabilitiesOf(exceptions),
            plan.Methods.Concat(exceptions.AdditionalSourceMethods), exceptions.ExceptionMethods)
    {
        exceptions.RequireClosure(plan.Graph, plan.Program, plan.Schema);
        if (!string.Equals(plan.ExceptionOwnershipHash, exceptions.InstructionOwnershipHash, StringComparison.Ordinal))
        {
            throw WarpPortableClosedFrameSourcePlan.Invalid("The source-frame domain was not audited against this exact concrete EH instruction inventory.", 0);
        }
        this.exceptions = exceptions;
        frames = new(plan);
    }

    internal WarpPortableExceptionPlan ExceptionPlan => exceptions.Plan;

    internal override void Prepare(WarpPortableWordBindingPreparation context)
    {
        // Capture views from original bodies before the EH implementation installs
        // aliases. Both import into the same checked reservation namespace.
        frames.Prepare(context);
        exceptions.Prepare(context);
        context.RequireService(CompositionSemantics + "/" + BindingHash);
    }

    internal override WarpBlockTerminator? LowerInstruction(WarpPortableWordInstructionContext context)
    {
        if (exceptions.OwnsInstruction(context.SourceMethod.Identity, context.Instruction))
        {
            return exceptions.LowerInstruction(context) ?? throw WarpPortableClosedFrameSourcePlan.Invalid(
                "The concrete EH binding did not lower its exact owned source instruction.", context.Instruction.Offset);
        }
        return frames.LowerInstruction(context);
    }

    internal override void AfterSourceLowering(WarpPortableWordBindingPreparation context)
    {
        frames.AfterSourceLowering(context);
        exceptions.AfterSourceLowering(context);
    }

    internal override string Complete(WarpPortableWordBindingCompletion context)
    {
        string frameHash = frames.Complete(context);
        string exceptionHash = exceptions.Complete(context);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            CompositionSemantics, BindingHash, Frames = frameHash, Exceptions = exceptionHash,
            exceptions.InstructionOwnershipHash, context.MapsHash,
            LayoutProjection = WarpPortableWordProgramIdentity.ComputeLayoutProjection(context.StructuralKernel),
        })));
    }

    private static WarpPortableWordExecutionCapabilities CapabilitiesOf(WarpPortableExceptionSourceBinding exceptions)
    {
        ArgumentNullException.ThrowIfNull(exceptions);
        return exceptions.Capabilities with { FrameOwners = true, RuntimeStateAccess = true, NonlocalStateDispatch = true };
    }

    private static string Identity(WarpPortableClosedFrameSourcePlan plan, WarpPortableExceptionSourceBinding exceptions)
    {
        ArgumentNullException.ThrowIfNull(exceptions);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            CompositionSemantics, plan.PlanHash, exceptions.BindingHash, exceptions.InstructionOwnershipHash,
            Domain = "closed-internal-frame-values;private-prepared-nonnull-throw-domain-validation-required;no-factory-or-allocation-grant",
        })));
    }
}
