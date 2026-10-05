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
internal sealed class WarpPortableSourceNullableTests
{
    [TestMethod]
    public void NullableAddressUnboxAllowsNullAndCapturesAnOwnedCopyAllocation()
    {
        MethodInfo entry = Dynamic("NullableAddress", typeof(int?), il =>
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Unbox, typeof(int?)); il.Emit(OpCodes.Ldobj, typeof(int?)); il.Emit(OpCodes.Ret);
        });
        Func<object?, int?> reference = entry.CreateDelegate<Func<object?, int?>>();
        Assert.IsNull(reference(null)); Assert.AreEqual(19, reference(19));
        (WarpPortableMethodGraph graph, WarpPortableTypedProgram typed) = Capture(entry);
        WarpPortableTypedInstruction unbox = typed.Methods.First(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal))
            .Instructions.First(instruction => instruction.OpCode == OpCodes.Unbox.Value);
        Assert.IsFalse(unbox.Faults.Any(fault => fault.Kind == WarpPortableTypedFaultKind.NullReference));
        Assert.IsTrue(unbox.Faults.Any(fault => fault.Kind == WarpPortableTypedFaultKind.InvalidCast));
        Assert.Contains(WarpPortableTypedEffect.Allocate, unbox.Effects);
        Assert.AreEqual(6, unbox.ExitStack[^1].WordCount);
        Assert.AreEqual(WarpPortableProvenanceKind.HeapInterior, unbox.ExitStack[^1].Provenance[0].Kind);
        _ = WarpPortableSourceHeapSchema.Create(graph, typed);
    }

    [TestMethod]
    public void NullableAddressCopyNeverAliasesTheOriginalUnderlyingBox()
    {
        MethodInfo entry = Dynamic("NullableCopy", typeof(int), il =>
        {
            LocalBuilder copy = il.DeclareLocal(typeof(int?).MakeByRefType());
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Unbox, typeof(int?)); il.Emit(OpCodes.Stloc, copy);
            il.Emit(OpCodes.Ldloc, copy); il.Emit(OpCodes.Ldc_I4, 7); il.Emit(OpCodes.Call, typeof(int?).GetConstructor([typeof(int)])!);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Unbox_Any, typeof(int)); il.Emit(OpCodes.Ret);
        });
        Assert.AreEqual(5, entry.CreateDelegate<Func<object, int>>()(5));
        WarpPortableSourceHeapSchema schema = Schema(typeof(Kernels).GetMethod(nameof(Kernels.Int))!);
        uint[] arena = schema.CreateArena(73, 512, 16, 32, 2, 512);
        Lease(arena); Seed(arena, [5, 0]);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.BoxValue), arena, [Id(schema, typeof(int)), 0]));
        uint[] original = Result(arena); Acknowledge(arena);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.MaterializeSourceNullable), arena, [.. original, Id(schema, typeof(int?))]));
        uint[] copyOwner = Result(arena); Acknowledge(arena);
        Assert.AreNotEqual(original[1], copyOwner[1]);
        WarpPortableSourceNullableLayout shape = schema.NullableLayouts.First(layout => layout.Type == Id(schema, typeof(int?)));
        for (uint part = 0; part < 4; part++)
        {
            Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.WriteSourceByte), arena,
                [.. copyOwner, shape.ValueByteOffset, 4, Id(schema, typeof(int)), part, part == 0 ? 7u : 0u]));
        }
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.UnboxValue), arena, [.. original, Id(schema, typeof(int)), 0]));
        Assert.AreEqual(5u, arena[arena[WarpPortableHeapLayout.ScratchStart]]);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReadSourceByte), arena,
            [.. copyOwner, shape.ValueByteOffset, 4, Id(schema, typeof(int)), 0, 0]));
        Assert.AreEqual(7u, arena[WarpPortableHeapLayout.Result]); Release(arena);
    }

    [TestMethod]
    public void NullMaterializationZerosHasValueAndEveryUnderlyingByte()
    {
        WarpPortableSourceHeapSchema schema = Schema(typeof(Kernels).GetMethod(nameof(Kernels.Int))!);
        uint[] arena = schema.CreateArena(73, 512, 16, 32, 2, 512); Lease(arena);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.MaterializeSourceNullable), arena, [0, 0, 0, Id(schema, typeof(int?))]));
        uint[] owner = Result(arena); Acknowledge(arena);
        WarpPortableSourceHeapType type = schema.Types.First(type => type.Id == Id(schema, typeof(int?)));
        for (uint part = 0; part < (uint)type.PayloadBytes; part++)
        {
            Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReadSourceByte), arena, [.. owner, 0, (uint)type.PayloadBytes, type.Id, part, 0]));
            Assert.AreEqual(0u, arena[WarpPortableHeapLayout.Result]);
        }
        Release(arena);
    }

    [TestMethod]
    public void NullableValueUnboxUsesDefaultWithoutAllocatingAndRejectsWrongBoxesBeforeMutation()
    {
        WarpPortableSourceHeapSchema schema = Schema(typeof(Kernels).GetMethod(nameof(Kernels.Int))!);
        uint[] arena = schema.CreateArena(73, 512, 16, 32, 2, 512); Lease(arena); Seed(arena, [uint.MaxValue, uint.MaxValue]);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.UnboxSourceNullableValue), arena, [0, 0, 0, Id(schema, typeof(int?)), 0]));
        Assert.AreEqual(0u, arena[arena[WarpPortableHeapLayout.ScratchStart]]); Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]);
        Seed(arena, [0x3F800000]);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.BoxValue), arena, [Id(schema, typeof(float)), 0]));
        uint[] wrong = Result(arena); Acknowledge(arena); Seed(arena, [0xDEADBEEF, 0x01234567]);
        uint objects = arena[WarpPortableHeapLayout.LiveObjects];
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, Heap(nameof(WarpPortableHeapServices.MaterializeSourceNullable), arena, [.. wrong, Id(schema, typeof(int?))]));
        Assert.AreEqual(objects, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, Heap(nameof(WarpPortableHeapServices.UnboxSourceNullableValue), arena, [.. wrong, Id(schema, typeof(int?)), 0]));
        Assert.AreEqual(0xDEADBEEFu, arena[arena[WarpPortableHeapLayout.ScratchStart]]); Release(arena);
    }

    [TestMethod]
    public void PackedNullableBoxCopiesAllEightIntegerBytesWithoutChangingTheType()
    {
        WarpPortableSourceHeapSchema schema = Schema(typeof(Kernels).GetMethod(nameof(Kernels.Packed))!);
        WarpPortableSourceNullableLayout shape = schema.NullableLayouts.First(layout => layout.Type == Id(schema, typeof(Small?)));
        Assert.AreEqual(1u, shape.ValueByteOffset);
        uint[] arena = schema.CreateArena(73, 512, 16, 32, 2, 512); Lease(arena);
        byte[] bytes = new byte[12]; bytes[shape.HasValueByteOffset] = 2; bytes[shape.ValueByteOffset] = 0xAB;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan((int)shape.ValueByteOffset + 1), 0xFEDCBA9876543210);
        Seed(arena, Enumerable.Range(0, 3).Select(word => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(word * 4))).ToArray());
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.BoxSourceNullableValue), arena, [shape.Type, 0]));
        uint[] owner = Result(arena); Acknowledge(arena);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.GetType), arena, owner)); Assert.AreEqual(shape.ElementType, arena[WarpPortableHeapLayout.Result]);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.UnboxSourceNullableValue), arena, [.. owner, shape.Type, 0]));
        uint scratch = arena[WarpPortableHeapLayout.ScratchStart];
        byte[] roundtrip = Enumerable.Range(0, 3).SelectMany(word => BitConverter.GetBytes(arena[scratch + (uint)word])).ToArray();
        Assert.AreEqual((byte)1, roundtrip[shape.HasValueByteOffset]);
        CollectionAssert.AreEqual(bytes.Skip((int)shape.ValueByteOffset).Take(9).ToArray(), roundtrip.Skip((int)shape.ValueByteOffset).Take(9).ToArray());
        Release(arena);
    }

    [TestMethod]
    public void NullableMaterializedReferenceFieldsRemainTracedFromTheCopyOwner()
    {
        WarpPortableSourceHeapSchema schema = Schema(typeof(Kernels).GetMethod(nameof(Kernels.Reference))!);
        uint[] arena = schema.CreateArena(73, 512, 16, 32, 2, 512); Lease(arena);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AllocateObject), arena, [Id(schema, typeof(object))]));
        uint[] related = Result(arena); Acknowledge(arena);
        WarpPortableSourceNullableLayout shape = schema.NullableLayouts.First(layout => layout.Type == Id(schema, typeof(ReferenceValue?)));
        uint[] scratch = new uint[5]; scratch[0] = 1; related.CopyTo(scratch, (int)shape.ValueByteOffset / 4); Seed(arena, scratch);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.BoxSourceNullableValue), arena, [shape.Type, 0]));
        uint[] original = Result(arena); Acknowledge(arena);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.MaterializeSourceNullable), arena, [.. original, shape.Type]));
        uint[] copy = Result(arena);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcquireRoot), arena, [.. copy, WarpPortableHeapLayout.StrongRoot, 0, 0, 0]));
        Acknowledge(arena); Release(arena);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.RequestCollection), arena, []));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.Collect), arena, []));
        Assert.AreEqual(2u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.GetType), arena, related)); Assert.AreEqual(Id(schema, typeof(object)), arena[WarpPortableHeapLayout.Result]);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, Heap(nameof(WarpPortableHeapServices.GetType), arena, original));
    }

    [TestMethod]
    public void NullableServicesRequireTheExactCapturedShapeAndOwnerLease()
    {
        WarpPortableSourceHeapSchema schema = Schema(typeof(Kernels).GetMethod(nameof(Kernels.Int))!);
        uint[] arena = schema.CreateArena(73, 512, 16, 32, 2, 512);
        Assert.AreEqual(WarpPortableHeapLayout.Busy, Heap(nameof(WarpPortableHeapServices.MaterializeSourceNullable), arena, [0, 0, 0, Id(schema, typeof(int?))]));
        Lease(arena);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidType, Heap(nameof(WarpPortableHeapServices.MaterializeSourceNullable), arena, [0, 0, 0, Id(schema, typeof(int))]));
        uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
        uint start = arena[descriptor + WarpPortableSourceMemoryLayout.NullableStart];
        arena[start + WarpPortableSourceMemoryLayout.NullableValueByte] = uint.MaxValue;
        Assert.AreEqual(WarpPortableHeapLayout.InvalidType, Heap(nameof(WarpPortableHeapServices.MaterializeSourceNullable), arena, [0, 0, 0, Id(schema, typeof(int?))]));
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]); Release(arena);
    }

    private static WarpPortableSourceHeapSchema Schema(MethodInfo entry)
    {
        (WarpPortableMethodGraph graph, WarpPortableTypedProgram typed) = Capture(entry); return WarpPortableSourceHeapSchema.Create(graph, typed);
    }
    private static (WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed) Capture(MethodInfo entry)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(entry, permittedAssemblies: [entry.Module.Assembly, typeof(WarpPortableSourceNullableTests).Assembly]);
        return (graph, WarpPortableTypedProgram.Verify(graph));
    }
    private static uint Id(WarpPortableSourceHeapSchema schema, Type type) => schema.TypeId(WarpPortableMethodGraphIdentity.Type(type));
    private static uint[] Result(uint[] arena) => arena.Skip((int)WarpPortableHeapLayout.Result).Take(3).ToArray();
    private static void Seed(uint[] arena, uint[] words) => words.CopyTo(arena, (int)arena[WarpPortableHeapLayout.ScratchStart]);
    private static void Lease(uint[] arena) => Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
    private static void Acknowledge(uint[] arena) => Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), arena, [uint.MaxValue]));
    private static void Release(uint[] arena) => Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    private static uint Heap(string name, uint[] arena, uint[] arguments)
    {
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableHeapServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout); uint[] state = layout.CreateInitialState(64, 10000000);
        for (int iteration = 0; iteration < 100000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; iteration++)
        {
            compiled.ExecuteManagedQuantum((arguments.Length == 0 ? [0u] : arguments).Select(word => new[] { word }).ToArray(), [], 0, state, 64, layout.MaximumBlockCost, arena);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]); return state[layout.GetResultWordOffset(0, 64)];
    }
    private static MethodInfo Dynamic(string name, Type result, Action<ILGenerator> emit)
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("NullableSource_" + name + "_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder type = assembly.DefineDynamicModule(name).DefineType(name, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, result, [typeof(object)]);
        emit(method.GetILGenerator()); return type.CreateType()!.GetMethod(name)!;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Small
    {
        public byte Tag;
        public long Wide;
        public Small(byte tag, long wide) { Tag = tag; Wide = wide; }
    }
    private struct ReferenceValue
    {
        public object? Related;
        public int Value;
        public ReferenceValue(object? related, int value) { Related = related; Value = value; }
    }
    private static class Kernels
    {
        public static int? Int(int? value) => value;
        public static Small? Packed(Small? value) => value;
        public static ReferenceValue? Reference(ReferenceValue? value) => value;
    }
}
