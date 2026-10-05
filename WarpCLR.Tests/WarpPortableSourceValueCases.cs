using System.Reflection;
using System.Runtime.InteropServices;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableSourceValueCases
{
    internal static void GeneratedPackedValueCopiesRetainEveryByteAndLeaveAdjacentFields()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema); uint[] owner = Allocate(schema, arena, typeof(Holder));
        WarpPortableSourceHeapField left = Field(schema, typeof(Holder), nameof(Holder.Left));
        WarpPortableSourceHeapField right = Field(schema, typeof(Holder), nameof(Holder.Right));
        uint[] expected = [0x345678A5, 0xFEDCBA12, 0xF9]; Scratch(arena, expected);
        var source = new Holder(new Packed(0xA5, unchecked((long)0xF9FEDCBA12345678UL)), default, default, false, null);
        Assert.AreEqual((byte)0xA5, source.Left.Tag); Assert.AreEqual(unchecked((long)0xF9FEDCBA12345678UL), source.Left.Wide);
        Assert.AreEqual(0u, Write(arena, owner, left, 0));
        Assert.AreEqual(0u, Read(arena, owner, left, 8));
        CollectionAssert.AreEqual(expected, ScratchWords(arena, 8, 3));
        Assert.AreEqual(0u, Read(arena, owner, right, 12));
        CollectionAssert.AreEqual(new uint[3], ScratchWords(arena, 12, 3));
        Assert.AreEqual(9, left.ByteSize);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.InitializeSourceValue), arena,
            [.. owner, (uint)left.HeapByteOffset + 1, 8, Id(schema, typeof(long))]));
        Assert.AreEqual(0u, Read(arena, owner, left, 8));
        CollectionAssert.AreEqual(new uint[] { 0xA5, 0, 0 }, ScratchWords(arena, 8, 3));
    }

    internal static void BooleanMemoryKeepsTheLowByteAndReadZeroesScratchPadding()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema); uint[] owner = Allocate(schema, arena, typeof(Holder));
        WarpPortableSourceHeapField flag = Field(schema, typeof(Holder), nameof(Holder.Flag));
        Scratch(arena, [0xAABBCCFE]);
        Assert.AreEqual(0u, Write(arena, owner, flag, 0));
        uint output = arena[WarpPortableHeapLayout.ScratchStart] + 4; arena[output] = uint.MaxValue;
        Assert.AreEqual(0u, Read(arena, owner, flag, 4));
        Assert.AreEqual(254u, arena[output]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.InitializeSourceValue), arena,
            [.. owner, (uint)flag.HeapByteOffset, 1, flag.FieldType]));
        Assert.AreEqual(0u, Read(arena, owner, flag, 4)); Assert.AreEqual(0u, arena[output]);
    }

    internal static void ReferenceAggregateWritesPrevalidateEveryOwnerBeforeAnyMutation()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema); uint[] holder = Allocate(schema, arena, typeof(Holder));
        uint[] first = Allocate(schema, arena, typeof(object)); uint[] second = Allocate(schema, arena, typeof(object));
        WarpPortableSourceHeapField pair = Field(schema, typeof(Holder), nameof(Holder.Pair));
        WarpPortableSourceHeapType type = schema.Types.First(type => type.Id == pair.FieldType);
        uint[] words = new uint[type.PayloadWords]; first.CopyTo(words, (int)type.References[0].Offset); second.CopyTo(words, (int)type.References[1].Offset);
        uint[] before = Payload(arena, holder);
        words[type.References[1].Offset]++; Scratch(arena, words);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        Assert.AreEqual(WarpPortableHeapLayout.WrongContext, Write(arena, holder, pair, 0));
        CollectionAssert.AreEqual(before, Payload(arena, holder));
        words[type.References[1].Offset]--; Scratch(arena, words);
        Assert.AreEqual(0u, Write(arena, holder, pair, 0)); Assert.AreEqual(0u, Read(arena, holder, pair, 16));
        CollectionAssert.AreEqual(words, ScratchWords(arena, 16, words.Length));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    }

    internal static void ReferenceAggregateLeaseIsRequiredForReadWriteAndInitialization()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema); uint[] holder = Allocate(schema, arena, typeof(Holder));
        WarpPortableSourceHeapField pair = Field(schema, typeof(Holder), nameof(Holder.Pair));
        Assert.AreEqual(WarpPortableHeapLayout.Busy, Read(arena, holder, pair, 0));
        Assert.AreEqual(WarpPortableHeapLayout.Busy, Write(arena, holder, pair, 0));
        Assert.AreEqual(WarpPortableHeapLayout.Busy, Service(nameof(WarpPortableHeapServices.InitializeSourceValue), arena,
            [.. holder, (uint)pair.HeapByteOffset, (uint)pair.ByteSize, pair.FieldType]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.InitializeSourceValue), arena,
            [.. holder, (uint)pair.HeapByteOffset, (uint)pair.ByteSize, pair.FieldType]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    }

    internal static void EqualWidthRetypingAndScratchBoundsFailBeforePayloadWrites()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema); uint[] holder = Allocate(schema, arena, typeof(Holder));
        WarpPortableSourceHeapField left = Field(schema, typeof(Holder), nameof(Holder.Left)); uint[] before = Payload(arena, holder);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, Service(nameof(WarpPortableHeapServices.WriteSourceValue), arena,
            [.. holder, (uint)left.HeapByteOffset + 1, 8, Id(schema, typeof(double)), 0]));
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, Write(arena, holder, left, arena[WarpPortableHeapLayout.ScratchWords] - 1));
        CollectionAssert.AreEqual(before, Payload(arena, holder));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, Write(arena, [holder[0], holder[1], holder[2] + 1], left, 0));
    }

    internal static void StaticValueStorageUsesExactDeclaredTypeAndPackedBytes()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        WarpPortableSourceHeapField field = Field(schema, typeof(Statics), nameof(Statics.Value));
        uint[] owner = [arena[WarpPortableHeapLayout.Context], 0, field.DeclaringType];
        Scratch(arena, [0x345678A5, 0xFEDCBA12, 0xF9]); Assert.AreEqual(0u, Write(arena, owner, field, 0));
        Assert.AreEqual(0u, Read(arena, owner, field, 8)); CollectionAssert.AreEqual(new uint[] { 0x345678A5, 0xFEDCBA12, 0xF9 }, ScratchWords(arena, 8, 3));
        Assert.AreEqual(WarpPortableHeapLayout.NullReference, Write(arena, [0, 0, 0], field, 0));
    }

    internal static void ReferenceCopiesRemainTracedAfterTheirSourceOperationCompletes()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema); uint[] holder = Allocate(schema, arena, typeof(Holder));
        uint[] value = Allocate(schema, arena, typeof(object)); WarpPortableSourceHeapField reference = Field(schema, typeof(Holder), nameof(Holder.Reference));
        Scratch(arena, value); Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, Write(arena, holder, reference, 0));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireRoot), arena, [.. holder, WarpPortableHeapLayout.StrongRoot, 0, 0, 0]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.RequestCollection), arena, []));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.Collect), arena, []));
        Assert.AreEqual(2u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, Read(arena, holder, reference, 8)); CollectionAssert.AreEqual(value, ScratchWords(arena, 8, 3));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    }

    internal static void ReadonlyCovariantReferenceViewKeepsTheActualOwnerAndType()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema); uint[] strings = AllocateArray(schema, arena, typeof(string[]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReadSourceValue), arena, [.. strings, 0, 12, Id(schema, typeof(object)), 0, 1]));
        CollectionAssert.AreEqual(new uint[3], ScratchWords(arena, 0, 3));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, Service(nameof(WarpPortableHeapServices.WriteSourceValue), arena, [.. strings, 0, 12, Id(schema, typeof(object)), 0]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    }

    private static WarpPortableSourceHeapSchema Schema()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Fixtures).GetMethod(nameof(Fixtures.Schema))!,
            concreteTypes: [typeof(Packed[]), typeof(string[])]);
        return WarpPortableSourceHeapSchema.Create(graph, WarpPortableTypedProgram.Verify(graph));
    }
    private static uint[] Arena(WarpPortableSourceHeapSchema schema) => schema.CreateArena(177, 4096, 32, 32, 2, 4096);
    private static uint Id(WarpPortableSourceHeapSchema schema, Type type) => schema.TypeId(WarpPortableMethodGraphIdentity.Type(type));
    private static WarpPortableSourceHeapField Field(WarpPortableSourceHeapSchema schema, Type type, string name) =>
        schema.Fields.First(field => string.Equals(field.Identity, WarpPortableMethodGraphIdentity.Field(type.GetField(name)!), StringComparison.Ordinal));
    private static void Scratch(uint[] arena, uint[] values) => values.CopyTo(arena, (int)arena[WarpPortableHeapLayout.ScratchStart]);
    private static uint[] ScratchWords(uint[] arena, uint offset, int count) => arena.AsSpan((int)(arena[WarpPortableHeapLayout.ScratchStart] + offset), count).ToArray();
    private static uint[] Payload(uint[] arena, uint[] owner)
    {
        uint slot = arena[WarpPortableHeapLayout.SlotStart] + (owner[1] - 1) * WarpPortableHeapLayout.SlotWords;
        return arena.AsSpan((int)arena[slot + WarpPortableHeapLayout.SlotPayload], (int)arena[slot + WarpPortableHeapLayout.SlotPayloadWords]).ToArray();
    }
    private static uint Write(uint[] arena, uint[] owner, WarpPortableSourceHeapField field, uint scratch) =>
        Service(nameof(WarpPortableHeapServices.WriteSourceValue), arena, [.. owner, (uint)field.HeapByteOffset, (uint)field.ByteSize, field.FieldType, scratch]);
    private static uint Read(uint[] arena, uint[] owner, WarpPortableSourceHeapField field, uint scratch) =>
        Service(nameof(WarpPortableHeapServices.ReadSourceValue), arena, [.. owner, (uint)field.HeapByteOffset, (uint)field.ByteSize, field.FieldType, scratch, 0]);
    private static uint[] Allocate(WarpPortableSourceHeapSchema schema, uint[] arena, Type type) => Allocation(arena, nameof(WarpPortableHeapServices.AllocateObject), [Id(schema, type)]);
    private static uint[] AllocateArray(WarpPortableSourceHeapSchema schema, uint[] arena, Type type) => Allocation(arena, nameof(WarpPortableHeapServices.AllocateArray), [Id(schema, type), 2]);
    private static uint[] Allocation(uint[] arena, string method, uint[] arguments)
    {
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, Service(method, arena, arguments)); uint[] result = arena.AsSpan((int)WarpPortableHeapLayout.Result, 3).ToArray();
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue])); return result;
    }
    private static uint Service(string name, uint[] arena, uint[] arguments)
    {
        MethodInfo method = typeof(WarpPortableHeapServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(method); CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(64, 10000000); uint[][] inputs = arguments.Length == 0 ? [new uint[1]] : arguments.Select(word => new[] { word }).ToArray();
        for (int quantum = 0; quantum < 100000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; quantum++)
        {
            compiled.ExecuteManagedQuantum(inputs, [], 0, state, 64, layout.MaximumBlockCost, arena);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]); return state[layout.GetResultWordOffset(0, 64)];
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Packed { public byte Tag; public long Wide; public Packed(byte tag, long wide) { Tag = tag; Wide = wide; } }
    private struct ReferencingValue { public object? First; public object? Second; public byte Tag; public ReferencingValue(object? first, object? second, byte tag) { First = first; Second = second; Tag = tag; } }
    private sealed class Holder
    {
        public Packed Left; public Packed Right; public ReferencingValue Pair; public bool Flag; public object? Reference;
        public Holder(Packed left, Packed right, ReferencingValue pair, bool flag, object? reference) { Left = left; Right = right; Pair = pair; Flag = flag; Reference = reference; }
    }
    private static class Statics { public static Packed Value = new(1, 2); }
    private static class Fixtures
    {
        public static long Schema(Packed value, Holder holder, ReferencingValue references, bool flag) =>
            value.Wide + holder.Left.Wide + Statics.Value.Wide + (references.First is null ? 0 : 1) + (flag ? 1 : 0);
    }
}
