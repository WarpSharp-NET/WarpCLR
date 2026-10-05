using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest instantiates this fixture through reflection.")]
internal sealed class WarpPortableSourceReadonlyTests
{
    [TestMethod]
    public void ReadonlyReferenceArrayAddressPermitsCovarianceAndCapturesControlledMutability()
    {
        MethodInfo entry = ReferenceAddress(readOnly: true);
        object[] actual = new string[] { "exact" };
        Assert.AreEqual("exact", entry.CreateDelegate<Func<object[], object>>()(actual));
        WarpPortableTypedProgram typed = Verify(entry, [typeof(string[])]);
        WarpPortableTypedInstruction address = Entry(typed, entry).Instructions.First(instruction => instruction.OpCode == OpCodes.Ldelema.Value);
        Assert.IsTrue(address.ExitStack[^1].IsReadOnly); Assert.IsTrue(address.ExitStack[^1].ControlledMutability);
        Assert.IsFalse(address.Faults.Any(fault => fault.Kind == WarpPortableTypedFaultKind.ArrayTypeMismatch));
    }

    [TestMethod]
    public void WritableReferenceArrayAddressChecksTheExactRuntimeElementType()
    {
        MethodInfo entry = ReferenceAddress(readOnly: false);
        object[] actual = new string[] { "exact" };
        Assert.ThrowsExactly<ArrayTypeMismatchException>(() => entry.CreateDelegate<Func<object[], object>>()(actual));
        WarpPortableTypedInstruction address = Entry(Verify(entry, [typeof(string[])]), entry).Instructions.First(instruction => instruction.OpCode == OpCodes.Ldelema.Value);
        Assert.IsTrue(address.Faults.Any(fault => fault.Kind == WarpPortableTypedFaultKind.ArrayTypeMismatch));
    }

    [TestMethod]
    public void ControlledArrayValueReceiverCallsTheRealMutatingInstanceMethod()
    {
        MethodInfo entry = CounterEntry(storeDirectly: false);
        Assert.AreEqual(1, entry.CreateDelegate<Func<int>>()());
        WarpPortableTypedProgram typed = Verify(entry);
        WarpPortableTypedInstruction call = Entry(typed, entry).Instructions.First(instruction => instruction.OpCode == OpCodes.Call.Value);
        Assert.IsTrue(call.EntryStack[^1].ControlledMutability);
        Assert.IsTrue(typed.Methods.Any(method => method.Identity.Contains("Increment", StringComparison.Ordinal) && method.Instructions.Any(instruction => instruction.OpCode == OpCodes.Stfld.Value)));
    }

    [TestMethod]
    public void ControlledArrayAddressStillRejectsADirectFieldWrite()
    {
        MethodInfo entry = CounterEntry(storeDirectly: true);
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => Verify(entry));
        Assert.AreEqual("WRPCLR2200", error.Code, StringComparer.Ordinal);
        Assert.IsTrue(error.Message.Contains("readonly", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReadonlyPrefixAlsoAppliesToTheSpecialMultidimensionalAddressCall()
    {
        TypeBuilder type = DynamicType("ReadonlyMdAddress");
        MethodBuilder method = type.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Newobj, typeof(int[,]).GetConstructor([typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Readonly);
        il.Emit(OpCodes.Call, typeof(int[,]).GetMethod("Address")!); il.Emit(OpCodes.Ldind_I4); il.Emit(OpCodes.Ret);
        MethodInfo entry = type.CreateType()!.GetMethod("Entry")!;
        Assert.AreEqual(0, entry.CreateDelegate<Func<int>>()());
        WarpPortableTypedInstruction address = Entry(Verify(entry), entry).Instructions.First(instruction => instruction.OpCode == OpCodes.Call.Value);
        Assert.IsTrue(address.ExitStack[^1].ControlledMutability); Assert.IsTrue(address.ExitStack[^1].IsReadOnly);
    }

    private static MethodInfo ReferenceAddress(bool readOnly)
    {
        TypeBuilder type = DynamicType("ReferenceAddress");
        MethodBuilder method = type.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object[])]);
        ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_0);
        if (readOnly) { il.Emit(OpCodes.Readonly); }
        il.Emit(OpCodes.Ldelema, typeof(object)); il.Emit(OpCodes.Ldind_Ref); il.Emit(OpCodes.Ret);
        return type.CreateType()!.GetMethod("Entry")!;
    }

    private static MethodInfo CounterEntry(bool storeDirectly)
    {
        TypeBuilder root = DynamicType("ControlledValue");
        ModuleBuilder module = (ModuleBuilder)root.Module;
        TypeBuilder counter = module.DefineType("Counter", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, typeof(ValueType));
        FieldBuilder count = counter.DefineField("Count", typeof(int), FieldAttributes.Public);
        MethodBuilder increment = counter.DefineMethod("Increment", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        ILGenerator body = increment.GetILGenerator(); body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Dup); body.Emit(OpCodes.Ldfld, count);
        body.Emit(OpCodes.Ldc_I4_1); body.Emit(OpCodes.Add); body.Emit(OpCodes.Stfld, count); body.Emit(OpCodes.Ret);
        Type value = counter.CreateType()!;
        MethodBuilder method = root.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Newarr, value); il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Readonly); il.Emit(OpCodes.Ldelema, value);
        if (storeDirectly) { il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stfld, value.GetField("Count")!); }
        else { il.Emit(OpCodes.Call, value.GetMethod("Increment")!); }
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelema, value); il.Emit(OpCodes.Ldfld, value.GetField("Count")!); il.Emit(OpCodes.Ret);
        return root.CreateType()!.GetMethod("Entry")!;
    }

    private static TypeBuilder DynamicType(string name)
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name + "_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        return assembly.DefineDynamicModule(name).DefineType("Kernels", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
    }
    private static WarpPortableTypedProgram Verify(MethodInfo entry, Type[]? concrete = null) => WarpPortableTypedProgram.Verify(
        WarpPortableMethodGraph.Discover(entry, concreteTypes: concrete, permittedAssemblies: [entry.Module.Assembly]));
    private static WarpPortableTypedMethod Entry(WarpPortableTypedProgram program, MethodInfo entry) =>
        program.Methods.First(method => string.Equals(method.Identity, WarpPortableMethodGraphIdentity.Method(entry), StringComparison.Ordinal));
}
