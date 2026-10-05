using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableSourceProgramIdentityCases
{
    internal static void ValidatedProgramIdentityBindsTheFullSourceAndGeneratedKernel()
    {
        Fixture fixture = Capture(false);
        WarpPortableWordProgramIdentity identity = WarpPortableWordProgramIdentity.Validate(fixture.Graph, fixture.Schema, fixture.Lowered);
        Assert.AreSame(fixture.Lowered.CompilerIdentity, identity);
        Assert.AreEqual(fixture.Graph.GraphHash, identity.GraphHash, StringComparer.Ordinal);
        Assert.AreEqual(fixture.Typed.VerifiedHash, identity.VerifiedHash, StringComparer.Ordinal);
        Assert.AreEqual(fixture.Schema.SchemaHash, identity.TypeSchemaHash, StringComparer.Ordinal);
        Assert.AreEqual(fixture.Lowered.MapsHash, identity.MapsHash, StringComparer.Ordinal);
        Assert.AreEqual(WarpIrHash.Compute(fixture.Lowered.Kernel), identity.KernelIrHash, StringComparer.Ordinal);
        Assert.AreEqual(identity.IdentityHash, WarpPortableWordProgramIdentity.Validate(fixture.Graph, fixture.Schema, fixture.Lowered).IdentityHash, StringComparer.Ordinal);
        Assert.IsNull(identity.CliContractHash); Assert.AreEqual(0, identity.NativeEvaluationBits);
        foreach (uint input in new uint[] { 0, 1, 0x80000000, uint.MaxValue })
        {
            Assert.AreEqual((ulong)input, Execute(fixture.Lowered, input));
        }
    }

    internal static void ValidatedNativeIdentityRetainsItsExactCliWidthContract()
    {
        Fixture fixture = Capture(true);
        WarpPortableWordProgramIdentity identity = WarpPortableWordProgramIdentity.Validate(fixture.Graph, fixture.Schema, fixture.Lowered);
        Assert.AreEqual(64, identity.NativeEvaluationBits);
        Assert.AreEqual(fixture.Typed.CliSizes!.ContractHash, identity.CliContractHash, StringComparer.Ordinal);
        Assert.IsTrue(identity.RequiredServices.Contains(WarpPortableCliNativeInteger.Semantics + "/" + identity.CliContractHash, StringComparer.Ordinal));
        WarpVerificationException missing = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableTypedProgram.Verify(fixture.Graph));
        Assert.AreEqual("WRPCLR2210", missing.Code, StringComparer.Ordinal);
        foreach (uint input in new uint[] { 0, 1, 0x80000000, uint.MaxValue }) { Assert.AreEqual((ulong)input, Execute(fixture.Lowered, input)); }
    }

    internal static void CallerCannotReplaceKernelHashesMapsServicesOrTheCompletedPlan()
    {
        Fixture fixture = Capture(false); WarpPortableWordLoweredProgram lowered = fixture.Lowered;
        var relabeled = new WarpControlFlowKernel("caller-label", lowered.Kernel.InputBufferCount, lowered.Kernel.ScalarArgumentCount,
            lowered.Kernel.Blocks, lowered.Kernel.Reduction, lowered.Kernel.Functions, lowered.Kernel.Execution);
        foreach (WarpPortableWordLoweredProgram changed in new[]
        {
            lowered with { Kernel = relabeled }, lowered with { LoweredHash = new string('A', 64) },
            lowered with { MapsHash = new string('B', 64) }, lowered with { RequiredServices = lowered.RequiredServices.Add("invented-service") },
            lowered with { ExecutionPlanHash = new string('C', 64) },
        })
        {
            AssertRejected(fixture, changed);
        }
        var manual = new WarpPortableWordLoweredProgram(lowered.VerifiedProgram, lowered.LoweredHash, lowered.MapsHash, lowered.Kernel,
            lowered.Bodies, lowered.RequiredServices, lowered.EntryProjection);
        AssertRejected(fixture, manual);
    }

    internal static void ChangedOriginalInstructionAndRootMapsHavePreciseDiagnostics()
    {
        Fixture fixture = Capture(false); WarpPortableWordBody body = fixture.Lowered.Bodies[0]; WarpPortableWordSourceBlock source = body.SourceBlocks[0];
        WarpPortableWordSourceBlock opcode = source with { Instruction = source.Instruction with { OpCode = OpCodes.Nop.Value } };
        WarpPortableWordSourceBlock roots = source with { Roots = [new(0, new("stack", 0, 0, false, []))] };
        foreach (WarpPortableWordSourceBlock changed in new[] { opcode, roots })
        {
            WarpPortableWordBody replacement = body with { SourceBlocks = body.SourceBlocks.SetItem(0, changed) };
            WarpVerificationException error = AssertRejected(fixture, fixture.Lowered with { Bodies = [replacement] });
            Assert.AreEqual(source.Instruction.Offset, error.IlOffset);
        }
        WarpPortableWordBody storage = body with { EvaluationWordOffset = body.EvaluationWordOffset + 1 };
        AssertRejected(fixture, fixture.Lowered with { Bodies = [storage] });
    }

    internal static void IdentityCannotCrossClosuresSchemasOrTypedSnapshots()
    {
        Fixture fixture = Capture(false); Fixture other = Capture(false);
        WarpVerificationException graph = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableWordProgramIdentity.Validate(other.Graph, fixture.Schema, fixture.Lowered));
        Assert.AreEqual("WRPCLR2350", graph.Code, StringComparer.Ordinal);
        WarpVerificationException schema = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableWordProgramIdentity.Validate(fixture.Graph, other.Schema, fixture.Lowered));
        Assert.AreEqual("WRPCLR2350", schema.Code, StringComparer.Ordinal);
        AssertRejected(fixture, fixture.Lowered with { VerifiedProgram = other.Typed });
        AssertRejected(fixture, fixture.Lowered with { EntryProjection = fixture.Lowered.EntryProjection with { WrapperInputRoots = [new(0, false, [])] } });
    }

    private static WarpVerificationException AssertRejected(Fixture fixture, WarpPortableWordLoweredProgram program)
    {
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordProgramIdentity.Validate(fixture.Graph, fixture.Schema, program));
        Assert.AreEqual("WRPCLR2350", error.Code, StringComparer.Ordinal); return error;
    }
    private static Fixture Capture(bool native)
    {
        TypeBuilder type = AssemblyBuilder.DefineDynamicAssembly(new("WarpProgramIdentity"), AssemblyBuilderAccess.RunAndCollect)
            .DefineDynamicModule("source").DefineType("Source", TypeAttributes.Public | TypeAttributes.Sealed);
        MethodBuilder method = type.DefineMethod("Source", MethodAttributes.Public | MethodAttributes.Static, typeof(ulong), [typeof(uint)]);
        ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); if (native) { il.Emit(OpCodes.Conv_U); }
        il.Emit(OpCodes.Conv_U8); il.Emit(OpCodes.Ret);
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(type.CreateType()!.GetMethod("Source")!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph, native ? WarpPortableCliSizeContract.Capture(graph) : null);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        return new(graph, typed, schema, WarpPortableWordLowerer.Lower(graph, typed));
    }
    private static ulong Execute(WarpPortableWordLoweredProgram lowered, uint input)
    {
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(new WarpLogicalMachineLayout(lowered.Kernel));
        uint[] state = compiled.Layout.CreateInitialState(64, 1000000); int quanta = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && quanta++ < 10000)
        {
            compiled.ExecuteQuantum([[input]], [], 0, state, 64, compiled.Layout.MaximumBlockCost);
        }
        Assert.IsLessThan(10000, quanta); Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        int result = compiled.Layout.GetResultWordOffset(0, 64); return state[result] | ((ulong)state[result + 1] << 32);
    }
    private sealed record Fixture(WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed,
        WarpPortableSourceHeapSchema Schema, WarpPortableWordLoweredProgram Lowered);
}
