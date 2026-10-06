using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this internal fixture through reflection; the unfiltered TRX records the executed methods.")]
internal sealed class WarpPortableInitializerProjectionTests
{
    [TestMethod]
    public void RootInvocationMapsActualZeroChargePreludeWithoutInventedSourceFields()
    {
        WarpPortableInitializerProjectionFixtures.Fixture fixture = Strict();
        WarpPortableSourceInitializerExecutableSite site = fixture.Projection.Sites.First(site => site.Origin.Kind == WarpPortableSourceOperationOriginKind.EntryInvocation);
        Assert.IsNull(site.Origin.SourceOffset); Assert.IsNull(site.Origin.SourceOpCode); Assert.IsNull(site.Origin.EffectIndex);
        Assert.IsEmpty(site.Origin.Effects); Assert.IsEmpty(site.Origin.ExceptionMemberships);
        WarpPortableWordBody body = fixture.Program.Bodies.First(body => body.Function == site.Function);
        Assert.AreSame(body.InvocationPrelude, site.Prelude);
        Assert.IsNotNull(site.Prelude);
        CollectionAssert.AreEquivalent(site.Prelude.GeneratedBlocks.ToArray(), site.Points.Select(point => point.Block).Distinct().ToArray());
        Assert.IsTrue(site.Points.All(point => point.InvocationPrelude && point.SourceOffset is null && point.SourceOpCode is null &&
            point.ChargedSourceSteps == 0 && point.SourceCost == 0 && point.ExceptionMemberships.IsEmpty));
        Assert.AreEqual(fixture.Projection.Layout.GetBlockEntry(body.Function, 0), site.EntryProgramCounter);
        Assert.IsTrue(body.SourceBlocks.All(source => source.Block != 0));
    }

    [TestMethod]
    public void TypedCapturedMethodIndexesAreIndependentFromFinalExecutableFunctions()
    {
        WarpPortableInitializerProjectionFixtures.Fixture fixture = Strict();
        Assert.IsTrue(fixture.Graph.Methods.Any(method => method.Intrinsic is not null));
        Assert.IsTrue(fixture.Projection.SourceSteps.Any(step => step.CapturedMethodIndex != step.Function), "The actual fixture must contain a captured/executable index mismatch.");
        foreach (WarpPortableSourceInitializerExecutableStep step in fixture.Projection.SourceSteps)
        {
            Assert.AreEqual(step.MethodIdentity, fixture.Typed.Methods[step.CapturedMethodIndex - 1].Identity, StringComparer.Ordinal);
            Assert.AreEqual(step.MethodIdentity, fixture.Program.Kernel.Functions[step.Function - 1].Name, StringComparer.Ordinal);
            Assert.AreEqual(step.Function, fixture.Projection.Layout.Nodes[step.EntryProgramCounter].Function);
        }
        foreach (WarpPortableSourceInitializerExecutableSite site in fixture.Projection.Sites)
        {
            Assert.AreEqual(site.Trigger.Initializer, fixture.Typed.Methods[site.InitializerCapturedMethodIndex - 1].Identity, StringComparer.Ordinal);
            Assert.AreEqual(site.Trigger.Initializer, fixture.Program.Kernel.Functions[site.InitializerFunction - 1].Name, StringComparer.Ordinal);
        }
    }

