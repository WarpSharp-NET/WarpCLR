using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture through reflection.")]
internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void VectorAndRankOneMultidimensionalArraysCannotShareATypeIdentity()
    {
        string vector = WarpPortableMethodGraphIdentity.Type(typeof(int[]));
        string bounded = WarpPortableMethodGraphIdentity.Type(typeof(int).MakeArrayType(1));
        Assert.AreNotEqual(vector, bounded, StringComparer.Ordinal);
        Assert.EndsWith("[]", vector, StringComparison.Ordinal); Assert.EndsWith("[*]", bounded, StringComparison.Ordinal);
    }

    [TestMethod]
    public void CapturedMemoryViewsBindNestedPackedBytesAndExactStaticFields()
    {
        WarpPortableSourceHeapSchema schema = Schema();
        WarpPortableSourceHeapField packed = Field(schema, typeof(Holder), nameof(Holder.Value));
        Assert.Contains(new WarpPortableSourceMemoryView(packed.DeclaringType, WarpPortableSourceMemoryLayout.Instance, (uint)packed.HeapByteOffset + 1, 8, Id(schema, typeof(long))), schema.MemoryViews);
        WarpPortableSourceHeapField global = Field(schema, typeof(Statics), nameof(Statics.Value));
        Assert.Contains(new WarpPortableSourceMemoryView(global.DeclaringType, WarpPortableSourceMemoryLayout.Static, (uint)global.HeapByteOffset + 1, 8, Id(schema, typeof(long))), schema.MemoryViews);
        uint[] arena = schema.CreateArena(71, 512, 16, 16, 2, 512);
        uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
        Assert.AreEqual(WarpPortableSourceMemoryLayout.Magic, arena[descriptor]);
        Assert.AreEqual((uint)schema.MemoryViews.Length, arena[descriptor + WarpPortableSourceMemoryLayout.ViewCount]);
        Assert.IsLessThan(arena[WarpPortableHeapLayout.DataStart], descriptor);
    }

    [TestMethod]
    public void GeneratedHeapByteViewsRejectEqualWidthTypeConfusionAndStaleOwners()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        uint[] owner = Allocate(schema, arena, typeof(Holder));
        WarpPortableSourceHeapField field = Field(schema, typeof(Holder), nameof(Holder.Value));
        uint offset = (uint)field.HeapByteOffset + 1;
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ValidateSourceByteOwner), arena, [.. owner, offset, 8, Id(schema, typeof(long)), 0]));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, Heap(nameof(WarpPortableHeapServices.ValidateSourceByteOwner), arena, [.. owner, offset, 8, Id(schema, typeof(double)), 0]));
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, Heap(nameof(WarpPortableHeapServices.ValidateSourceByteOwner), arena, [.. owner, offset, 4, Id(schema, typeof(long)), 0]));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, Heap(nameof(WarpPortableHeapServices.ValidateSourceByteOwner), arena, [owner[0], owner[1], owner[2] + 1, offset, 8, Id(schema, typeof(long)), 0]));
        Assert.AreEqual(WarpPortableHeapLayout.WrongContext, Heap(nameof(WarpPortableHeapServices.ValidateSourceByteOwner), arena, [owner[0] + 1, owner[1], owner[2], offset, 8, Id(schema, typeof(long)), 0]));
    }

    [TestMethod]
    public void GeneratedStaticByteOwnersAreDistinctFromNullAndRespectDeclaredStorage()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        WarpPortableSourceHeapField field = Field(schema, typeof(Statics), nameof(Statics.Value));
        uint[] owner = [arena[WarpPortableHeapLayout.Context], 0, field.DeclaringType];
        uint offset = (uint)field.HeapByteOffset + 1;
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ValidateSourceByteOwner), arena, [.. owner, offset, 8, Id(schema, typeof(long)), 0]));
        Assert.AreEqual(WarpPortableHeapLayout.NullReference, Heap(nameof(WarpPortableHeapServices.ValidateSourceByteOwner), arena, [0, 0, 0, offset, 8, Id(schema, typeof(long)), 0]));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, Heap(nameof(WarpPortableHeapServices.ValidateSourceByteOwner), arena, [.. owner, offset, 8, Id(schema, typeof(double)), 0]));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.WriteSourceByte), arena, [.. owner, offset, 8, Id(schema, typeof(long)), 7, 0xABCDEF01]));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReadSourceByte), arena, [.. owner, offset, 8, Id(schema, typeof(long)), 7, 0]));
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.Result]);
    }

    [TestMethod]
    public void GeneratedByteWritesKeepPackedNeighboursAndEveryWideBit()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema); uint[] owner = Allocate(schema, arena, typeof(Holder));
        WarpPortableSourceHeapField field = Field(schema, typeof(Holder), nameof(Holder.Value));
        uint start = (uint)field.HeapByteOffset;
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.WriteSourceByte), arena, [.. owner, start, 1, Id(schema, typeof(byte)), 0, 0xAB]));
        ulong bits = 0x0123456789ABCDEF;
        for (uint index = 0; index < 8; index++)
        {
            Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.WriteSourceByte), arena, [.. owner, start + 1, 8, Id(schema, typeof(long)), index, (uint)(bits >> (int)(index * 8))]));
            Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReadSourceByte), arena, [.. owner, start + 1, 8, Id(schema, typeof(long)), index, 0]));
            Assert.AreEqual((uint)(bits >> (int)(index * 8)) & 255u, arena[WarpPortableHeapLayout.Result]);
        }
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReadSourceByte), arena, [.. owner, start, 1, Id(schema, typeof(byte)), 0, 0]));
        Assert.AreEqual(0xABu, arena[WarpPortableHeapLayout.Result]);
    }

    [TestMethod]
    public void ReadonlyArrayViewsAllowReferenceCovarianceButWritableViewsRejectIt()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AllocateArray), arena, [Id(schema, typeof(string[])), 2]));
        uint[] owner = arena.Skip((int)WarpPortableHeapLayout.Result).Take(3).ToArray();
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ValidateSourceByteOwner), arena, [.. owner, 12, 12, Id(schema, typeof(string)), 0]));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ValidateSourceByteOwner), arena, [.. owner, 12, 12, Id(schema, typeof(object)), 1]));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, Heap(nameof(WarpPortableHeapServices.ValidateSourceByteOwner), arena, [.. owner, 12, 12, Id(schema, typeof(object)), 0]));
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, Heap(nameof(WarpPortableHeapServices.ValidateSourceByteOwner), arena, [.. owner, 24, 12, Id(schema, typeof(object)), 1]));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, Heap(nameof(WarpPortableHeapServices.ValidateSourceByteOwner), arena, [.. owner, 1, 12, Id(schema, typeof(object)), 1]));
    }

    [TestMethod]
    public void ReferenceBytesRequireAnOperationLeaseBeforeTheyCanTearAnOwner()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema); uint[] owner = Allocate(schema, arena, typeof(Holder));
        WarpPortableSourceHeapField field = Field(schema, typeof(Holder), nameof(Holder.Reference));
        Assert.AreEqual(WarpPortableHeapLayout.Busy, Heap(nameof(WarpPortableHeapServices.WriteSourceByte), arena, [.. owner, (uint)field.HeapByteOffset, 12, Id(schema, typeof(object)), 0, 0]));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.WriteSourceByte), arena, [.. owner, (uint)field.HeapByteOffset, 12, Id(schema, typeof(object)), 0, 0]));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    }

    [TestMethod]
    public void CorruptMemoryDescriptorsFailBeforeAnyPayloadAccess()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema); uint[] owner = Allocate(schema, arena, typeof(Holder));
        uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
        arena[descriptor + WarpPortableSourceMemoryLayout.ViewStart] = uint.MaxValue;
        Assert.AreEqual(WarpPortableHeapLayout.InvalidType, Heap(nameof(WarpPortableHeapServices.ValidateSourceByteOwner), arena, [.. owner, 0, 1, Id(schema, typeof(byte)), 0]));
    }

    [TestMethod]
    public void GeneratedReferenceHashDependsOnLogicalIdentityAndNeverTheContextNonce()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] first = Arena(schema); uint[] second = schema.CreateArena(999, 512, 16, 16, 2, 512);
        uint[] left = Allocate(schema, first, typeof(Holder)); uint[] right = Allocate(schema, second, typeof(Holder));
        Assert.AreNotEqual(left[0], right[0]);
        uint a = Hash(left[1], left[2]); uint b = Hash(right[1], right[2]);
        Assert.AreEqual(a, b); Assert.AreEqual(a, Hash(left[1], left[2])); Assert.AreEqual(0u, Hash(0, 0));
        Assert.AreNotEqual(a, Hash(left[1], left[2] + 1));
    }

    private static WarpPortableSourceHeapSchema Schema()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Fixtures).GetMethod(nameof(Fixtures.Schema))!);
        return WarpPortableSourceHeapSchema.Create(graph, WarpPortableTypedProgram.Verify(graph));
    }
    private static uint[] Arena(WarpPortableSourceHeapSchema schema) => schema.CreateArena(71, 512, 16, 16, 2, 512);
    private static uint Id(WarpPortableSourceHeapSchema schema, Type type) => schema.TypeId(WarpPortableMethodGraphIdentity.Type(type));
    private static WarpPortableSourceHeapField Field(WarpPortableSourceHeapSchema schema, Type owner, string name) => schema.Fields.First(field =>
        string.Equals(field.Identity, WarpPortableMethodGraphIdentity.Field(owner.GetField(name)!), StringComparison.Ordinal));
    private static uint[] Allocate(WarpPortableSourceHeapSchema schema, uint[] arena, Type type)
    {
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AllocateObject), arena, [Id(schema, type)]));
        return arena.Skip((int)WarpPortableHeapLayout.Result).Take(3).ToArray();
    }
    private static uint Heap(string service, uint[] arena, uint[] arguments) => Execute(WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableHeapServices).GetMethod(service)!), arguments, arena);
    private static uint Hash(uint slot, uint generation)
    {
        WarpIntegerMapKernel verified = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(typeof(WarpPortableSourceIdentity).GetMethod(nameof(WarpPortableSourceIdentity.Hash))!, 2));
        return Execute(new(verified.ControlFlow), [slot, generation], null);
    }
    private static uint Execute(WarpLogicalMachineLayout layout, uint[] arguments, uint[]? arena)
    {
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout); uint[] state = layout.CreateInitialState(64, 10000000);
        for (int count = 0; count < 100000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; count++)
        {
            if (arena is null) { compiled.ExecuteQuantum(arguments.Select(word => new[] { word }).ToArray(), [], 0, state, 64, layout.MaximumBlockCost); }
            else { compiled.ExecuteManagedQuantum(arguments.Select(word => new[] { word }).ToArray(), [], 0, state, 64, layout.MaximumBlockCost, arena); }
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return state[layout.GetResultWordOffset(0, 64)];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Packed
    {
        public byte Tag;
        public long Wide;
        public bool Flag;
        public Packed(byte tag, long wide, bool flag) { Tag = tag; Wide = wide; Flag = flag; }
    }
    private sealed class Holder
    {
        public Packed Value;
        public object? Reference;
        public Holder(Packed value, object? reference) { Value = value; Reference = reference; }
    }
    private static class Statics { public static Packed Value = new(1, 2, false); }
    private static class Fixtures
    {
        public static long Schema(Holder[] holders, byte[] bytes, object[] objects, string[] strings, ref Packed local) =>
            holders[0].Value.Wide + holders[0].Value.Tag + (holders[0].Value.Flag ? 1 : 0) +
            (holders[0].Reference is null ? 0 : 1) + bytes[0] + (objects[0] is null ? 0 : 1) + (strings[0] is null ? 0 : 1) + Statics.Value.Wide + local.Wide;
    }
}
