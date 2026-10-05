using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableSourceCliSizeCases
{
    internal static void ExplicitCliPrimitiveAndReferenceSizesRemainSeparateFromOwnerWords()
    {
        foreach ((Type type, uint expected) in new (Type, uint)[]
        {
            (typeof(bool), 1), (typeof(byte), 1), (typeof(char), 2), (typeof(short), 2), (typeof(int), 4),
            (typeof(uint), 4), (typeof(long), 8), (typeof(ulong), 8), (typeof(float), 4), (typeof(double), 8),
            (typeof(object), 8), (typeof(string), 8), (typeof(object[]), 8),
        })
        {
            Fixture fixture = Capture(type);
            Assert.AreEqual(expected, fixture.Contract.TypeSize(WarpPortableMethodGraphIdentity.Type(type), 0).ByteSize);
            Assert.AreEqual(expected, Execute(fixture.Lowered));
            if (!type.IsValueType)
            {
                Assert.AreEqual(12, fixture.Typed.Types.First(item => string.Equals(item.Identity, WarpPortableMethodGraphIdentity.Type(type), StringComparison.Ordinal)).ByteSize);
            }
        }
        Assert.AreEqual(8, Unsafe.SizeOf<object>()); Assert.AreEqual(8, Unsafe.SizeOf<string>());
    }

    internal static void PackedAndNestedCliSizesIncludeTheirOwnClrPaddingAndOffsets()
    {
        var packed = new Packed(0xA5, 0x123456789ABCL); var nested = new Nested(packed, 0xABCDEF01);
        Assert.AreEqual((byte)0xA5, nested.Left.Tag); Assert.AreEqual(0x123456789ABCL, nested.Left.Value);
        Assert.AreEqual(0xABCDEF01u, nested.Right);
        Fixture packedFixture = Capture(typeof(Packed)); Fixture nestedFixture = Capture(typeof(Nested));
        Assert.AreEqual((uint)Unsafe.SizeOf<Packed>(), Execute(packedFixture.Lowered));
        Assert.AreEqual((uint)Unsafe.SizeOf<Nested>(), Execute(nestedFixture.Lowered));
        WarpPortableCliTypeSize layout = packedFixture.Contract.TypeSize(WarpPortableMethodGraphIdentity.Type(typeof(Packed)), 0);
        Assert.AreEqual(9u, layout.ByteSize);
        Assert.AreEqual(1u, layout.Fields.First(field => field.Identity.Contains("::Value:", StringComparison.Ordinal)).ByteOffset);
        Assert.AreEqual(8u, layout.Fields.First(field => field.Identity.Contains("::Value:", StringComparison.Ordinal)).ByteSize);
    }

    internal static void ReferenceContainingCliSizeNeverUsesItsPortableTwelveByteOwners()
    {
        var value = new ReferenceAndLong(new object(), 0x123456789ABCL);
        Assert.IsNotNull(value.Reference); Assert.AreEqual(0x123456789ABCL, value.Value);
        Fixture fixture = Capture(typeof(ReferenceAndLong)); string identity = WarpPortableMethodGraphIdentity.Type(typeof(ReferenceAndLong));
        WarpPortableTypedType storage = fixture.Typed.Types.First(type => string.Equals(type.Identity, identity, StringComparison.Ordinal));
        WarpPortableCliTypeSize numeric = fixture.Contract.TypeSize(identity, 0);
        Assert.AreEqual((uint)Unsafe.SizeOf<ReferenceAndLong>(), numeric.ByteSize);
        Assert.AreEqual(16u, Execute(fixture.Lowered)); Assert.AreEqual(24, storage.ByteSize);
        Assert.HasCount(1, storage.ManagedRootByteOffsets); Assert.AreEqual(0, storage.ManagedRootByteOffsets.First());
        Assert.AreEqual(8u, numeric.Fields.First(field => field.Identity.Contains("::Value:", StringComparison.Ordinal)).ByteOffset);
        Assert.AreEqual(16, storage.Fields.First(field => field.Identity.Contains("::Value:", StringComparison.Ordinal)).ByteOffset);
        Assert.IsTrue(fixture.Lowered.RequiredServices.Any(service => service.Contains(fixture.Contract.ContractHash, StringComparison.Ordinal)));
    }

    internal static void ExplicitAndClosedGenericCliLayoutsRetainExactMetadataSpecializations()
    {
        var explicitValue = new ExplicitValue(0x12345678); Assert.AreEqual(0x12345678, explicitValue.Value);
        var generic = new GenericValue<long>(new object(), 1234); Assert.IsNotNull(generic.Reference); Assert.AreEqual(1234L, generic.Value);
        Fixture explicitFixture = Capture(typeof(ExplicitValue)); Fixture genericFixture = Capture(typeof(GenericValue<long>));
        Assert.AreEqual((uint)Unsafe.SizeOf<ExplicitValue>(), Execute(explicitFixture.Lowered));
        Assert.AreEqual((uint)Unsafe.SizeOf<GenericValue<long>>(), Execute(genericFixture.Lowered));
        Assert.AreEqual(16u, explicitFixture.Contract.TypeSize(WarpPortableMethodGraphIdentity.Type(typeof(ExplicitValue)), 0).Fields.First().ByteOffset);
        Assert.IsTrue(genericFixture.Contract.Sizes.Any(size => string.Equals(size.Identity, WarpPortableMethodGraphIdentity.Type(typeof(GenericValue<long>)), StringComparison.Ordinal)));
    }

    internal static void CliLayoutCaptureAndGeneratedExecutionNeverRunUserTypeInitializers()
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new("WarpCliNoInitializer"), AssemblyBuilderAccess.RunAndCollect);
        ModuleBuilder module = assembly.DefineDynamicModule("source");
        TypeBuilder counterBuilder = module.DefineType("Counter", TypeAttributes.Public | TypeAttributes.Sealed);
        counterBuilder.DefineField("Count", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        Type counter = counterBuilder.CreateType()!; FieldInfo count = counter.GetField("Count")!;
        TypeBuilder valueBuilder = module.DefineType("WithInitializer", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, typeof(ValueType));
        valueBuilder.DefineField("Reference", typeof(object), FieldAttributes.Public); valueBuilder.DefineField("Value", typeof(long), FieldAttributes.Public);
        ILGenerator initializer = valueBuilder.DefineTypeInitializer().GetILGenerator(); initializer.Emit(OpCodes.Ldc_I4_1); initializer.Emit(OpCodes.Stsfld, count); initializer.Emit(OpCodes.Ret);
        Type value = valueBuilder.CreateType()!; Fixture fixture = Capture(value);
        Assert.AreEqual(16u, Execute(fixture.Lowered));
        Assert.AreEqual(0, (int)count.GetValue(null)!);
        Assert.IsTrue(fixture.Graph.Types.Any(type => type.SourceType == value && type.Initializer is not null));
        Assert.AreEqual(fixture.Contract.ContractHash, WarpPortableCliSizeContract.Capture(fixture.Graph).ContractHash, StringComparer.Ordinal);
        Assert.AreEqual(0, (int)count.GetValue(null)!);
    }

    internal static void CliSizeBindingChangesVerifiedKernelIdentityAndRejectsAnotherClosure()
    {
        WarpPortableMethodGraph graph = Graph(typeof(int)); WarpPortableCliSizeContract contract = WarpPortableCliSizeContract.Capture(graph);
        WarpPortableTypedProgram plain = WarpPortableTypedProgram.Verify(graph), bound = WarpPortableTypedProgram.Verify(graph, contract);
        Assert.AreNotEqual(plain.VerifiedHash, bound.VerifiedHash, StringComparer.Ordinal);
        Assert.AreNotEqual(WarpIrHash.Compute(WarpPortableWordLowerer.Lower(graph, plain).Kernel), WarpIrHash.Compute(WarpPortableWordLowerer.Lower(graph, bound).Kernel), StringComparer.Ordinal);
        Assert.AreEqual(contract.ContractHash, WarpPortableCliSizeContract.Capture(graph).ContractHash, StringComparer.Ordinal);
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableTypedProgram.Verify(Graph(typeof(uint)), contract));
        Assert.AreEqual("WRPCLR2210", error.Code, StringComparer.Ordinal);
    }

    internal static void UnboundReferenceAndCompoundSizeofRemainExplicitlyRejected()
    {
        foreach (Type type in new[] { typeof(object), typeof(ReferenceAndLong) })
        {
            WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableTypedProgram.Verify(Graph(type)));
            Assert.AreEqual("WRPCLR2200", error.Code, StringComparer.Ordinal); Assert.IsTrue(error.Message.Contains("physical", StringComparison.Ordinal));
        }
        WarpPortableMethodGraph packed = Graph(typeof(Packed)); WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(packed);
        WarpVerificationException compound = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(packed, typed));
        Assert.AreEqual("WRPCLR2300", compound.Code, StringComparer.Ordinal); Assert.IsTrue(compound.Message.Contains("separate captured CLI", StringComparison.Ordinal));
    }

    internal static void NativeArrayLengthShiftCannotBeAdmittedAsAnI4Operation()
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new("WarpNativeLengthWitness"), AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder source = assembly.DefineDynamicModule("source").DefineType("Source", TypeAttributes.Public | TypeAttributes.Sealed);
        MethodBuilder method = source.DefineMethod("LengthShift", MethodAttributes.Public | MethodAttributes.Static, typeof(ulong), [typeof(uint[])]);
        ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Ldc_I4_S, (sbyte)32);
        il.Emit(OpCodes.Shl); il.Emit(OpCodes.Conv_U8); il.Emit(OpCodes.Ret);
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source.CreateType()!.GetMethod("LengthShift")!);
        WarpVerificationException width = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableTypedProgram.Verify(graph));
        Assert.AreEqual("WRPCLR2210", width.Code, StringComparer.Ordinal); Assert.AreEqual(1, width.IlOffset);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph, WarpPortableCliSizeContract.Capture(graph));
        Assert.AreEqual(WarpPortableStackCategory.CliNativeInteger, typed.Methods.First(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal)).Instructions[1].ExitStack[0].Category);
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(graph, typed));
        Assert.AreEqual("WRPCLR2300", error.Code, StringComparer.Ordinal); Assert.AreEqual(1, error.IlOffset);
        Assert.IsTrue(error.Message.Contains("native-unsigned", StringComparison.Ordinal));
        uint[] values = [1];
        Assert.AreEqual(4294967296UL, (ulong)((nuint)values.Length << 32));
        Assert.AreEqual(1u, (uint)values.Length << 32);
    }

    internal static void CliCaptureRejectsOversizedValueLocalsBeforeFieldAddressCapture()
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new("WarpCliCaptureLimit"), AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder value = assembly.DefineDynamicModule("source").DefineType("Oversized",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.ExplicitLayout, typeof(ValueType),
            PackingSize.Size1, WarpCompilationAdmission.MaximumValueSlotsPerEntry + 1);
        value.DefineField("Tag", typeof(byte), FieldAttributes.Public).SetOffset(0);
        WarpPortableMethodGraph graph = Graph(value.CreateType()!);
        WarpCompilationResourceException error = Assert.ThrowsExactly<WarpCompilationResourceException>(() => WarpPortableCliSizeContract.Capture(graph));
        Assert.IsTrue(error.Message.Contains("ValueSlots", StringComparison.Ordinal));
    }

    private static Fixture Capture(Type type)
    {
        WarpPortableMethodGraph graph = Graph(type); WarpPortableCliSizeContract contract = WarpPortableCliSizeContract.Capture(graph);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph, contract);
        return new(graph, contract, typed, WarpPortableWordLowerer.Lower(graph, typed));
    }
    private static WarpPortableMethodGraph Graph(Type type)
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new("WarpCliSizeWitness"), AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder source = assembly.DefineDynamicModule("source").DefineType("Source", TypeAttributes.Public | TypeAttributes.Sealed);
        MethodBuilder method = source.DefineMethod("Size", MethodAttributes.Public | MethodAttributes.Static, typeof(uint), Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Sizeof, type); il.Emit(OpCodes.Ret);
        Type created = source.CreateType()!;
        return WarpPortableMethodGraph.Discover(created.GetMethod("Size")!, permittedAssemblies: [created.Assembly, type.Assembly, typeof(WarpPortableSourceCliSizeCases).Assembly]);
    }
    private static uint Execute(WarpPortableWordLoweredProgram program)
    {
        var layout = new WarpLogicalMachineLayout(program.Kernel); uint[] state = layout.CreateInitialState(64, 1000);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        for (int quantum = 0; quantum < 10000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; quantum++)
        {
            compiled.ExecuteQuantum([new uint[1]], [], 0, state, 64, layout.MaximumBlockCost);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return state[layout.GetResultWordOffset(0, 64)];
    }
    private sealed record Fixture(WarpPortableMethodGraph Graph, WarpPortableCliSizeContract Contract,
        WarpPortableTypedProgram Typed, WarpPortableWordLoweredProgram Lowered);
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Packed { public byte Tag; public long Value; public Packed(byte tag, long value) { Tag = tag; Value = value; } }
    [StructLayout(LayoutKind.Sequential)]
    private struct Nested { public Packed Left; public uint Right; public Nested(Packed left, uint right) { Left = left; Right = right; } }
    [StructLayout(LayoutKind.Sequential)]
    private struct ReferenceAndLong { public object? Reference; public long Value; public ReferenceAndLong(object? reference, long value) { Reference = reference; Value = value; } }
    [StructLayout(LayoutKind.Sequential)]
    private struct GenericValue<T> { public object? Reference; public T Value; public GenericValue(object? reference, T value) { Reference = reference; Value = value; } }
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct ExplicitValue { [FieldOffset(16)] public int Value; public ExplicitValue(int value) { Value = value; } }
}