    [TestMethod]
    public void InstructionOriginsBindExactFinalNodesAndOrderedOriginalEffects()
    {
        WarpPortableInitializerProjectionFixtures.Fixture fixture = WarpPortableInitializerProjectionFixtures.Initializer(
            typeof(WarpPortableInitializerProjectionFixtures.Entries), nameof(WarpPortableInitializerProjectionFixtures.Entries.RelaxedRead));
        Assert.IsTrue(fixture.Projection.Sites.All(site => site.Origin.Kind == WarpPortableSourceOperationOriginKind.Instruction));
        foreach (WarpPortableSourceInitializerExecutableSite site in fixture.Projection.Sites)
        {
            Assert.IsNull(site.Prelude);
            WarpPortableSourceInitializerExecutableStep step = fixture.Projection.SourceSteps.First(step => step.Function == site.Function && step.SourceOffset == site.Origin.SourceOffset);
            Assert.AreEqual(step.SourceOpCode, site.Origin.SourceOpCode);
            Assert.IsNotNull(site.Origin.EffectIndex);
            Assert.AreEqual(WarpPortableTypedEffect.TypeInitialize, step.Instruction.Effects[site.Origin.EffectIndex.Value]);
            CollectionAssert.AreEqual(step.Instruction.Effects.ToArray(), site.Origin.Effects.ToArray());
            CollectionAssert.AreEqual(step.Instruction.ExceptionMemberships.ToArray(), site.Origin.ExceptionMemberships.ToArray());
            CollectionAssert.AreEqual(step.Points.ToArray(), site.Points.ToArray());
            Assert.AreEqual(1, site.Points.Sum(point => point.ChargedSourceSteps));
        }
        Assert.IsNotEmpty(fixture.Projection.Sites);
    }

    [TestMethod]
    public void EachOriginalCilStepHasOneEntryChargeAndNoSyntheticOrContinuationCharge()
    {
        WarpPortableInitializerProjectionFixtures.Fixture fixture = Strict();
        foreach (WarpPortableSourceInitializerExecutableStep step in fixture.Projection.SourceSteps)
        {
            Assert.AreEqual(1, step.Points.Sum(point => point.ChargedSourceSteps));
            WarpPortableSourceInitializerExecutablePoint entry = step.Points.First(point => point.ChargedSourceSteps == 1);
            Assert.AreEqual(step.EntryProgramCounter, entry.ProgramCounter); Assert.IsTrue(entry.StartsBlock);
            Assert.IsTrue(step.Points.Where(point => !point.StartsBlock || point.Block != step.Source.Block).All(point => point.ChargedSourceSteps == 0));
        }
        Assert.IsTrue(fixture.Projection.Points.Any(point => !point.StartsBlock && point.SourceCost == 1 && point.ChargedSourceSteps == 0));
        Assert.IsTrue(fixture.Projection.Points.Where(point => point.RuntimeHelper || point.InvocationPrelude || point.SourceOffset is null)
            .All(point => point.ChargedSourceSteps == 0));
        Assert.AreEqual(fixture.Projection.SourceSteps.Length, fixture.Projection.Points.Sum(point => point.ChargedSourceSteps));
    }

    [TestMethod]
    public void InvocationHelperClosureStopsAtActualGuestCctorAndGuardedDispatch()
    {
        WarpPortableInitializerProjectionFixtures.Fixture fixture = Strict();
        WarpPortableSourceInitializerExecutableSite site = fixture.Projection.Sites.First(site => site.Origin.Kind == WarpPortableSourceOperationOriginKind.EntryInvocation);
        WarpPortableSourceInitializerCandidate candidate = fixture.Projection.Describe(site);
        Assert.IsTrue(candidate.RequiresRuntimeReceipt);
        Assert.IsNotEmpty(candidate.HelperFunctions);
        Assert.IsTrue(candidate.HelperFunctions.All(function => fixture.Program.Kernel.Execution!.Bodies[function].RuntimeHelper));
        WarpPortableSourceInitializerFrontier guest = candidate.Frontiers.First(frontier => frontier.Kind == WarpPortableSourceInitializerFrontierKind.InitializerCall);
        Assert.AreEqual(site.InitializerFunction, guest.TargetFunction);
        Assert.AreEqual(site.Trigger.Initializer, guest.TargetMethodIdentity, StringComparer.Ordinal);
        Assert.IsNotNull(guest.FirstGuestProgramCounter);
        Assert.DoesNotContain(guest.FirstGuestProgramCounter.Value, candidate.ProgramCounters);
        Assert.IsTrue(candidate.Frontiers.Any(frontier => frontier.Kind == WarpPortableSourceInitializerFrontierKind.GuardedStateDispatch));
        Assert.IsTrue(candidate.Frontiers.All(frontier => frontier.RequiresRuntimeReceipt));
    }

