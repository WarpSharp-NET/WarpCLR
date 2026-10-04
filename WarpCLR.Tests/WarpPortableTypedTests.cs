using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpPortableTypedTests
{
    [TestMethod]
    public void MixedPrimitiveAndTupleTypesRetainWidthsSignednessFieldsAndRoots()
    {
        WarpPortableTypedProgram program = Verify(nameof(Kernels.Mixed));
        Assert.IsTrue(program.Types.Any(type => type.Category == WarpPortableStackCategory.Binary32 && type.WordCount == 1));
        Assert.IsTrue(program.Types.Any(type => type.Category == WarpPortableStackCategory.Binary64 && type.WordCount == 2));
        WarpPortableTypedType boolean = Only(program.Types.Where(type => string.Equals(type.Identity, WarpPortableMethodGraphIdentity.Type(typeof(bool)), StringComparison.Ordinal)));
        Assert.AreEqual(8, boolean.StorageBits);
        Assert.AreEqual(WarpPortableStackCategory.I4, boolean.Category);
        WarpPortableTypedType tuple = Only(program.Types.Where(type => string.Equals(type.Identity,
            WarpPortableMethodGraphIdentity.Type(typeof((long, ulong, float, double, byte, bool, string))), StringComparison.Ordinal)));
        Assert.HasCount(7, tuple.Fields);
        Assert.HasCount(1, tuple.ManagedRootByteOffsets);
        Assert.IsTrue(program.Methods.Any(method => method.Intrinsic?.Contains("value-tuple.construct", StringComparison.Ordinal) == true));
        Assert.IsTrue(program.Methods.All(method => method.Instructions.All(instruction => !instruction.Reachable || instruction.EntryStack.All(value => value.WordCount > 0))));
    }

    [TestMethod]
    public void RefInOutAndPrivateByrefOwnersAreTrackedWithoutExecutingUserCode()
    {
        WarpPortableTypedProgram program = Verify(nameof(Kernels.References));
        Assert.IsTrue(program.Methods.Any(method => method.Instructions.Any(instruction => instruction.EntryStack.Any(value =>
            value.Category == WarpPortableStackCategory.ManagedByref && value.WordCount == 6 &&
            value.Provenance.Any(origin => origin.Kind == WarpPortableProvenanceKind.FrameLocal)))));
        Assert.IsTrue(program.Methods.Any(method => method.Instructions.Any(instruction => instruction.EntryArguments.Any(slot => slot.Value.IsReadOnly))));
        Assert.IsTrue(program.Methods.Any(method => method.Instructions.Any(instruction => instruction.Roots.Any(root => root.IsInteriorOwner))));
    }

    [TestMethod]
    public void RecursiveGenericInstanceVirtualInterfaceAndDelegateClosuresVerify()
    {
        Type fixture = typeof(WarpPortableClosureTests).GetNestedType("Kernels", BindingFlags.NonPublic)!;
        string[] names = ["Direct", "Even", "Specialized", "Dispatch", "GenericDispatch", "VirtualDelegate", "DefaultConstruction", "DelegateCall", "StringData", "BitCast", "Cleanup", "ArrayData"];
        foreach (string name in names)
        {
            WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(fixture.GetMethod(name)!);
            WarpPortableTypedProgram program = WarpPortableTypedProgram.Verify(graph);
            Assert.AreEqual(graph.GraphHash, program.GraphHash, StringComparer.Ordinal);
            Assert.HasCount(graph.Methods.Length, program.Methods);
        }
    }

    [TestMethod]
    public void ExceptionFiltersFinallyAndFaultSitesKeepSourceOffsetsAndOrderedEffects()
    {
        WarpPortableTypedProgram program = Verify(nameof(Kernels.Handling));
        WarpPortableTypedInstruction[] nodes = program.Methods.SelectMany(method => method.Instructions).ToArray();
        Assert.IsTrue(nodes.Any(node => node.ExceptionMemberships.Any(member => member.Role == WarpPortableExceptionRole.Filter)));
        Assert.IsTrue(nodes.Any(node => node.ExceptionMemberships.Any(member => member.Role == WarpPortableExceptionRole.Finally)));
        Assert.IsTrue(nodes.Any(node => !node.UnwindRegions.IsEmpty));
        Assert.IsTrue(nodes.All(node => node.Faults.All(fault => fault.SourceOffset == node.Offset)));
        WarpPortableTypedInstruction array = Only(nodes.Where(node => node.OpCode == OpCodes.Ldelem_I4.Value));
        CollectionAssert.AreEqual(new[] { WarpPortableTypedFaultKind.NullReference, WarpPortableTypedFaultKind.IndexOutOfRange }, array.Faults.Select(fault => fault.Kind).ToArray());
    }

    [TestMethod]
    public void ArrayStoreAndCastFaultsRemainDifferentUserExceptionKinds()
    {
        WarpPortableTypedProgram program = Verify(nameof(Kernels.ArrayAndCast));
        WarpPortableTypedInstruction[] nodes = program.Methods.SelectMany(method => method.Instructions).ToArray();
        Assert.IsTrue(nodes.Any(node => node.OpCode == OpCodes.Stelem_Ref.Value && node.Faults.Any(fault => fault.Kind == WarpPortableTypedFaultKind.ArrayTypeMismatch)));
        Assert.IsTrue(nodes.Any(node => node.OpCode == OpCodes.Castclass.Value && node.Faults.Any(fault => fault.Kind == WarpPortableTypedFaultKind.InvalidCast)));
    }

    [TestMethod]
    public void ADirectNullArrayReferenceRetainsItsSourceNullFaultBeforeBoundsChecks()
    {
        MethodInfo source = Dynamic("NullArray", typeof(int), [], static (il, _) =>
        {
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelem_I4); il.Emit(OpCodes.Ret);
        });
        WarpPortableTypedProgram program = WarpPortableTypedProgram.Verify(WarpPortableMethodGraph.Discover(source));
        WarpPortableTypedInstruction access = Only(program.Methods.SelectMany(method => method.Instructions).Where(node => node.OpCode == OpCodes.Ldelem_I4.Value));
        Assert.IsTrue(access.EntryStack[0].IsNull);
        CollectionAssert.AreEqual(new[] { WarpPortableTypedFaultKind.NullReference, WarpPortableTypedFaultKind.IndexOutOfRange }, access.Faults.Select(fault => fault.Kind).ToArray());
        Assert.IsTrue(access.Faults.All(fault => fault.SourceOffset == access.Offset));
        Assert.AreEqual(WarpPortableMethodGraphIdentity.Type(typeof(int)), access.MemoryType, StringComparer.Ordinal);
    }

    [TestMethod]
    public void ExplicitPrimitiveUnionSharesBytesAndKeepsExactFloatInterpretation()
    {
        WarpPortableTypedProgram program = Verify(nameof(Kernels.Union));
        WarpPortableTypedType type = Only(program.Types.Where(type => string.Equals(type.Identity, WarpPortableMethodGraphIdentity.Type(typeof(Bits)), StringComparison.Ordinal)));
        Assert.HasCount(2, type.Fields);
        Assert.IsTrue(type.Fields.All(field => field.ByteOffset == 0));
        Assert.AreEqual(4, type.ByteSize);
    }

    [TestMethod]
    public void BooleanStoresDeclareByteTruncationInsteadOfNonzeroNormalization()
    {
        MethodInfo source = Dynamic("BooleanStore", typeof(bool), [typeof(bool)], static (il, _) =>
        {
            il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Starg_S, (byte)0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        });
        WarpPortableTypedProgram program = WarpPortableTypedProgram.Verify(WarpPortableMethodGraph.Discover(source));
        WarpPortableTypedInstruction store = Only(program.Methods.SelectMany(method => method.Instructions).Where(node => node.OpCode == OpCodes.Starg_S.Value));
        Assert.AreEqual(8, store.StorageBits);
        Assert.AreEqual(WarpPortableMethodGraphIdentity.Type(typeof(bool)), store.MemoryType, StringComparer.Ordinal);
    }

    [TestMethod]
    public void SmallIntegerStoragePromotesToFullI4ArithmeticBeforeLaterTruncation()
    {
        MethodInfo source = Dynamic("Promote", typeof(int), [typeof(byte)], static (il, _) =>
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4, 256); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ret);
        });
        WarpPortableTypedProgram program = WarpPortableTypedProgram.Verify(WarpPortableMethodGraph.Discover(source));
        WarpPortableTypedInstruction add = Only(program.Methods.SelectMany(method => method.Instructions).Where(node => node.OpCode == OpCodes.Add.Value));
        Assert.AreEqual(WarpPortableMethodGraphIdentity.Type(typeof(byte)), add.EntryStack[0].SourceStorageType, StringComparer.Ordinal);
        Assert.AreEqual(WarpPortableMethodGraphIdentity.Type(typeof(int)), add.ExitStack[0].TypeIdentity, StringComparer.Ordinal);
        Assert.IsNull(add.ExitStack[0].SourceStorageType);
    }

    [TestMethod]
    public void PortableTypeHandlesAndConstrainedPrimitiveTypeQueriesRemainDistinctFromIntegers()
    {
        WarpPortableTypedProgram program = Verify(nameof(Kernels.TypeQuery));
        Assert.IsTrue(program.Types.Any(type => type.Category == WarpPortableStackCategory.Handle && type.WordCount == 1));
        Assert.IsTrue(program.Methods.Any(method => method.Intrinsic?.Contains("type.from-handle", StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public void OpaqueRuntimeHandleSizeCannotBecomeAnObservableBackendDependentInteger()
    {
        MethodInfo source = Dynamic("HandleSize", typeof(uint), [], static (il, _) =>
        {
            il.Emit(OpCodes.Sizeof, typeof(RuntimeTypeHandle)); il.Emit(OpCodes.Ret);
        });
        Reject(source, OpCodes.Sizeof, "physical");
    }

    [TestMethod]
    public void RecursiveByrefReturnSummariesPreserveCallerOwnersAndHeapInteriors()
    {
        WarpPortableTypedProgram program = Verify(nameof(Kernels.Borrowed));
        Assert.IsTrue(program.Methods.Any(method => method.ReturnSummary.Origins.Any(origin => origin.Kind == WarpPortableProvenanceKind.Argument)));
        Assert.IsTrue(program.Methods.Any(method => method.ReturnSummary.Origins.Any(origin => origin.Kind == WarpPortableProvenanceKind.HeapInterior)));
        Assert.IsTrue(program.Methods.Any(method => method.Instructions.Any(node => node.EntryStack.Any(value => value.Provenance.Any(origin => origin.Kind == WarpPortableProvenanceKind.FrameLocal)))));
    }

    [TestMethod]
    public void TypedProgramHashBindsClosureAndAllImmutableMaps()
    {
        MethodInfo source = Source(nameof(Kernels.Mixed));
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source);
        WarpPortableTypedProgram first = WarpPortableTypedProgram.Verify(graph);
        WarpPortableTypedProgram second = WarpPortableTypedProgram.Verify(WarpPortableMethodGraph.Discover(source));
        Assert.AreEqual(first.VerifiedHash, second.VerifiedHash, StringComparer.Ordinal);
        Assert.AreNotEqual(first.GraphHash, first.VerifiedHash, StringComparer.Ordinal);
        Assert.AreNotEqual(first.VerifiedHash, Verify(nameof(Kernels.References)).VerifiedHash, StringComparer.Ordinal);
    }

    [TestMethod]
    public void AnUninitializedLocalReadFailsAtItsActualInstruction()
    {
        MethodInfo source = Dynamic("Uninitialized", typeof(int), [], static (il, builder) =>
        {
            builder.InitLocals = false; il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Ret);
        });
        Reject(source, OpCodes.Ldloc_0, "initialization");
    }

    [TestMethod]
    public void AFloatByrefCannotBeReinterpretedAsIntegerStorage()
    {
        MethodInfo source = Dynamic("Confusion", typeof(int), [typeof(float)], static (il, _) =>
        {
            il.Emit(OpCodes.Ldarga_S, (byte)0); il.Emit(OpCodes.Ldind_I4); il.Emit(OpCodes.Ret);
        });
        Reject(source, OpCodes.Ldind_I4, "category");
    }

    [TestMethod]
    public void DifferentFloatWidthsCannotMergeWithoutAnExplicitConversion()
    {
        MethodInfo source = Dynamic("Merge", typeof(void), [typeof(bool)], static (il, _) =>
        {
            Label wide = il.DefineLabel(); Label join = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brtrue_S, wide); il.Emit(OpCodes.Ldc_R4, 1.0f); il.Emit(OpCodes.Br_S, join);
            il.MarkLabel(wide); il.Emit(OpCodes.Ldc_R8, 1.0); il.MarkLabel(join); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ret);
        });
        Reject(source, OpCodes.Pop, "categories");
    }

    [TestMethod]
    public void AReadonlyByrefCannotBeUsedForAStore()
    {
        MethodInfo source = Dynamic("Readonly", typeof(void), [typeof(long).MakeByRefType()], static (il, builder) =>
        {
            ParameterBuilder parameter = builder.DefineParameter(1, ParameterAttributes.In, "value");
            parameter.SetCustomAttribute(new CustomAttributeBuilder(typeof(IsReadOnlyAttribute).GetConstructor(Type.EmptyTypes)!, []));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I8, 1L); il.Emit(OpCodes.Stind_I8); il.Emit(OpCodes.Ret);
        });
        Reject(source, OpCodes.Stind_I8, "readonly");
    }

    [TestMethod]
    public void AnOutPointeeCannotBeReadBeforeInitialization()
    {
        MethodInfo source = Dynamic("OutRead", typeof(int), [typeof(int).MakeByRefType()], static (il, builder) =>
        {
            builder.DefineParameter(1, ParameterAttributes.Out, "value"); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldind_I4); il.Emit(OpCodes.Ret);
        });
        Reject(source, OpCodes.Ldind_I4, "initialization");
    }

    [TestMethod]
    public void AByrefCannotEscapePrivateFrameStorageAtReturn()
    {
        MethodInfo source = Dynamic("Escape", typeof(int).MakeByRefType(), [], static (il, _) =>
        {
            il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Ldloca_S, (byte)0); il.Emit(OpCodes.Ret);
        });
        Reject(source, OpCodes.Ret, "lifetime");
    }

    [TestMethod]
    public void AnOrdinaryBranchCannotSkipFinallyUnwinding()
    {
        MethodInfo source = Dynamic("SkipFinally", typeof(void), [], static (il, _) =>
        {
            Label exit = il.DefineLabel(); il.BeginExceptionBlock(); il.Emit(OpCodes.Br, exit);
            il.BeginFinallyBlock(); il.Emit(OpCodes.Nop); il.EndExceptionBlock(); il.MarkLabel(exit); il.Emit(OpCodes.Ret);
        });
        Reject(source, OpCodes.Br, "boundary");
    }

    [TestMethod]
    public void APartialValueFieldWriteDoesNotInitializeTheRestOfTheValue()
    {
        MethodInfo source = Dynamic("Partial", typeof(void), [], static (il, builder) =>
        {
            builder.InitLocals = false;
            TypeBuilder pair = ((ModuleBuilder)builder.Module).DefineType("Pair", TypeAttributes.Public | TypeAttributes.SequentialLayout | TypeAttributes.Sealed, typeof(ValueType));
            pair.DefineField("First", typeof(int), FieldAttributes.Public);
            pair.DefineField("Second", typeof(int), FieldAttributes.Public);
            Type type = pair.CreateType()!;
            il.DeclareLocal(type); il.Emit(OpCodes.Ldloca_S, (byte)0); il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, type.GetField("First")!); il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ret);
        });
        Reject(source, OpCodes.Ldloc_0, "initialization");
    }

    [TestMethod]
    public void AWriteThroughMergedByrefAliasesDoesNotDefinitelyInitializeEveryPossibleOwner()
    {
        MethodInfo source = Dynamic("Aliases", typeof(int), [typeof(bool)], static (il, builder) =>
        {
            builder.InitLocals = false; il.DeclareLocal(typeof(int)); il.DeclareLocal(typeof(int));
            Label alternate = il.DefineLabel(); Label join = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brtrue_S, alternate); il.Emit(OpCodes.Ldloca_S, (byte)0); il.Emit(OpCodes.Br_S, join);
            il.MarkLabel(alternate); il.Emit(OpCodes.Ldloca_S, (byte)1); il.MarkLabel(join);
            il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stind_I4); il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Ret);
        });
        Reject(source, OpCodes.Ldloc_0, "initialization");
    }

    private static WarpPortableTypedProgram Verify(string name) => WarpPortableTypedProgram.Verify(WarpPortableMethodGraph.Discover(Source(name)));
    private static MethodInfo Source(string name) => typeof(Kernels).GetMethod(name)!;
    private static T Only<T>(IEnumerable<T> source)
    {
        T[] values = source.ToArray(); Assert.HasCount(1, values); return values[0];
    }

    private static void Reject(MethodInfo source, OpCode operation, string message)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source);
        WarpPortableMethodGraphInstruction instruction = Only(graph.Methods.SelectMany(method => method.Instructions).Where(node => node.OpCode == operation));
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableTypedProgram.Verify(graph));
        Assert.AreEqual(instruction.Offset, error.IlOffset);
        StringAssert.Contains(error.Message, message, StringComparison.Ordinal);
    }

    private static MethodInfo Dynamic(string methodName, Type result, Type[] parameters, Action<ILGenerator, MethodBuilder> emit)
    {
        var name = new AssemblyName("WarpCLR.PortableTyped." + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder type = assembly.DefineDynamicModule(name.Name!).DefineType("TypedProbe", TypeAttributes.Public);
        MethodBuilder method = type.DefineMethod(methodName, MethodAttributes.Public | MethodAttributes.Static, result, parameters);
        emit(method.GetILGenerator(), method);
        return type.CreateType()!.GetMethod(methodName)!;
    }

    private static class Kernels
    {
        public static (long, ulong, float, double, byte, bool, string) Mixed(long signed, ulong unsigned, float single, double wide, byte small, bool flag, string text) =>
            (signed + 1, unsigned ^ 7UL, single * 2.0f, wide + (double)single, unchecked((byte)(small + 1)), flag, text);

        public static long References(ref long value, in float scale, out double result)
        {
            long local = value; Update(ref local, in scale); value = local; result = local + (double)scale; return local;
        }

        private static void Update(ref long value, in float scale) { value += (long)scale; }

        public static int Handling(int[] input, int index)
        {
            int value;
            try { value = input[index] / index; }
            catch (InvalidOperationException exception) when (exception.Message.Length > 0) { value = 7; }
            finally { index++; }
            return value + index;
        }

        public static string ArrayAndCast(object[] values, object candidate) { values[0] = candidate; return (string)candidate; }

        public static float Union(uint value) { Bits bits = default; bits.Integer = value; return bits.Real; }

        public static bool TypeQuery(byte value) => value.GetType() == typeof(byte);

        public static int Borrowed(int value)
        {
            int local = value; ref int same = ref Recurse(ref local, 2); same++;
            var box = new Container(value); ref int field = ref FieldReference(box); field++; return local + box.Value;
        }

        private static ref int Recurse(ref int value, int count)
        {
            if (count == 0) { return ref value; }
            return ref Recurse(ref value, count - 1);
        }

        private static ref int FieldReference(Container value) => ref value.Value;
    }

    private sealed class Container(int value) { public int Value = value; }

    [StructLayout(LayoutKind.Explicit, Size = 4)]
    private struct Bits
    {
        [FieldOffset(0)] public uint Integer;
        [FieldOffset(0)] public float Real;
    }
}
