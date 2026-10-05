using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableWordExecutionBindingCases
{
    internal static void BoundGeneratedContinuationsPreserveValuesAndOriginalSourceCharges()
    {
        Fixture fixture = Capture(); var binding = new NopBinding(fixture);
        WarpPortableWordLoweredProgram lowered = WarpPortableWordLowerer.Lower(fixture.Graph, fixture.Typed, fixture.Schema, binding);
        var layout = new WarpLogicalMachineLayout(lowered.Kernel); CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        foreach (uint input in new uint[] { 0, 1, 0x80000000, uint.MaxValue })
        {
            uint[] state = layout.CreateInitialState(1, 4); int quanta = Execute(compiled, state, input);
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(input, state[layout.GetResultWordOffset(0, 1)]); Assert.AreEqual(0u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
            Assert.IsGreaterThan(1, quanta);
            uint[] limited = layout.CreateInitialState(1, 3); Execute(compiled, limited, input);
            Assert.AreEqual(WarpLogicalMachineLayout.Faulted, limited[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(WarpLogicalMachineLayout.StepLimitFault, limited[WarpLogicalMachineLayout.FaultKindOffset]);
        }
        Assert.HasCount(1, lowered.Bodies); WarpPortableWordBody source = lowered.Bodies.First();
        Assert.AreEqual(1, source.StoragePrefixWords); Assert.AreEqual(1, source.MaximumStackWords);
        foreach (WarpPortableWordSourceBlock block in source.SourceBlocks.Where(block => block.Instruction.OpCode == OpCodes.Nop.Value))
        {
            Assert.HasCount(2, block.GeneratedBlocks); Assert.AreEqual(block.Block, block.GeneratedBlocks[0]);
            Assert.AreEqual(1, lowered.Kernel.Execution!.Bodies[source.Function].SourceBlockCosts[block.Block]);
            Assert.AreEqual(0, lowered.Kernel.Execution.Bodies[source.Function].SourceBlockCosts[block.GeneratedBlocks[1]]);
        }
        Assert.IsTrue(lowered.Kernel.Execution!.Bodies.Skip(2).All(body => body.RuntimeHelper && body.SourceBlockCosts.All(cost => cost == 0)));
        Assert.IsTrue(lowered.RequiredServices.Any(service => service.Contains(WarpPortableWordExecutionBinding.Version, StringComparison.Ordinal)));
    }

    internal static void PlanProjectionAvoidsIdentityHashCyclesAndStillBindsTheFinalIr()
    {
        Fixture fixture = Capture(); var binding = new NopBinding(fixture);
        WarpPortableWordLoweredProgram lowered = WarpPortableWordLowerer.Lower(fixture.Graph, fixture.Typed, fixture.Schema, binding);
        Assert.AreEqual(Projection(lowered.Kernel), lowered.ExecutionPlanHash, StringComparer.Ordinal);
        Assert.AreEqual(binding.BindingHash, lowered.ExecutionBindingHash, StringComparer.Ordinal);
        Assert.IsNotNull(binding.Completed); Assert.AreEqual(lowered.MapsHash, binding.Completed.MapsHash, StringComparer.Ordinal);
        Assert.AreNotEqual(WarpIrHash.Compute(binding.Completed.StructuralKernel), WarpIrHash.Compute(lowered.Kernel), StringComparer.Ordinal);
        Assert.AreEqual(Projection(binding.Completed.StructuralKernel), Projection(lowered.Kernel), StringComparer.Ordinal);
        Assert.AreNotEqual(WarpPortableWordLowerer.Lower(fixture.Graph, fixture.Typed).LoweredHash, lowered.LoweredHash, StringComparer.Ordinal);
        WarpPortableWordProgramIdentity identity = WarpPortableWordProgramIdentity.Validate(fixture.Graph, fixture.Schema, lowered);
        Assert.AreEqual(binding.BindingHash, identity.ExecutionBindingHash, StringComparer.Ordinal);
        Assert.AreEqual(lowered.ExecutionPlanHash, identity.ExecutionPlanHash, StringComparer.Ordinal);
        Assert.AreEqual(WarpPortableWordProgramIdentity.ComputeLayoutProjection(binding.Completed.StructuralKernel), identity.StructuralLayoutHash, StringComparer.Ordinal);
        Assert.AreEqual(WarpIrHash.Compute(lowered.Kernel), identity.KernelIrHash, StringComparer.Ordinal);
    }

    internal static void EmissionCapabilitiesCloseBeforeExecutionAndRejectOtherClosures()
    {
        Fixture fixture = Capture(); var binding = new NopBinding(fixture);
        WarpPortableWordLowerer.Lower(fixture.Graph, fixture.Typed, fixture.Schema, binding);
        Assert.IsNotNull(binding.LastInstruction); Assert.IsNotNull(binding.Prepared);
        Assert.ThrowsExactly<InvalidOperationException>(() => binding.LastInstruction.LoadPrivateWord(0));
        Assert.ThrowsExactly<InvalidOperationException>(() => binding.Prepared.ReserveFunction("late-runtime-body"));
        Fixture other = Capture();
        WarpVerificationException mismatch = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableWordLowerer.Lower(other.Graph, other.Typed, other.Schema, binding));
        Assert.AreEqual("WRPCLR2300", mismatch.Code, StringComparer.Ordinal); Assert.AreEqual(0, mismatch.IlOffset);
    }

    internal static void ABindingCannotReplaceMissingThrowOrNumericFaultSemantics()
    {
        foreach (bool numeric in new[] { false, true })
        {
            Fixture fixture = CaptureUnsupported(numeric); var binding = new NopBinding(fixture);
            WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() =>
                WarpPortableWordLowerer.Lower(fixture.Graph, fixture.Typed, fixture.Schema, binding));
            Assert.AreEqual("WRPCLR2300", error.Code, StringComparer.Ordinal); Assert.AreEqual(numeric ? 2 : 1, error.IlOffset);
        }
    }

    internal static void UnhandledBindingsCannotLeavePartiallyEmittedInstructions()
    {
        Fixture fixture = Capture(); var binding = new PartialBinding(fixture);
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableWordLowerer.Lower(fixture.Graph, fixture.Typed, fixture.Schema, binding));
        Assert.AreEqual("WRPCLR2300", error.Code, StringComparer.Ordinal); Assert.AreEqual(0, error.IlOffset);
        Assert.IsTrue(error.Message.Contains("partially emitted", StringComparison.Ordinal));
    }

    internal static void FrameViewsRejectAnAliasOwnerDifferentFromTheRootMaps()
    {
        Fixture fixture = Capture(); WarpPortableWordLoweredProgram lowered = WarpPortableWordLowerer.Lower(fixture.Graph, fixture.Typed);
        WarpPortableWordBody changed = lowered.Bodies.First() with { AliasOwnerFunction = 1, AliasPrefixWords = 1 };
        Assert.AreNotEqual(lowered.MapsHash, WarpPortableWordMapHash.Compute([changed], lowered.EntryProjection), StringComparer.Ordinal);
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableSourceFrameSchema.Create(fixture.Schema, lowered with { Bodies = [changed] }));
        Assert.AreEqual("WRPCLR2400", error.Code, StringComparer.Ordinal);
    }

    private static int Execute(CoreCLRResumableKernel compiled, uint[] state, uint input)
    {
        int quanta = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && quanta < 10000)
        {
            compiled.ExecuteQuantum([[input]], [], 0, state, 1, compiled.Layout.MaximumBlockCost); quanta++;
        }
        Assert.IsLessThan(10000, quanta); return quanta;
    }

    private static Fixture Capture()
    {
        TypeBuilder source = NewSource(); MethodBuilder method = source.DefineMethod("Source", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Nop); il.Emit(OpCodes.Nop); il.Emit(OpCodes.Ret);
        return Capture(source.CreateType()!.GetMethod("Source")!);
    }

    private static Fixture CaptureUnsupported(bool numeric)
    {
        TypeBuilder source = NewSource(); MethodBuilder method = source.DefineMethod("Source", MethodAttributes.Public | MethodAttributes.Static,
            numeric ? typeof(int) : typeof(void), numeric ? [typeof(int)] : [typeof(Exception)]);
        ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0);
        if (numeric) { il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add_Ovf); il.Emit(OpCodes.Ret); }
        else { il.Emit(OpCodes.Throw); }
        return Capture(source.CreateType()!.GetMethod("Source")!);
    }

    private static TypeBuilder NewSource() => AssemblyBuilder.DefineDynamicAssembly(new("WarpBoundSource"), AssemblyBuilderAccess.RunAndCollect)
        .DefineDynamicModule("source").DefineType("Source", TypeAttributes.Public | TypeAttributes.Sealed);

    private static Fixture Capture(MethodInfo source)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source); WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        return new(graph, typed, WarpPortableSourceHeapSchema.Create(graph, typed));
    }

    private static string Projection(WarpControlFlowKernel kernel) => WarpIrHash.Compute(new WarpControlFlowKernel("warp.layout-projection/identity-label-only-excluded/0.1",
        kernel.InputBufferCount, kernel.ScalarArgumentCount, kernel.Blocks, kernel.Reduction, kernel.Functions, kernel.Execution));

    private sealed record Fixture(WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed, WarpPortableSourceHeapSchema Schema);

    private sealed class NopBinding : WarpPortableWordExecutionBinding
    {
        private int helper;
        internal NopBinding(Fixture fixture) : base(fixture.Graph.GraphHash, fixture.Typed.VerifiedHash, fixture.Schema.SchemaHash,
            "warp.binding-test/nop-fixed-continuation/0.1", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fixture.Typed.VerifiedHash))), new()) { }
        internal WarpPortableWordInstructionContext? LastInstruction { get; private set; }
        internal WarpPortableWordBindingPreparation? Prepared { get; private set; }
        internal WarpPortableWordBindingCompletion? Completed { get; private set; }
        internal override void Prepare(WarpPortableWordBindingPreparation context)
        {
            Prepared = context;
            helper = context.Services.Import(typeof(WarpPortableInteger32).GetMethod(nameof(WarpPortableInteger32.ExtendSigned8))!,
                "warp.binding-test/signed8-word-helper/0.1", WarpPortableGeneratedServiceKind.Words).Function;
        }
        internal override WarpBlockTerminator? LowerInstruction(WarpPortableWordInstructionContext context)
        {
            if (context.SourceInstruction.OpCode != OpCodes.Nop) { return null; }
            LastInstruction = context;
            int generated = context.ReserveGeneratedBlock();
            context.Call(helper, [context.Constant(0)]);
            context.EmitGeneratedBlock(generated, continuation => continuation.Next());
            return new WarpBranchTerminator(new(generated, []));
        }
        internal override string Complete(WarpPortableWordBindingCompletion context)
        {
            Completed = context; return Projection(context.StructuralKernel);
        }
    }

    private sealed class PartialBinding(Fixture fixture) : WarpPortableWordExecutionBinding(fixture.Graph.GraphHash, fixture.Typed.VerifiedHash,
        fixture.Schema.SchemaHash, "warp.binding-test/partial-negative/0.1", fixture.Typed.VerifiedHash, new())
    {
        internal override WarpBlockTerminator? LowerInstruction(WarpPortableWordInstructionContext context) { context.Constant(0); return null; }
        internal override string Complete(WarpPortableWordBindingCompletion context) => Projection(context.StructuralKernel);
    }
}