    [TestMethod]
    public void ProjectionBindsExactCompilerObjectAndRejectsClonedProgramAndForeignRows()
    {
        WarpPortableInitializerProjectionFixtures.Fixture fixture = Strict();
        fixture.Projection.Validate(fixture.Graph, fixture.Schema, fixture.Program);
        WarpVerificationException clone = Assert.ThrowsExactly<WarpVerificationException>(() =>
            fixture.Projection.Validate(fixture.Graph, fixture.Schema, fixture.Program with { }));
        Assert.AreEqual("WRPCLR2500", clone.Code, StringComparer.Ordinal);
        WarpPortableSourceInitializerExecutableSite site = fixture.Projection.Sites[0];
        Assert.ThrowsExactly<WarpVerificationException>(() => fixture.Projection.Describe(site with { }));
        WarpPortableSourceInitializerExecutableStep step = fixture.Projection.SourceSteps[0];
        Assert.ThrowsExactly<WarpVerificationException>(() => fixture.Projection.Describe(step with { }));
        WarpVerificationException modified = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableSourceInitializerExecutableProjection.Capture(fixture.Graph, fixture.Schema, fixture.Program with { MapsHash = new string('0', 64) }));
        Assert.AreEqual("WRPCLR2350", modified.Code, StringComparer.Ordinal);
    }

    [TestMethod]
    public void ProjectionAndCandidateIdentitiesAreDeterministicForTheSameFinalProgram()
    {
        WarpPortableInitializerProjectionFixtures.Fixture fixture = Strict();
        WarpPortableSourceInitializerExecutableProjection second = WarpPortableSourceInitializerExecutableProjection.Capture(fixture.Graph, fixture.Schema, fixture.Program);
        Assert.AreEqual(fixture.Projection.ProjectionHash, second.ProjectionHash, StringComparer.Ordinal);
        Assert.AreEqual(fixture.Projection.Describe(fixture.Projection.Sites[0]).CandidateHash, second.Describe(second.Sites[0]).CandidateHash, StringComparer.Ordinal);
        Assert.AreEqual(fixture.Program.CompilerIdentity!.KernelIrHash, fixture.Projection.ProgramIdentity.KernelIrHash, StringComparer.Ordinal);
    }

    [TestMethod]
    [DataRow(0, null, null, null)]
    [DataRow(3, null, null, null)]
    [DataRow(1, null, 0, 0)]
    [DataRow(1, 0, null, 0)]
    [DataRow(1, 0, 0, null)]
    [DataRow(1, -1, 0, 0)]
    [DataRow(1, 0, 0, -1)]
    [DataRow(2, 0, null, null)]
    [DataRow(2, null, 0, null)]
    [DataRow(2, null, null, 0)]
    public void ZeroUnknownOrPartialOriginShapesAreRejected(int kind, int? offset, int? opcode, int? effect)
    {
        WarpVerificationException invalid = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableSourceInitializerExecutableProjection.RequireOriginShape((WarpPortableSourceOperationOriginKind)kind, offset,
                opcode is { } value ? checked((ushort)value) : null, effect));
        Assert.AreEqual("WRPCLR2500", invalid.Code, StringComparer.Ordinal);
    }

    [TestMethod]
    public void RealInstructionAndInvocationOriginShapesRemainDistinct()
    {
        WarpPortableSourceInitializerExecutableProjection.RequireOriginShape(WarpPortableSourceOperationOriginKind.Instruction, 0, 0, 0);
        WarpPortableSourceInitializerExecutableProjection.RequireOriginShape(WarpPortableSourceOperationOriginKind.EntryInvocation, null, null, null);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RealConstructorCallStopsHelperTraversalBeforeGuestFrameAndExecutesCompiledCil(bool returnedValue)
    {
        WarpPortableInitializerProjectionFixtures.Fixture fixture = WarpPortableInitializerProjectionFixtures.Constructor(returnedValue);
        string target = fixture.Graph.Methods.First(method => method.SourceMethod is ConstructorInfo { IsStatic: false } && method.Intrinsic is null).Identity;
        WarpPortableSourceInitializerExecutableStep source = fixture.Projection.SourceSteps.First(step => fixture.Graph.Methods.First(method =>
            string.Equals(method.Identity, step.MethodIdentity, StringComparison.Ordinal)).Instructions.Any(instruction => instruction.Offset == step.SourceOffset &&
                string.Equals(instruction.Method, target, StringComparison.Ordinal)));
        Assert.AreEqual(unchecked((ushort)(returnedValue ? OpCodes.Newobj.Value : OpCodes.Call.Value)), source.SourceOpCode);
        WarpPortableSourceInitializerCandidate candidate = fixture.Projection.Describe(source);
        WarpPortableSourceInitializerFrontier ctor = candidate.Frontiers.First(frontier => frontier.Kind == WarpPortableSourceInitializerFrontierKind.ConstructorCall);
        Assert.IsNotNull(ctor.TargetFunction); Assert.IsNotNull(ctor.FirstGuestProgramCounter);
        Assert.DoesNotContain(ctor.FirstGuestProgramCounter.Value, candidate.ProgramCounters);
        Assert.IsTrue(fixture.Graph.Methods.First(method => string.Equals(method.Identity, ctor.TargetMethodIdentity, StringComparison.Ordinal)).SourceMethod is ConstructorInfo { IsStatic: false });
        foreach (uint input in new uint[] { 0, 17, uint.MaxValue })
        {
            (uint result, uint[] state) = WarpPortableInitializerProjectionExecution.Run(fixture, [input]);
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(returnedValue ? input : unchecked(input + 5), result);
            if (returnedValue)
            {
                Assert.AreEqual(5u, state[fixture.Projection.Layout.GetResultWordOffset(1, WarpPortableInitializerProjectionExecution.Depth)]);
                Assert.AreEqual(WarpPortableInitializerProjectionFixtures.Entries.MakePair(input).First, result);
            }
            else { Assert.AreEqual(WarpPortableInitializerProjectionFixtures.Entries.Construct(input), result); }
        }
    }

    [TestMethod]
    public void FinalFilterAliasesAndHandlersRetainOriginalMembershipsAndGuardedGuestEnds()
    {
        WarpPortableInitializerProjectionFixtures.Fixture fixture = WarpPortableInitializerProjectionFixtures.Filter();
        Assert.IsEmpty(fixture.Projection.Sites);
        Assert.IsNotNull(CoreCLRResumableKernel.Compile(fixture.Projection.Layout));
        WarpPortableWordBody alias = fixture.Program.Bodies.First(body => body.AliasOwnerFunction != -1);
        Assert.IsFalse(fixture.Program.Kernel.Execution!.Bodies[alias.Function].CountsSourceDepth);
        Assert.IsTrue(fixture.Projection.SourceSteps.Where(step => step.Function == alias.Function).All(step => step.Alias && step.OriginalFunction == alias.AliasOwnerFunction &&
            step.Instruction.ExceptionMemberships.Any(membership => membership.Role == WarpPortableExceptionRole.Filter) && step.Points.Sum(point => point.ChargedSourceSteps) == 1));
        WarpPortableSourceInitializerExecutableStep throwing = fixture.Projection.SourceSteps.First(step => step.SourceOpCode == unchecked((ushort)OpCodes.Throw.Value));
        WarpPortableSourceInitializerCandidate candidate = fixture.Projection.Describe(throwing);
        WarpPortableSourceInitializerFrontier filter = candidate.Frontiers.First(frontier => frontier.Kind == WarpPortableSourceInitializerFrontierKind.GuardedStateDispatch && frontier.TargetFunction == alias.Function);
        Assert.IsNotNull(filter.FirstGuestProgramCounter);
        Assert.DoesNotContain(filter.FirstGuestProgramCounter.Value, candidate.ProgramCounters);
        Assert.IsTrue(fixture.Projection.Points[filter.FirstGuestProgramCounter.Value].ExceptionMemberships.Any(membership => membership.Role == WarpPortableExceptionRole.Filter));
        Assert.IsTrue(candidate.Frontiers.Any(frontier => frontier.Kind == WarpPortableSourceInitializerFrontierKind.GuardedStateDispatch && frontier.TargetProgramCounter is { } pc &&
            fixture.Projection.Points[pc].ExceptionMemberships.Any(membership => membership.Role == WarpPortableExceptionRole.Catch)));
        Assert.IsTrue(candidate.Frontiers.All(frontier => frontier.RequiresRuntimeReceipt));
    }

    [TestMethod]
    public void ActualSourceBoundaryPcMatchesProjectionBeforeFirstChargedGuestCctorStep()
    {
        WarpPortableInitializerProjectionFixtures.Fixture fixture = Strict();
        uint[] arena = fixture.Schema.CreateArena(1701, 4096, 16, 16, 1, 4096);
        (_, uint[] state) = WarpPortableInitializerProjectionExecution.Run(fixture, [], arena, stopBeforeSource: true);
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(WarpLogicalMachineLayout.BeforeSourceBoundary, state[WarpLogicalMachineLayout.SourceBoundaryStateOffset]);
        int top = WarpLogicalMachineLayout.HeaderWords + (checked((int)state[WarpLogicalMachineLayout.DepthOffset]) - 1) * fixture.Projection.Layout.FrameWords;
        int pc = checked((int)state[top + WarpLogicalMachineLayout.FrameProgramCounterOffset]);
        int function = checked((int)state[top + WarpLogicalMachineLayout.FrameFunctionOffset]);
        WarpPortableSourceInitializerExecutablePoint point = fixture.Projection.Points[pc];
        Assert.AreEqual(function, point.Function); Assert.AreEqual(1, point.ChargedSourceSteps);
        WarpPortableSourceInitializerExecutableSite entry = fixture.Projection.Sites.First(site => site.Origin.Kind == WarpPortableSourceOperationOriginKind.EntryInvocation);
        Assert.AreEqual(entry.InitializerFunction, function);
        Assert.AreEqual(fixture.Projection.Describe(entry).Frontiers.First(frontier => frontier.Kind == WarpPortableSourceInitializerFrontierKind.InitializerCall).FirstGuestProgramCounter, pc);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LeaseState]);
    }

    [TestMethod]
    public void UnboundProtectedInitializerExecutionRemainsRejectedBeforeProjection()
    {
        MethodInfo method = typeof(WarpPortableInitializerProjectionFixtures.ProtectedInitializer).GetMethod(nameof(WarpPortableInitializerProjectionFixtures.ProtectedInitializer.Read))!;
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(method);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpVerificationException denied = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableClosedInitializerSourcePlan.Capture(graph, typed, WarpPortableSourceHeapSchema.Create(graph, typed)));
        Assert.AreEqual("WRPCLR2480", denied.Code, StringComparer.Ordinal);
    }

    [TestMethod]
    public void GeneratedInitializerStillExecutesOriginalCilWithoutSourceFallback()
    {
        WarpPortableInitializerProjectionFixtures.Fixture fixture = Strict();
        uint[] arena = fixture.Schema.CreateArena(1702, 4096, 16, 16, 1, 4096);
        for (int invocation = 0; invocation < 2; invocation++)
        {
            (uint value, uint[] state) = WarpPortableInitializerProjectionExecution.Run(fixture, [], arena);
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]); Assert.AreEqual(42u, value);
        }
        Assert.AreEqual(42u, WarpPortableInitializerProjectionFixtures.Strict.Read());
    }


    private static WarpPortableInitializerProjectionFixtures.Fixture Strict() => WarpPortableInitializerProjectionFixtures.Initializer(
        typeof(WarpPortableInitializerProjectionFixtures.Strict), nameof(WarpPortableInitializerProjectionFixtures.Strict.Read));
}
