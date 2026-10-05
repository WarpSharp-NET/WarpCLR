using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpPortableWordLoweringTests
{
    [TestMethod]
    public void SignedSmallArgumentsPromoteBeforeFullI4Arithmetic()
    {
        WarpPortableWordLoweredProgram program = Lower(nameof(Kernels.Small));
        Assert.AreEqual(8, program.Bodies[0].Arguments[0].Type.StorageBits);
        Assert.IsTrue(program.Bodies[0].Arguments[0].Type.IsSigned);
        CollectionAssert.AreEqual(new uint[] { 255 }, Execute(program, [255]));
    }

    [TestMethod]
    public void BooleanStorageTruncatesToByteWithoutNormalizingItsBitPattern()
    {
        MethodInfo source = Dynamic("Boolean", typeof(bool), [typeof(bool)], static il =>
        {
            il.Emit(OpCodes.Ldc_I4, 258); il.Emit(OpCodes.Starg_S, (byte)0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        });
        CollectionAssert.AreEqual(new uint[] { 2 }, Execute(Lower(source), [0]));
    }

    [TestMethod]
    public void ConditionalLoopsAndSwitchesExecuteGeneratedBranches()
    {
        CollectionAssert.AreEqual(new uint[] { 21 }, Execute(Lower(nameof(Kernels.Loop)), [6]));
        WarpPortableWordLoweredProgram choice = Lower(nameof(Kernels.Choose));
        CollectionAssert.AreEqual(new uint[] { 11 }, Execute(choice, [0]));
        CollectionAssert.AreEqual(new uint[] { 29 }, Execute(choice, [2]));
        CollectionAssert.AreEqual(new uint[] { 41 }, Execute(choice, [8]));
        Assert.IsGreaterThan(choice.Bodies[0].SourceBlocks.Length + 1, choice.Kernel.Functions[0].Blocks.Count);
    }

    [TestMethod]
    public void DirectAndMutualRecursionUseSeparatePrivateFrames()
    {
        CollectionAssert.AreEqual(new uint[] { 21 }, Execute(Lower(nameof(Kernels.Recursive)), [6]));
        CollectionAssert.AreEqual(new uint[] { 1 }, Execute(Lower(nameof(Kernels.Even)), [10]));
        CollectionAssert.AreEqual(new uint[] { 0 }, Execute(Lower(nameof(Kernels.Even)), [11]));
    }

    [TestMethod]
    public void WideIntegerArithmeticCarriesAcrossWordBoundaries()
    {
        CollectionAssert.AreEqual(new uint[] { 0, 2 }, Execute(Lower(nameof(Kernels.Wide)), [uint.MaxValue, 1, 1, 0]));
    }

    [TestMethod]
    public void MixedBinary32AndBinary64UseCompiledWordArithmeticServices()
    {
        WarpPortableWordLoweredProgram single = Lower(nameof(Kernels.Single));
        CollectionAssert.AreEqual(new uint[] { 0x40A00000 }, Execute(single, [0x3F800000, 0x40000000]));
        WarpPortableWordLoweredProgram wide = Lower(nameof(Kernels.Double));
        CollectionAssert.AreEqual(new uint[] { 0, 0x40140000 }, Execute(wide, [0, 0x3FF00000, 0, 0x40000000]));
        Assert.IsTrue(single.RequiredServices.Any(identity => identity.Contains(WarpPortableBinary32.Semantics, StringComparison.Ordinal)));
        Assert.IsTrue(wide.RequiredServices.Any(identity => identity.Contains(WarpPortableBinary64.Semantics, StringComparison.Ordinal)));
        Assert.IsTrue(single.Kernel.Execution!.Bodies.Skip(2).All(body => body.RuntimeHelper));
    }

    [TestMethod]
    public void TupleConstructorAndMultiwordResultPreservePackedWidthsAndReferenceRoots()
    {
        WarpPortableWordLoweredProgram program = Lower(nameof(Kernels.Tuple));
        uint[] result = Execute(program, [255, 2, 0x3F800000, 17, 4, 29]);
        WarpPortableTypedType tuple = program.VerifiedProgram.Types.First(type => type.Category == WarpPortableStackCategory.Value && type.Fields.Length == 4);
        WarpPortableTypedField[] fields = tuple.Fields.Where(field => !field.IsStatic).ToArray();
        Assert.AreEqual((byte)255, ReadByte(result, fields[0].ByteOffset));
        Assert.AreEqual((byte)2, ReadByte(result, fields[1].ByteOffset));
        Assert.AreEqual(0x3F800000u, result[fields[2].ByteOffset / 4]);
        CollectionAssert.AreEqual(new uint[] { 17, 4, 29 }, result.Skip(fields[3].ByteOffset / 4).Take(3).ToArray());
        Assert.Contains(fields[3].ByteOffset, tuple.ManagedRootByteOffsets);
    }

    [TestMethod]
    public void VoidSourceCallsAndClosedGenericCallsKeepTheirExactSignatures()
    {
        CollectionAssert.AreEqual(new uint[] { 43 }, Execute(Lower(nameof(Kernels.Generic)), [43]));
        Assert.IsTrue(Lower(nameof(Kernels.Generic)).Kernel.Functions.Any(function => function.ResultWordCount == 0));
    }

    [TestMethod]
    public void ManagedReferenceCopiesAndIdentityComparisonsRetainAllThreeWords()
    {
        CollectionAssert.AreEqual(new uint[] { 17, 4, 29 }, Execute(Lower(nameof(Kernels.Reference)), [17, 4, 29]));
        CollectionAssert.AreEqual(new uint[] { 1 }, Execute(Lower(nameof(Kernels.ReferenceEqual)), [17, 4, 29, 17, 4, 29]));
        CollectionAssert.AreEqual(new uint[] { 0 }, Execute(Lower(nameof(Kernels.ReferenceEqual)), [17, 4, 29, 17, 4, 30]));
    }

    [TestMethod]
    public void AdjacentUnsignedToSingleCastAvoidsDoubleRoundingAndPreservesBothSourceMaps()
    {
        WarpPortableWordLoweredProgram program = Lower(nameof(Kernels.UnsignedSingle));
        CollectionAssert.AreEqual(new uint[] { 0x5F000001 }, Execute(program, [1, 0x80000080]));
        Assert.IsTrue(program.RequiredServices.Any(identity => identity.Contains("unsigned-single-adjacent", StringComparison.Ordinal)));
        WarpPortableWordBody source = program.Bodies[0];
        Assert.AreEqual(program.VerifiedProgram.Methods.First(method => string.Equals(method.Identity, source.MethodIdentity, StringComparison.Ordinal)).PrivateStorageWords + 2, source.PrivateWordCount);
        Assert.IsTrue(source.SourceBlocks.Any(block => block.Instruction.OpCode == OpCodes.Conv_R_Un.Value));
        Assert.IsTrue(source.SourceBlocks.Any(block => block.Instruction.OpCode == OpCodes.Conv_R4.Value));
    }

    [TestMethod]
    public void NonadjacentBinary64IntermediateStillRoundsToBinary64BeforeBinary32()
    {
        MethodInfo source = Dynamic("DoubleRound", typeof(float), [typeof(ulong)], static il =>
        {
            il.DeclareLocal(typeof(double)); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Conv_R_Un); il.Emit(OpCodes.Stloc_0); il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Conv_R4); il.Emit(OpCodes.Ret);
        });
        WarpPortableWordLoweredProgram program = Lower(source);
        CollectionAssert.AreEqual(new uint[] { 0x5F000000 }, Execute(program, [1, 0x80000080]));
        Assert.IsFalse(program.RequiredServices.Any(identity => identity.Contains("unsigned-single-adjacent", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void SourceStepBudgetDoesNotChargeSoftwareArithmeticHelpers()
    {
        WarpPortableWordLoweredProgram program = Lower(nameof(Kernels.Single));
        int sourceInstructions = program.Bodies[0].SourceBlocks.Length;
        CollectionAssert.AreEqual(new uint[] { 0x40A00000 }, Execute(program, [0x3F800000, 0x40000000], sourceInstructions));
        Assert.IsGreaterThan(1, program.Kernel.Functions.Count);
    }

    [TestMethod]
    public void AllPortableEmittersContainFixedWordOperationsAndNoNativeFloatConversion()
    {
        var layout = new WarpLogicalMachineLayout(Lower(nameof(Kernels.Double)).Kernel);
        foreach (WarpBackendKind backend in new[] { WarpBackendKind.NVPTX, WarpBackendKind.AMDGPU, WarpBackendKind.SPIRV })
        {
            string source = WarpPortableMachineEmitter.Emit(layout, backend);
            Assert.IsFalse(source.Contains("fadd", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("fptrunc", StringComparison.Ordinal));
            Assert.IsTrue(source.Contains("load i32", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void DifferentClosuresCannotReuseTypedMaps()
    {
        WarpPortableMethodGraph first = Graph(nameof(Kernels.Single));
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(first);
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(Graph(nameof(Kernels.Double)), typed));
        Assert.AreEqual("WRPCLR2300", error.Code, StringComparer.Ordinal);
    }

    [TestMethod]
    public void UnboundCheckedFaultsFailAtTheSourceInstruction()
    {
        WarpPortableMethodGraph graph = Graph(nameof(Kernels.Checked));
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(graph, WarpPortableTypedProgram.Verify(graph)));
        Assert.AreEqual(graph.Methods.First(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal)).Instructions.First(instruction => instruction.OpCode == OpCodes.Add_Ovf).Offset, error.IlOffset);
    }

    private static WarpPortableMethodGraph Graph(string name) => WarpPortableMethodGraph.Discover(typeof(Kernels).GetMethod(name)!);
    private static WarpPortableWordLoweredProgram Lower(string name) { WarpPortableMethodGraph graph = Graph(name); return WarpPortableWordLowerer.Lower(graph, WarpPortableTypedProgram.Verify(graph)); }
    private static WarpPortableWordLoweredProgram Lower(MethodInfo method) { WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(method); return WarpPortableWordLowerer.Lower(graph, WarpPortableTypedProgram.Verify(graph)); }
    private static byte ReadByte(uint[] words, int offset) => unchecked((byte)(words[offset / 4] >> ((offset % 4) * 8)));

    private static uint[] Execute(WarpPortableWordLoweredProgram program, uint[] arguments, long sourceSteps = 100000)
    {
        var layout = new WarpLogicalMachineLayout(program.Kernel);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(32, sourceSteps);
        uint[][] inputs = arguments.Length == 0 ? [[0]] : arguments.Select(word => new[] { word }).ToArray();
        for (int attempt = 0; attempt < 10000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; attempt++)
        {
            compiled.ExecuteQuantum(inputs, [], 0, state, 32, layout.MaximumBlockCost);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return Enumerable.Range(0, layout.ResultWordCount).Select(word => state[layout.GetResultWordOffset(word, 32)]).ToArray();
    }

    private static MethodInfo Dynamic(string name, Type result, Type[] parameters, Action<ILGenerator> emit)
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("WordLowering_" + name + "_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder type = assembly.DefineDynamicModule(name).DefineType(name, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, result, parameters);
        emit(method.GetILGenerator()); return type.CreateType()!.GetMethod(name)!;
    }

    private static class Kernels
    {
        public static int Small(sbyte value) => value + 256;
        public static int Loop(int count) { int sum = 0; while (count > 0) { sum += count; count--; } return sum; }
        public static int Choose(int choice) => choice switch { 0 => 11, 1 => 17, 2 => 29, _ => 41 };
        public static int Recursive(int count) => count <= 0 ? 0 : count + Recursive(count - 1);
        public static bool Even(int count) => count == 0 || Odd(count - 1);
        private static bool Odd(int count) => count != 0 && Even(count - 1);
        public static long Wide(long left, long right) => left + right;
        public static float Single(float left, float right) => left + right * right;
        public static double Double(double left, double right) => left + right * right;
        public static (byte, bool, float, object) Tuple(byte value, bool flag, float number, object reference) => (value, flag, number, reference);
        public static int Generic(int value) { Sink(value); return Identity(value); }
        private static T Identity<T>(T value) => value;
        private static void Sink(int value) { _ = value; }
        public static object Reference(object value) => value;
        public static bool ReferenceEqual(object left, object right) => left == right;
        public static float UnsignedSingle(ulong value) => value;
        public static int Checked(int left, int right) => checked(left + right);
    }
}
