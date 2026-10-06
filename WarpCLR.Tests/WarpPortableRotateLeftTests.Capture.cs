using WarpCLR.Tests;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed partial class WarpPortableRotateLeftTests
{
    [TestMethod]
    public void OnlyExactCorelibUnsignedOverloadsWithInt32CountReceiveTheNewIntrinsic()
    {
        foreach (Type type in new[] { typeof(uint), typeof(ulong) })
        {
            MethodInfo target = Standard(nameof(BitOperations.RotateLeft), type);
            Assert.AreSame(typeof(object).Assembly, target.Module.Assembly);
            Assert.AreEqual(typeof(BitOperations), target.DeclaringType);
            Assert.AreEqual(type, target.ReturnType);
            CollectionAssert.AreEqual(new[] { type, typeof(int) }, target.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
            int width = type == typeof(uint) ? 32 : 64;
            Assert.AreEqual(width, WarpPortableMethodGraphIntrinsics.RotateLeftWordWidth(target));
            StringAssert.Contains(WarpPortableMethodGraphIntrinsics.Resolve(target)!, WarpPortableBitOperations.Semantics, StringComparison.Ordinal);
            WarpPortableWordMathBinding binding = WarpPortableWordMathCatalog.Resolve(target)!;
            Assert.AreEqual(typeof(WarpPortableBitOperations), binding.Implementation);
            Assert.HasCount(width / 32, binding.ValueEntrypoints);
            Assert.IsNull(binding.FaultEntrypoint);
            Assert.IsTrue(binding.AdditionalValueWords.IsEmpty);
            Assert.IsTrue(binding.Faults.IsEmpty);
            foreach (string name in binding.ValueEntrypoints)
            {
                MethodInfo service = binding.Implementation.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
                Assert.AreEqual(typeof(uint), service.ReturnType);
                Assert.IsTrue(service.GetParameters().All(parameter => parameter.ParameterType == typeof(uint)));
            }
        }
    }

    [TestMethod]
    public void CorelibRotateRightRemainsUnboundForBothWidths()
    {
        foreach ((Type type, string name) in new[]
        {
            (typeof(uint), nameof(WarpPortableRotateLeftSourceKernels.UnboundRotateRight32)),
            (typeof(ulong), nameof(WarpPortableRotateLeftSourceKernels.UnboundRotateRight64)),
        })
        {
            MethodInfo target = Standard(nameof(BitOperations.RotateRight), type);
            Assert.AreEqual(0, WarpPortableMethodGraphIntrinsics.RotateLeftWordWidth(target));
            Assert.IsNull(WarpPortableMethodGraphIntrinsics.Resolve(target));
            Assert.IsNull(WarpPortableWordMathCatalog.Resolve(target));
            WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableMethodGraph.Discover(Source(name)));
            StringAssert.Contains(error.Message, "outside the permitted assemblies", StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public void SameFullNameAndSignatureInAnotherAssemblyNeverReceiveCorelibAdmission()
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RotateLeftLookalike"), AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder type = assembly.DefineDynamicModule("RotateLeftLookalike").DefineType(typeof(BitOperations).FullName!,
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder method = type.DefineMethod(nameof(BitOperations.RotateLeft), MethodAttributes.Public | MethodAttributes.Static,
            typeof(uint), [typeof(uint), typeof(int)]);
        ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        MethodInfo source = type.CreateType()!.GetMethod(nameof(BitOperations.RotateLeft))!;
        Assert.AreEqual(0, WarpPortableMethodGraphIntrinsics.RotateLeftWordWidth(source));
        Assert.IsNull(WarpPortableMethodGraphIntrinsics.Resolve(source));
        Assert.IsNull(WarpPortableWordMathCatalog.Resolve(source));
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source);
        Assert.IsFalse(graph.Methods.RequireExactlyOne().Cil.IsEmpty);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, WarpPortableTypedProgram.Verify(graph));
        Assert.IsFalse(program.RequiredServices.Any(identity => identity.Contains(WarpPortableBitOperations.Semantics, StringComparison.Ordinal)));
    }

    [TestMethod]
    public void OriginalExternalCallKeepsItsCilAndLegacyRejectionWhileTypedLoweringGainsAnExactBinding()
    {
        MethodInfo source = typeof(TestKernels).GetMethod(nameof(TestKernels.ExternalCall))!;
        byte[] cil = source.GetMethodBody()!.GetILAsByteArray()!;
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source);
        WarpPortableMethodGraphMethod captured = graph.Methods.RequireExactlyOne(method => method.SourceMethod.Equals(source));
        CollectionAssert.AreEqual(cil, captured.Cil.ToArray());
        Assert.IsTrue(captured.Instructions.Any(instruction => instruction.OpCode == OpCodes.Call));
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, WarpPortableTypedProgram.Verify(graph));
        Assert.IsTrue(program.RequiredServices.Any(identity => identity.Contains(WarpPortableBitOperations.Semantics, StringComparison.Ordinal)));
        WarpVerificationException legacy = Assert.ThrowsExactly<WarpVerificationException>(() =>
            new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(source, 1)));
        Assert.AreEqual("WRPCIL1013", legacy.Code, StringComparer.Ordinal);
    }

    [TestMethod]
    public void ExistingOrdinaryContractsAndNonrotationProgramsKeepTheirVersionsAndNoRotationService()
    {
        Assert.AreEqual("warp.math.binary32.arithmetic/rne-gradual-canonical-nan/0.1|warp.math.binary64.arithmetic/rne-gradual-canonical-nan/0.1|warp.math.overloads/exact-signed-width-enum-fault/0.1",
            RuntimeConstant(typeof(WarpPortableMethodGraphIntrinsics), "MathContract"), StringComparer.Ordinal);
        Assert.AreEqual("warp.logical-source-frames/0.6", RuntimeConstant(typeof(WarpLogicalExecutionMetadata), "Version"), StringComparer.Ordinal);
        Assert.AreEqual("warp.logical-machine/0.8", RuntimeConstant(typeof(WarpLogicalMachineLayout), "Version"), StringComparer.Ordinal);
        MethodInfo source = typeof(TestKernels).GetMethod(nameof(TestKernels.ManifestMap))!;
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, WarpPortableTypedProgram.Verify(graph));
        Assert.IsFalse(program.RequiredServices.Any(identity => identity.Contains(WarpPortableBitOperations.Semantics, StringComparison.Ordinal)));
        Assert.IsNull(program.Kernel.Execution!.PrivateControllerProjection);
    }

    private static MethodInfo Source(string name) => typeof(WarpPortableRotateLeftSourceKernels).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    private static MethodInfo Standard(string name, Type valueType) => typeof(BitOperations).GetMethod(name, BindingFlags.Public | BindingFlags.Static, [valueType, typeof(int)])!;

    private static WarpPortableWordLoweredProgram Capture(string name)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(Source(name));
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed);
        WarpPortableWordProgramIdentity.Validate(graph, WarpPortableSourceHeapSchema.Create(graph, typed), program);
        return program;
    }

    private static string RuntimeConstant(Type owner, string field) =>
        (string)owner.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
}
