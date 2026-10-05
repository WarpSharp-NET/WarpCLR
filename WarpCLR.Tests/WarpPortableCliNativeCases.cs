using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableCliNativeCases
{
    internal static void NativeNumericConversionsRequireTheirOwnGraphBoundProfile()
    {
        MethodInfo source = NativeSource(OpCodes.Conv_U, null);
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source);
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableTypedProgram.Verify(graph));
        Assert.AreEqual("WRPCLR2210", error.Code, StringComparer.Ordinal); Assert.AreEqual(1, error.IlOffset);
        Fixture fixture = Capture(source); WarpPortableTypedType native = fixture.Typed.Types.First(type => type.Category == WarpPortableStackCategory.CliNativeInteger);
        Assert.AreEqual(64, native.StorageBits); Assert.AreEqual(2, native.WordCount); Assert.IsEmpty(native.ManagedRootByteOffsets);
        Assert.IsFalse(fixture.Schema.Types.Any(type => string.Equals(type.Identity, native.Identity, StringComparison.Ordinal)));
        Assert.IsTrue(fixture.Lowered.RequiredServices.Any(service => service.StartsWith(WarpPortableCliNativeInteger.Semantics, StringComparison.Ordinal)));
    }

    internal static void NativeSignedAndUnsignedExpansionMatchActualCoreClr64()
    {
        foreach (OpCode conversion in new[] { OpCodes.Conv_I, OpCodes.Conv_U })
        {
            Fixture fixture = Capture(NativeSource(conversion, null));
            Func<int, ulong> reference = fixture.Source.CreateDelegate<Func<int, ulong>>();
            foreach (int input in Inputs()) { Assert.AreEqual(reference(input), Execute(fixture, unchecked((uint)input))); }
        }
    }

    internal static void NativeShiftsKeepSixtyFourBitValuesAndCounts()
    {
        foreach (OpCode operation in new[] { OpCodes.Shl, OpCodes.Shr, OpCodes.Shr_Un })
        foreach (bool nativeCount in new[] { false, true })
        {
            Fixture fixture = Capture(ShiftSource(operation, nativeCount));
            Func<int, ulong> reference = fixture.Source.CreateDelegate<Func<int, ulong>>();
            foreach (int count in new[] { -1, 0, 1, 31, 32, 63, 64, 65, int.MinValue })
            {
                Assert.AreEqual(reference(count), Execute(fixture, unchecked((uint)count)));
            }
            Assert.AreEqual(WarpPortableStackCategory.CliNativeInteger,
                fixture.Typed.Methods.First(method => string.Equals(method.Identity, fixture.Graph.EntryIdentity, StringComparison.Ordinal)).Instructions
                    .First(instruction => instruction.OpCode == operation.Value).ExitStack[0].Category);
        }
    }

    internal static void NativeMixedI4ArithmeticPromotesWithSignedExtension()
    {
        foreach (OpCode operation in new[] { OpCodes.Add, OpCodes.Sub, OpCodes.Mul, OpCodes.And, OpCodes.Or, OpCodes.Xor })
        foreach (bool reverse in new[] { false, true })
        {
            Fixture fixture = Capture(MixedSource(operation, reverse)); Func<int, ulong> reference = fixture.Source.CreateDelegate<Func<int, ulong>>();
            foreach (int input in Inputs()) { Assert.AreEqual(reference(input), Execute(fixture, unchecked((uint)input)), operation.Name); }
        }
    }

    internal static void NativeMixedI4ComparisonsKeepHighWordsAndSignedPromotion()
    {
        foreach (OpCode operation in new[] { OpCodes.Ceq, OpCodes.Cgt, OpCodes.Cgt_Un, OpCodes.Clt, OpCodes.Clt_Un })
        foreach (bool reverse in new[] { false, true })
        {
            Fixture fixture = Capture(MixedSource(operation, reverse)); Func<int, ulong> reference = fixture.Source.CreateDelegate<Func<int, ulong>>();
            foreach (int input in Inputs()) { Assert.AreEqual(reference(input), Execute(fixture, unchecked((uint)input)), operation.Name); }
        }
    }

    internal static void NativeConditionalsConsumeBothWordsAndUseExactMixedComparisons()
    {
        foreach (OpCode operation in new[] { OpCodes.Brtrue, OpCodes.Brfalse, OpCodes.Brtrue_S, OpCodes.Brfalse_S,
            OpCodes.Beq, OpCodes.Bne_Un, OpCodes.Bgt, OpCodes.Bgt_Un, OpCodes.Bge, OpCodes.Bge_Un,
            OpCodes.Blt, OpCodes.Blt_Un, OpCodes.Ble, OpCodes.Ble_Un, OpCodes.Bgt_Un_S, OpCodes.Blt_Un_S, OpCodes.Bne_Un_S })
        foreach (long nativeValue in new[] { 0x100000000L, -1L })
        foreach (bool reverse in new[] { false, true })
        {
            Fixture fixture = Capture(BranchSource(operation, nativeValue, reverse)); Func<int, ulong> reference = fixture.Source.CreateDelegate<Func<int, ulong>>();
            foreach (int input in Inputs()) { Assert.AreEqual(reference(input), Execute(fixture, unchecked((uint)input)), operation.Name); }
        }
    }

    internal static void NativePrivateStorageAndCallsCoerceOnlyAtDeclaredWidths()
    {
        foreach (Type local in new[] { typeof(int), typeof(byte), typeof(sbyte), typeof(bool), typeof(char), typeof(short) })
        {
            Fixture fixture = Capture(StorageSource(local)); Func<int, ulong> reference = fixture.Source.CreateDelegate<Func<int, ulong>>();
            foreach (int input in Inputs()) { Assert.AreEqual(reference(input), Execute(fixture, unchecked((uint)input)), local.FullName); }
            WarpPortableTypedMethod method = fixture.Typed.Methods.First(item => string.Equals(item.Identity, fixture.Graph.EntryIdentity, StringComparison.Ordinal));
            WarpPortableTypedInstruction load = method.Instructions.First(item => item.OpCode == OpCodes.Ldloc_0.Value);
            Assert.AreEqual(WarpPortableStackCategory.I4, load.EntryLocals[0].Value.Category);
            Assert.IsTrue(load.EntryLocals[0].InitializedBytes.All(value => value));
        }
        Fixture call = Capture(CallSource()); Func<int, ulong> callReference = call.Source.CreateDelegate<Func<int, ulong>>();
        foreach (int input in Inputs()) { Assert.AreEqual(callReference(input), Execute(call, unchecked((uint)input))); }
    }

    internal static void LdlenAndArrayLengthRetainNativeCategoryBeforeExplicitI4Conversion()
    {
        foreach (bool conversion in new[] { false, true })
        {
            MethodInfo source = LengthSource(conversion); WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source);
            WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph, WarpPortableCliSizeContract.Capture(graph));
            WarpPortableTypedMethod method = typed.Methods.First(item => string.Equals(item.Identity, graph.EntryIdentity, StringComparison.Ordinal));
            WarpPortableTypedInstruction length = method.Instructions.First(item => item.OpCode == OpCodes.Ldlen.Value);
            Assert.AreEqual(WarpPortableStackCategory.CliNativeInteger, length.ExitStack[0].Category); Assert.AreEqual(2, length.ExitStack[0].WordCount);
            Assert.AreEqual(1, length.Faults.First().SourceOffset);
            if (conversion) { Assert.AreEqual(WarpPortableStackCategory.I4, method.Instructions.First(item => item.OpCode == OpCodes.Conv_I4.Value).ExitStack[0].Category); }
            WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(graph, typed));
            Assert.AreEqual("WRPCLR2300", error.Code, StringComparer.Ordinal); Assert.AreEqual(1, error.IlOffset);
        }
    }

    internal static void NativeEvaluationCannotAdmitNativeStorageOrManagedAddressEscapes()
    {
        MethodInfo address = AddressSource(); WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(address);
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableTypedProgram.Verify(graph, WarpPortableCliSizeContract.Capture(graph)));
        Assert.AreEqual("WRPCLR2200", error.Code, StringComparer.Ordinal); Assert.AreEqual(1, error.IlOffset);
        foreach (Type native in new[] { typeof(IntPtr), typeof(UIntPtr) })
        {
            TypeBuilder type = NewSource(); MethodBuilder method = type.DefineMethod("Native", MethodAttributes.Public | MethodAttributes.Static, native, [native]);
            ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
            WarpVerificationException signature = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableMethodGraph.Discover(type.CreateType()!.GetMethod("Native")!));
            Assert.AreEqual("WRPCLR2100", signature.Code, StringComparer.Ordinal);
        }
    }

    internal static void NativeCheckedFaultsRemainUnboundAtTheirOriginalOffsets()
    {
        foreach (OpCode operation in new[] { OpCodes.Add_Ovf, OpCodes.Add_Ovf_Un, OpCodes.Div_Un, OpCodes.Rem_Un })
        {
            MethodInfo source = MixedSource(operation, false); WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source);
            WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph, WarpPortableCliSizeContract.Capture(graph));
            WarpPortableTypedInstruction fault = typed.Methods.First(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal)).Instructions.First(item => item.OpCode == operation.Value);
            Assert.IsNotEmpty(fault.Faults);
            WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(graph, typed));
            Assert.AreEqual("WRPCLR2300", error.Code, StringComparer.Ordinal); Assert.AreEqual(fault.Offset, error.IlOffset);
        }
    }

    private static int[] Inputs() => [0, 1, -1, int.MinValue, int.MaxValue, 0x12345678, unchecked((int)0x876543FF)];

    private static Fixture Capture(MethodInfo source)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source); WarpPortableCliSizeContract cli = WarpPortableCliSizeContract.Capture(graph);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph, cli);
        return new(source, graph, typed, WarpPortableSourceHeapSchema.Create(graph, typed), WarpPortableWordLowerer.Lower(graph, typed));
    }

    private static ulong Execute(Fixture fixture, uint input)
    {
        var layout = new WarpLogicalMachineLayout(fixture.Lowered.Kernel); CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(64, 1000000); int quanta = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && quanta++ < 10000)
        {
            compiled.ExecuteQuantum([[input]], [], 0, state, 64, layout.MaximumBlockCost);
        }
        Assert.IsLessThan(10000, quanta); Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        int result = layout.GetResultWordOffset(0, 64); return state[result] | ((ulong)state[result + 1] << 32);
    }

    private static MethodInfo NativeSource(OpCode conversion, OpCode? operation)
    {
        TypeBuilder type = NewSource(); MethodBuilder source = Source(type); ILGenerator il = source.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(conversion); if (operation is { } unary) { il.Emit(unary); }
        il.Emit(OpCodes.Conv_U8); il.Emit(OpCodes.Ret); return Finish(type);
    }

    private static MethodInfo ShiftSource(OpCode operation, bool nativeCount)
    {
        TypeBuilder type = NewSource(); MethodBuilder source = Source(type); ILGenerator il = source.GetILGenerator();
        il.Emit(OpCodes.Ldc_I8, unchecked((long)0xFEDCBA9876543210UL)); il.Emit(OpCodes.Conv_U); il.Emit(OpCodes.Ldarg_0);
        if (nativeCount) { il.Emit(OpCodes.Conv_I); }
        il.Emit(operation); il.Emit(OpCodes.Conv_U8); il.Emit(OpCodes.Ret); return Finish(type);
    }

    private static MethodInfo MixedSource(OpCode operation, bool reverse)
    {
        TypeBuilder type = NewSource(); MethodBuilder source = Source(type); ILGenerator il = source.GetILGenerator();
        if (reverse) { il.Emit(OpCodes.Ldarg_0); }
        il.Emit(OpCodes.Ldc_I8, 0x100000000L); il.Emit(OpCodes.Conv_U); if (!reverse) { il.Emit(OpCodes.Ldarg_0); }
        il.Emit(operation); il.Emit(OpCodes.Conv_U8); il.Emit(OpCodes.Ret); return Finish(type);
    }

    private static MethodInfo BranchSource(OpCode operation, long nativeValue, bool reverse)
    {
        TypeBuilder type = NewSource(); MethodBuilder source = Source(type); ILGenerator il = source.GetILGenerator(); Label selected = il.DefineLabel();
        bool single = operation == OpCodes.Brtrue || operation == OpCodes.Brfalse || operation == OpCodes.Brtrue_S || operation == OpCodes.Brfalse_S;
        if (!single && reverse) { il.Emit(OpCodes.Ldarg_0); }
        il.Emit(OpCodes.Ldc_I8, nativeValue); il.Emit(OpCodes.Conv_U);
        if (!single && !reverse) { il.Emit(OpCodes.Ldarg_0); }
        il.Emit(operation, selected); il.Emit(OpCodes.Ldc_I8, 11L); il.Emit(OpCodes.Ret);
        il.MarkLabel(selected); il.Emit(OpCodes.Ldc_I8, 22L); il.Emit(OpCodes.Ret); return Finish(type);
    }

    private static MethodInfo StorageSource(Type local)
    {
        TypeBuilder type = NewSource(); MethodBuilder source = Source(type); ILGenerator il = source.GetILGenerator(); il.DeclareLocal(local);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Conv_U); il.Emit(OpCodes.Ldc_I4_S, (sbyte)32); il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Or); il.Emit(OpCodes.Stloc_0); il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Conv_U8); il.Emit(OpCodes.Ret);
        return Finish(type);
    }

    private static MethodInfo CallSource()
    {
        TypeBuilder type = NewSource(); MethodBuilder callee = type.DefineMethod("Truncate", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        ILGenerator helper = callee.GetILGenerator(); helper.Emit(OpCodes.Ldarg_0); helper.Emit(OpCodes.Ret);
        MethodBuilder source = Source(type); ILGenerator il = source.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Conv_U);
        il.Emit(OpCodes.Ldc_I4_S, (sbyte)32); il.Emit(OpCodes.Shl); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Call, callee); il.Emit(OpCodes.Conv_U8); il.Emit(OpCodes.Ret); return Finish(type);
    }

    private static MethodInfo LengthSource(bool conversion)
    {
        TypeBuilder type = NewSource(); MethodBuilder source = type.DefineMethod("Source", MethodAttributes.Public | MethodAttributes.Static,
            conversion ? typeof(int) : typeof(ulong), [typeof(int[])]); ILGenerator il = source.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldlen); il.Emit(conversion ? OpCodes.Conv_I4 : OpCodes.Conv_U8); il.Emit(OpCodes.Ret); return Finish(type);
    }

    private static MethodInfo AddressSource()
    {
        TypeBuilder type = NewSource(); MethodBuilder source = type.DefineMethod("Source", MethodAttributes.Public | MethodAttributes.Static, typeof(ulong), [typeof(object)]);
        ILGenerator il = source.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Conv_U); il.Emit(OpCodes.Conv_U8); il.Emit(OpCodes.Ret); return Finish(type);
    }

    private static MethodBuilder Source(TypeBuilder type) => type.DefineMethod("Source", MethodAttributes.Public | MethodAttributes.Static, typeof(ulong), [typeof(int)]);
    private static TypeBuilder NewSource() => AssemblyBuilder.DefineDynamicAssembly(new("WarpCliNativeWitness"), AssemblyBuilderAccess.RunAndCollect)
        .DefineDynamicModule("source").DefineType("Source", TypeAttributes.Public | TypeAttributes.Sealed);
    private static MethodInfo Finish(TypeBuilder type) => type.CreateType()!.GetMethod("Source")!;
    private sealed record Fixture(MethodInfo Source, WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed,
        WarpPortableSourceHeapSchema Schema, WarpPortableWordLoweredProgram Lowered);
}
