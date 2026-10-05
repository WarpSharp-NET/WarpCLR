using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest instantiates this fixture through reflection.")]
internal sealed class WarpPortableSourceHeapTests
{
    [TestMethod]
    public void SchemaIdentityAndPackedFieldsComeFromTheExactClosedTypedGraph()
    {
        (WarpPortableMethodGraph graph, WarpPortableTypedProgram typed) = Capture(nameof(Kernels.Fields));
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        Assert.AreEqual(schema.SchemaHash, WarpPortableSourceHeapSchema.Create(graph, typed).SchemaHash, StringComparer.Ordinal);
        Assert.AreEqual(graph.GraphHash, schema.GraphHash, StringComparer.Ordinal);
        uint[] ids = schema.Types.Select(type => type.Id).ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(1, ids.Length).Select(id => (uint)id).ToArray(), ids);
        WarpPortableSourceHeapField field = schema.Fields.First(item => item.Identity.Contains(nameof(Packed.Value), StringComparison.Ordinal));
        Assert.AreEqual(1, field.SourceByteOffset); Assert.AreEqual(1, field.HeapByteOffset); Assert.AreEqual(8, field.ByteSize);
        Assert.AreEqual(WarpPortableHeapLayout.Value, schema.Types[(int)field.DeclaringType - 1].Kind);
        (WarpPortableMethodGraph other, _) = Capture(nameof(Kernels.Cast));
        Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableSourceHeapSchema.Create(other, typed));
    }

    [TestMethod]
    public void ArrayCovarianceAndValueStrideAreSchemaBoundWithoutChangingNumberWidths()
    {
        (WarpPortableMethodGraph graph, WarpPortableTypedProgram typed) = Capture(nameof(Kernels.Arrays), [typeof(string[])]);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableSourceHeapType text = Find(schema, typeof(string[]));
        WarpPortableSourceHeapType objects = Find(schema, typeof(object[]));
        WarpPortableSourceHeapType bytes = Find(schema, typeof(byte[]));
        Assert.Contains(objects.Id, text.AssignableTo); Assert.DoesNotContain(objects.Id, bytes.AssignableTo);
        Assert.AreEqual(3u, text.ElementStrideWords); Assert.AreEqual(12, text.ElementByteSize);
        Assert.AreEqual(1, bytes.ElementByteSize); Assert.AreEqual(1u, bytes.ElementStrideWords);
        Assert.AreEqual(WarpPortableHeapLayout.Value, Find(schema, typeof(byte)).Kind);
    }

    [TestMethod]
    public void FaultTablesPreserveNullBoundsCovarianceAndCastKindsAtOriginalOffsets()
    {
        (WarpPortableMethodGraph graph, WarpPortableTypedProgram typed) = Capture(nameof(Kernels.Arrays));
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableTypedMethod body = typed.Methods.First(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal));
        WarpPortableTypedInstruction store = body.Instructions.First(item => item.OpCode == OpCodes.Stelem_Ref.Value);
        WarpPortableSourceHeapFault[] faults = schema.Faults.Where(fault => fault.SourceOffset == store.Offset && string.Equals(fault.MethodIdentity, body.Identity, StringComparison.Ordinal)).ToArray();
        CollectionAssert.AreEqual(new[] { WarpPortableTypedFaultKind.NullReference, WarpPortableTypedFaultKind.IndexOutOfRange, WarpPortableTypedFaultKind.ArrayTypeMismatch }, faults.Select(fault => fault.Kind).ToArray());
        Assert.AreEqual(Find(schema, typeof(ArrayTypeMismatchException)).Id, faults[^1].ExceptionType);
        (WarpPortableMethodGraph castGraph, WarpPortableTypedProgram castTyped) = Capture(nameof(Kernels.Cast));
        WarpPortableSourceHeapSchema castSchema = WarpPortableSourceHeapSchema.Create(castGraph, castTyped);
        Assert.IsTrue(castSchema.Faults.Any(fault => fault.Kind == WarpPortableTypedFaultKind.InvalidCast && fault.ExceptionType == Find(castSchema, typeof(InvalidCastException)).Id));
    }

    [TestMethod]
    public void ExceptionRecordPrefixKeepsUserFieldsAndAllHiddenRootsDisjoint()
    {
        (WarpPortableMethodGraph graph, WarpPortableTypedProgram typed) = Capture(nameof(Kernels.NewError));
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableSourceHeapType error = Find(schema, typeof(SourceError));
        Assert.IsTrue(error.ExceptionRecord);
        foreach (WarpPortableSourceHeapField field in schema.Fields.Where(field => field.DeclaringType == error.Id && !field.IsStatic))
        {
            Assert.AreEqual(field.SourceByteOffset + WarpPortableSourceExceptionLayout.PrefixBytes, field.HeapByteOffset);
        }
        Assert.Contains(new WarpPortableHeapReferenceLayout(0, Find(schema, typeof(string)).Id), error.References);
        Assert.Contains(new WarpPortableHeapReferenceLayout(3, Find(schema, typeof(Exception)).Id), error.References);
        Assert.Contains(new WarpPortableHeapReferenceLayout(6, Find(schema, typeof(uint[])).Id), error.References);
        Assert.IsTrue(schema.Faults.Any(fault => fault.Kind == WarpPortableTypedFaultKind.AllocationQuota && fault.UncatchableRuntimeTermination));
    }

    [TestMethod]
    public void DiscoveryAndArenaCreationNeverInvokeSourceTypeInitializers()
    {
        Assert.AreEqual(0, Probe.Calls);
        (WarpPortableMethodGraph graph, WarpPortableTypedProgram typed) = Capture(nameof(Kernels.Initialized));
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        Assert.IsNotNull(Find(schema, typeof(Initialized)).Initializer);
        _ = schema.CreateArena(91, 512, 8, 16, 2, 512);
        Assert.AreEqual(0, Probe.Calls);
    }

    [TestMethod]
    public void CompiledValueFieldsExtractUnalignedWideAndBooleanBytesExactly()
    {
        byte[] bytes = new byte[12]; bytes[0] = 0xAB; bytes[9] = 2;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(1), 0x0123456789ABCDEF);
        uint[] input = Enumerable.Range(0, 3).Select(word => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(word * 4))).ToArray();
        MethodInfo wide = Getter(typeof(Packed), typeof(long), nameof(Packed.Value));
        MethodInfo boolean = Getter(typeof(Packed), typeof(bool), nameof(Packed.Flag));
        CollectionAssert.AreEqual(new uint[] { 0x89ABCDEF, 0x01234567 }, Run(Lower(wide), input));
        CollectionAssert.AreEqual(new uint[] { 2 }, Run(Lower(boolean), input));
    }

    [TestMethod]
    public void BoxedValueMergesKeepAThreeWordReferenceAndItsRoot()
    {
        MethodInfo entry = Dynamic("BoxMerge", typeof(object), [typeof(int)], il =>
        {
            Label other = il.DefineLabel(); Label merge = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brtrue, other);
            il.Emit(OpCodes.Ldc_I4, 17); il.Emit(OpCodes.Box, typeof(int)); il.Emit(OpCodes.Br, merge);
            il.MarkLabel(other); il.Emit(OpCodes.Ldc_I4, 23); il.Emit(OpCodes.Box, typeof(int));
            il.MarkLabel(merge); il.Emit(OpCodes.Ret);
        });
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(entry);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableTypedInstruction result = typed.Methods[0].Instructions.First(item => item.OpCode == OpCodes.Ret.Value);
        Assert.AreEqual(WarpPortableStackCategory.Reference, result.EntryStack[0].Category);
        Assert.AreEqual(3, result.EntryStack[0].WordCount);
        Assert.IsTrue(result.Roots.Any(root => root.Storage is "stack" && root.WordOffset == 0));
        WarpVerificationException unbound = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(graph, typed));
        Assert.AreEqual("WRPCLR2300", unbound.Code, StringComparer.Ordinal);
        Assert.IsFalse(unbound.Message.Contains("root extends", StringComparison.Ordinal));
    }

    [TestMethod]
    public void NullableBoxShapeAndNullUnboxFaultsMatchTheUnderlyingValueContract()
    {
        MethodInfo entry = Dynamic("NullableBox", typeof(object), [typeof(int?)], il => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Box, typeof(int?)); il.Emit(OpCodes.Ret); });
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(entry);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableTypedMethod body = typed.Methods.First(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal));
        WarpPortableTypedInstruction box = body.Instructions.First(item => item.OpCode == OpCodes.Box.Value);
        Assert.AreEqual(WarpPortableMethodGraphIdentity.Type(typeof(int)), box.ExitStack[^1].TypeIdentity, StringComparer.Ordinal);
        Assert.AreEqual(3, box.ExitStack[^1].WordCount);
        (WarpPortableMethodGraph unboxGraph, WarpPortableTypedProgram unboxTyped) = Capture(nameof(Kernels.NullableUnbox));
        WarpPortableTypedInstruction unbox = unboxTyped.Methods.First(method => string.Equals(method.Identity, unboxGraph.EntryIdentity, StringComparison.Ordinal)).Instructions.First(item => item.OpCode == OpCodes.Unbox_Any.Value);
        Assert.IsFalse(unbox.Faults.Any(fault => fault.Kind == WarpPortableTypedFaultKind.NullReference));
        Assert.IsTrue(unbox.Faults.Any(fault => fault.Kind == WarpPortableTypedFaultKind.InvalidCast));
        _ = WarpPortableSourceHeapSchema.Create(graph, typed); _ = WarpPortableSourceHeapSchema.Create(unboxGraph, unboxTyped);
    }

    [TestMethod]
    public void ThrowNullHasItsOwnCatchableOriginalOffsetBinding()
    {
        MethodInfo entry = Dynamic("ThrowNull", typeof(void), Array.Empty<Type>(), il => { il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Throw); });
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(entry);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableSourceHeapFault fault = schema.Faults.First(item => item.Kind == WarpPortableTypedFaultKind.NullReference);
        Assert.AreEqual(OpCodes.Throw.Value, fault.SourceOpCode); Assert.AreEqual(1, fault.SourceOffset);
        Assert.AreEqual(Find(schema, typeof(NullReferenceException)).Id, fault.ExceptionType);
        Assert.IsFalse(fault.UncatchableRuntimeTermination);
    }

    [TestMethod]
    public void ManagedByrefWritesRequireOwnerLeasesEvenWithoutAnArenaOpcode()
    {
        (WarpPortableMethodGraph graph, WarpPortableTypedProgram typed) = Capture(nameof(Kernels.WriteReference));
        WarpPortableTypedMethod body = typed.Methods.First(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal));
        WarpPortableTypedInstruction write = body.Instructions.First(item => item.OpCode == OpCodes.Stind_Ref.Value);
        WarpPortableSourceOperationMetadata operation = WarpPortableSourceOperationCatalog.Describe(body.Identity, "stind.ref", write, typed.Types.ToDictionary(type => type.Identity, StringComparer.Ordinal));
        Assert.IsTrue(operation.WritesOwnerReferences); Assert.IsTrue(operation.RequiresLease); Assert.IsTrue(operation.MayTargetCallerFrame);
        Assert.HasCount(1, operation.MemoryOwnerByteOffsets); Assert.AreEqual(0, operation.MemoryOwnerByteOffsets[0]);
        Assert.IsTrue(operation.DestinationProvenance.Any(origin => origin.Kind == WarpPortableProvenanceKind.Argument));
    }

    [TestMethod]
    public void GeneratedArenaServicesUseTheCapturedSchemaAndPersistentRealObjectReferences()
    {
        (WarpPortableMethodGraph graph, WarpPortableTypedProgram typed) = Capture(nameof(Kernels.Fields));
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        uint[] arena = schema.CreateArena(91, 512, 8, 16, 2, 512);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AllocateObject), arena, [Find(schema, typeof(Owner)).Id]));
        uint[] reference = arena.Skip((int)WarpPortableHeapLayout.Result).Take(3).ToArray();
        Assert.AreEqual(91u, reference[0]); Assert.AreNotEqual(0u, reference[1]); Assert.AreNotEqual(0u, reference[2]);
        WarpPortableSourceHeapField field = schema.Fields.First(item => item.Identity.Contains(nameof(Owner.Wide), StringComparison.Ordinal));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.WriteWord), arena, [.. reference, (uint)field.HeapByteOffset / 4, 0x89ABCDEF]));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.WriteWord), arena, [.. reference, (uint)field.HeapByteOffset / 4 + 1, 0x01234567]));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReadWord), arena, [.. reference, (uint)field.HeapByteOffset / 4]));
        Assert.AreEqual(0x89ABCDEFu, arena[WarpPortableHeapLayout.Result]);
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
    }

    private static WarpPortableSourceHeapType Find(WarpPortableSourceHeapSchema schema, Type type) => schema.Types.First(item => string.Equals(item.Identity, WarpPortableMethodGraphIdentity.Type(type), StringComparison.Ordinal));
    private static (WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed) Capture(string name, Type[]? concrete = null)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Kernels).GetMethod(name)!, concreteTypes: concrete);
        return (graph, WarpPortableTypedProgram.Verify(graph));
    }
    private static WarpPortableWordLoweredProgram Lower(MethodInfo entry)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(entry,
            permittedAssemblies: [entry.Module.Assembly, typeof(WarpPortableSourceHeapTests).Assembly]);
        return WarpPortableWordLowerer.Lower(graph, WarpPortableTypedProgram.Verify(graph));
    }
    private static MethodInfo Getter(Type owner, Type result, string field) => Dynamic("Field_" + field, result, [owner], il => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, owner.GetField(field)!); il.Emit(OpCodes.Ret); });
    private static uint[] Run(WarpPortableWordLoweredProgram program, uint[] inputs)
    {
        var layout = new WarpLogicalMachineLayout(program.Kernel); uint[] state = layout.CreateInitialState(32, 100000);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        for (int iteration = 0; iteration < 1000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; iteration++)
        {
            compiled.ExecuteQuantum(inputs.Select(word => new[] { word }).ToArray(), [], 0, state, 32, layout.MaximumBlockCost);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return Enumerable.Range(0, layout.ResultWordCount).Select(word => state[layout.GetResultWordOffset(word, 32)]).ToArray();
    }
    private static uint Heap(string name, uint[] arena, uint[] arguments)
    {
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableHeapServices).GetMethod(name)!);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout); uint[] state = layout.CreateInitialState(32, 100000);
        for (int iteration = 0; iteration < 10000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; iteration++)
        {
            compiled.ExecuteManagedQuantum(arguments.Select(word => new[] { word }).ToArray(), [], 0, state, 32, layout.MaximumBlockCost, arena);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]); return state[layout.GetResultWordOffset(0, 32)];
    }
    private static MethodInfo Dynamic(string name, Type result, Type[] parameters, Action<ILGenerator> emit)
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("HeapSource_" + name + "_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder type = assembly.DefineDynamicModule(name).DefineType(name, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, result, parameters);
        emit(method.GetILGenerator()); return type.CreateType()!.GetMethod(name)!;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Packed
    {
        public byte Tag;
        public long Value;
        public bool Flag;
        public Packed(byte tag, long value, bool flag) { Tag = tag; Value = value; Flag = flag; }
    }
    private sealed class Owner
    {
        public long Wide;
        public Packed Data;
        public Owner() { Wide = 0; Data = new(1, 2, true); }
    }
    private sealed class SourceError : Exception
    {
        public object? Related;
        public SourceError() { }
        public SourceError(string message) : base(message) { }
        public SourceError(string message, Exception innerException) : base(message, innerException) { }
        public SourceError(object related) : base("source") { Related = related; }
    }
    private static class Probe { public static int Calls; }
    private static class Initialized
    {
        public static readonly Owner Value = Create();
        private static Owner Create()
        {
            Probe.Calls++; return new();
        }
    }
    private static class Kernels
    {
        public static long Fields(Owner value) => value.Wide + value.Data.Tag + value.Data.Value + (value.Data.Flag ? 1 : 0);
        public static object Arrays(object[] values, byte[] bytes, string text, int index) { values[index] = text; bytes[index] = 2; return values[index]; }
        public static Owner Cast(object value) => (Owner)value;
        public static SourceError NewError(object related) => new(related);
        public static Owner Initialized() => WarpPortableSourceHeapTests.Initialized.Value;
        public static int? NullableUnbox(object? value) => (int?)value;
        public static void WriteReference(ref object destination, object value) => destination = value;
    }
}
