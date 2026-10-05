using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableClosedFrameExceptionCases
{
    internal static void CompositeIdentityAttachesTheActualPlanAndRejectsCloneOrHashReplacement()
    {
        WarpPortableExceptionSourceProgram fixture = Capture(nameof(Sources.FailedPair));
        WarpPortableWordProgramIdentity identity = WarpPortableWordProgramIdentity.Validate(fixture.Graph, fixture.Schema, fixture.Lowered);
        Assert.IsNotNull(identity.ExceptionAttachment);
        Assert.AreSame(fixture.Plan, identity.ExceptionAttachment.Plan);
        Assert.AreNotEqual(fixture.Plan.PlanHash, fixture.Lowered.ExecutionPlanHash, StringComparer.Ordinal);
        Assert.AreSame(identity, WarpPortableWordProgramIdentity.Validate(fixture.Graph, fixture.Schema, fixture.Lowered));
        foreach (WarpPortableWordLoweredProgram changed in new[]
        {
            fixture.Lowered with { }, fixture.Lowered with { ExecutionPlanHash = fixture.Plan.PlanHash },
        })
        {
            WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() =>
                WarpPortableWordProgramIdentity.Validate(fixture.Graph, fixture.Schema, changed));
            Assert.AreEqual("WRPCLR2350", error.Code, StringComparer.Ordinal);
        }
        var unsealed = new WarpPortableWordLoweredProgram(fixture.Lowered.VerifiedProgram, fixture.Lowered.LoweredHash,
            fixture.Lowered.MapsHash, fixture.Lowered.Kernel, fixture.Lowered.Bodies, fixture.Lowered.RequiredServices,
            fixture.Lowered.EntryProjection)
            { ExecutionBindingHash = fixture.Lowered.ExecutionBindingHash, ExecutionPlanHash = fixture.Lowered.ExecutionPlanHash };
        WarpVerificationException replaced = Assert.ThrowsExactly<WarpVerificationException>(() =>
            unsealed.SealCompilerIdentity(fixture.Graph, fixture.Schema, identity.ExceptionAttachment));
        Assert.AreEqual("WRPCLR2350", replaced.Code, StringComparer.Ordinal);
        foreach (WarpPortableWordLoweredProgram candidate in new[] { fixture.Lowered, unsealed })
        {
            WarpVerificationException unregistered = Assert.ThrowsExactly<WarpVerificationException>(() =>
                WarpPortableWordLowerer.ExceptionAttachment.CaptureBoundProgram(candidate));
            Assert.AreEqual("WRPCLR2350", unregistered.Code, StringComparer.Ordinal);
        }
    }

    internal static void CompletionCannotAttachASecondPlanOrAPlanFromAnotherExactSource()
    {
        WarpPortableExceptionSourceProgram fixture = Capture(nameof(Sources.FailedPair));
        WarpPortableExceptionPlan foreign = Capture(nameof(Sources.NormalPair)).Plan;
        foreach (bool duplicate in new[] { false, true })
        {
            var exceptions = new WarpPortableExceptionSourceBinding(fixture.Graph, fixture.Typed, fixture.Schema,
                WarpPortableExceptionTestBuilder.Controller);
            WarpPortableClosedFrameSourcePlan plan = WarpPortableClosedFrameSourcePlan.CaptureForExceptionComposition(
                fixture.Graph, fixture.Typed, fixture.Schema, exceptions);
            var exact = new WarpPortableClosedFrameExceptionBinding(plan, exceptions);
            var witness = new AttachmentWitnessBinding(exact, foreign, duplicate);
            WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() =>
                WarpPortableWordLowerer.Lower(fixture.Graph, fixture.Typed, fixture.Schema, witness));
            Assert.AreEqual("WRPCLR2350", error.Code, StringComparer.Ordinal);
            Assert.IsNotNull(witness.Completed);
            Assert.ThrowsExactly<InvalidOperationException>(() => witness.Completed.AttachExceptionPlan(fixture.Plan));
            Assert.ThrowsExactly<InvalidOperationException>(() => _ = witness.Completed.Bodies);
        }
    }

    // Negative compiler metadata witnesses only; this wrapper emits exactly the
    // concrete production compiler binding before attempting a denied attachment.
    private sealed class AttachmentWitnessBinding(WarpPortableClosedFrameExceptionBinding exact,
        WarpPortableExceptionPlan foreign, bool duplicate) : WarpPortableWordExecutionBinding(exact.GraphHash,
            exact.VerifiedHash, exact.TypeSchemaHash, "warp.compiler-test/denied-plan-attachment/0.1", exact.BindingHash,
            exact.Capabilities, exact.AdditionalSourceMethods, exact.ExceptionMethods)
    {
        internal WarpPortableWordBindingCompletion? Completed { get; private set; }
        internal override void Prepare(WarpPortableWordBindingPreparation context) => exact.Prepare(context);
        internal override WarpBlockTerminator? LowerInstruction(WarpPortableWordInstructionContext context) => exact.LowerInstruction(context);
        internal override void AfterSourceLowering(WarpPortableWordBindingPreparation context) => exact.AfterSourceLowering(context);
        internal override string Complete(WarpPortableWordBindingCompletion context)
        {
            Completed = context;
            if (!duplicate) { context.AttachExceptionPlan(foreign); }
            string hash = exact.Complete(context);
            context.AttachExceptionPlan(exact.ExceptionPlan);
            return hash;
        }
    }
}
