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
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture through reflection.")]
internal sealed class WarpPortableSourceArrayTests
{
    [TestMethod]
    public void CapturedArraysBindExactRankVectorIdentityAndPackedStride()
    {
        WarpPortableSourceHeapSchema schema = Schema();
        WarpPortableSourceHeapType matrix = schema.Types.First(type => type.Id == Id(schema, typeof(int[,])));
        WarpPortableSourceHeapType vector = schema.Types.First(type => type.Id == Id(schema, typeof(int[])));
        WarpPortableSourceHeapType bounded = schema.Types.First(type => type.Id == Id(schema, typeof(int).MakeArrayType(1)));
        WarpPortableSourceHeapType packed = schema.Types.First(type => type.Id == Id(schema, typeof(Packed[,])));
        Assert.AreEqual(2u, matrix.ArrayRank); Assert.IsFalse(matrix.VectorArray);
        Assert.AreEqual(1u, vector.ArrayRank); Assert.IsTrue(vector.VectorArray);
        Assert.AreEqual(1u, bounded.ArrayRank); Assert.IsFalse(bounded.VectorArray);
        Assert.AreEqual(9, packed.ElementByteSize); Assert.AreEqual(3u, packed.ElementStrideWords);
        Assert.AreNotEqual(vector.Id, bounded.Id);
    }

    [TestMethod]
    public void GeneratedSignedBoundsAndRowMajorOffsetsMatchActualClrArrays()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        Array expected = Array.CreateInstance(typeof(int), [2, 3], [-2, 4]);
        uint[] owner = Allocate(schema, arena, typeof(int[,]), [unchecked((uint)-2), 2, 4, 3]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.SourceArrayDimension), arena, [.. owner, 0, 3]));
        Assert.AreEqual((uint)expected.Rank, arena[WarpPortableHeapLayout.Result]);
        for (uint dimension = 0; dimension < 2; dimension++)
        {
            for (uint operation = 0; operation < 3; operation++)
            {
                Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.SourceArrayDimension), arena, [.. owner, dimension, operation]));
                int value = operation == 0 ? expected.GetLength((int)dimension) : operation == 1 ? expected.GetLowerBound((int)dimension) : expected.GetUpperBound((int)dimension);
                Assert.AreEqual(unchecked((uint)value), arena[WarpPortableHeapLayout.Result]);
            }
        }
        for (int row = -2; row <= -1; row++)
        {
            for (int column = 4; column <= 6; column++)
            {
                Scratch(arena, [unchecked((uint)row), (uint)column]);
                Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.SourceArrayElementOffset), arena, [.. owner, 2, 0]));
                uint expectedOffset = (uint)((row + 2) * 3 + column - 4) * 4;
                Assert.AreEqual(expectedOffset, arena[WarpPortableHeapLayout.Result]);
            }
        }
    }

    [TestMethod]
    public void GeneratedPackedMatrixAddressPreservesEveryWideBitAndNeighbour()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        uint[] owner = Allocate(schema, arena, typeof(Packed[,]), [0, 2, 0, 3]);
        Scratch(arena, [1, 2]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.SourceArrayElementOffset), arena, [.. owner, 2, 0]));
        uint offset = arena[WarpPortableHeapLayout.Result]; Assert.AreEqual(60u, offset);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.WriteSourceByte), arena, [.. owner, offset, 1, Id(schema, typeof(byte)), 0, 0xAB]));
        ulong bits = 0xFEDCBA9876543210;
        for (uint index = 0; index < 8; index++)
        {
            Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.WriteSourceByte), arena, [.. owner, offset + 1, 8, Id(schema, typeof(long)), index, (uint)(bits >> (int)(index * 8))]));
            Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReadSourceByte), arena, [.. owner, offset + 1, 8, Id(schema, typeof(long)), index, 0]));
            Assert.AreEqual((uint)(bits >> (int)(index * 8)) & 255u, arena[WarpPortableHeapLayout.Result]);
        }
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReadSourceByte), arena, [.. owner, offset, 1, Id(schema, typeof(byte)), 0, 0]));
        Assert.AreEqual(0xABu, arena[WarpPortableHeapLayout.Result]);
    }

    [TestMethod]
    public void GeneratedZeroDimensionsRemainEmptyWithoutIntermediateProductOverflow()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        uint[] owner = Allocate(schema, arena, typeof(int[,]), [0, 0, 0, 0x10000]);
        uint slot = arena[WarpPortableHeapLayout.SlotStart] + (owner[1] - 1) * WarpPortableHeapLayout.SlotWords;
        Assert.AreEqual(0u, arena[slot + WarpPortableHeapLayout.SlotLength]);
        Scratch(arena, [0, 0]);
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, Service(nameof(WarpPortableHeapServices.SourceArrayElementOffset), arena, [.. owner, 2, 0]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.SourceArrayDimension), arena, [.. owner, 0, 2]));
        Assert.AreEqual(uint.MaxValue, arena[WarpPortableHeapLayout.Result]);
    }

    [TestMethod]
    public void GeneratedAllocationChecksAllDimensionsBeforeMutatingHeap()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        uint before = arena[WarpPortableHeapLayout.LiveObjects];
        Scratch(arena, [0, 0, 0, uint.MaxValue]);
        Assert.AreEqual(WarpPortableSourceArrayLayout.ArithmeticOverflow, Service(nameof(WarpPortableHeapServices.AllocateSourceArray), arena, [Id(schema, typeof(int[,])), 2, 0]));
        Assert.AreEqual(before, arena[WarpPortableHeapLayout.LiveObjects]);
        Scratch(arena, [0, 0x10000, 0, 0x10000]);
        Assert.AreEqual(WarpPortableSourceArrayLayout.ManagedDimensionsExceeded, Service(nameof(WarpPortableHeapServices.AllocateSourceArray), arena, [Id(schema, typeof(int[,])), 2, 0]));
        Assert.AreEqual(before, arena[WarpPortableHeapLayout.LiveObjects]);
        Scratch(arena, [0x7FFFFFFF, 2, 0, 1]);
        Assert.AreEqual(WarpPortableSourceArrayLayout.ArgumentOutOfRange, Service(nameof(WarpPortableHeapServices.AllocateSourceArray), arena, [Id(schema, typeof(int[,])), 2, 0]));
        Assert.AreEqual(before, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    }

    [TestMethod]
    public void GeneratedDimensionAndElementChecksPreserveNullBoundsAndRankFaults()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        uint[] owner = Allocate(schema, arena, typeof(int[,]), [unchecked((uint)-3), 2, 7, 4]);
        Assert.AreEqual(WarpPortableHeapLayout.NullReference, Service(nameof(WarpPortableHeapServices.SourceArrayDimension), arena, [0, 0, 0, uint.MaxValue, 0]));
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, Service(nameof(WarpPortableHeapServices.SourceArrayDimension), arena, [.. owner, 2, 0]));
        Assert.AreEqual(WarpPortableHeapLayout.WrongContext, Service(nameof(WarpPortableHeapServices.SourceArrayDimension), arena, [owner[0] + 1, owner[1], owner[2], 0, 0]));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidType, Service(nameof(WarpPortableHeapServices.SourceArrayElementOffset), arena, [.. owner, 1, 0]));
        Scratch(arena, [unchecked((uint)-4), 7]);
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, Service(nameof(WarpPortableHeapServices.SourceArrayElementOffset), arena, [.. owner, 2, 0]));
        Scratch(arena, [unchecked((uint)-2), 11]);
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, Service(nameof(WarpPortableHeapServices.SourceArrayElementOffset), arena, [.. owner, 2, 0]));
    }

    [TestMethod]
    public void GeneratedShapeMetadataIsBoundToAllocatedGenerationAndCapturedType()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        uint[] owner = Allocate(schema, arena, typeof(int[,]), [0, 2, 0, 2]);
        uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
        uint shape = arena[descriptor + WarpPortableSourceArrayLayout.ShapeStart] + (owner[1] - 1) * WarpPortableSourceArrayLayout.ShapeWords;
        arena[shape + WarpPortableSourceArrayLayout.Generation]++;
        Assert.AreEqual(WarpPortableHeapLayout.InvalidType, Service(nameof(WarpPortableHeapServices.SourceArrayDimension), arena, [.. owner, 0, 0]));
        arena[shape + WarpPortableSourceArrayLayout.Generation]--;
        arena[shape + WarpPortableSourceArrayLayout.Type] = Id(schema, typeof(object[,]));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidType, Service(nameof(WarpPortableHeapServices.SourceArrayDimension), arena, [.. owner, 0, 0]));
        arena[descriptor + WarpPortableSourceArrayLayout.ShapeStart] = uint.MaxValue;
        Assert.AreEqual(WarpPortableHeapLayout.InvalidType, Service(nameof(WarpPortableHeapServices.SourceArrayDimension), arena, [.. owner, 0, 0]));
    }

    [TestMethod]
    public void GeneratedBoundedRankOneArrayRetainsNegativeLowerBound()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        uint[] owner = Allocate(schema, arena, typeof(int).MakeArrayType(1), [unchecked((uint)-8), 5]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.SourceArrayDimension), arena, [.. owner, 0, 1]));
        Assert.AreEqual(unchecked((uint)-8), arena[WarpPortableHeapLayout.Result]);
        Scratch(arena, [unchecked((uint)-4)]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.SourceArrayElementOffset), arena, [.. owner, 1, 0]));
        Assert.AreEqual(16u, arena[WarpPortableHeapLayout.Result]);
    }

    [TestMethod]
    public void GeneratedBoundedRankOneZeroLowerBoundMorphsToActualVectorType()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        Array expected = Array.CreateInstance(typeof(int), [5], [0]);
        Assert.AreEqual(typeof(int[]), expected.GetType());
        uint[] owner = Allocate(schema, arena, typeof(int).MakeArrayType(1), [0, 5]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.GetType), arena, owner));
        Assert.AreEqual(Id(schema, expected.GetType()), arena[WarpPortableHeapLayout.Result]);
    }

    [TestMethod]
    public void GeneratedManagedDimensionFailureKeepsClrValidationOrder()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Array.CreateInstance(typeof(int), [2, 1], [int.MaxValue, 0]));
        Scratch(arena, [int.MaxValue, 2, 0, 1]);
        Assert.AreEqual(WarpPortableSourceArrayLayout.ArgumentOutOfRange, Service(nameof(WarpPortableHeapServices.AllocateSourceArray), arena, [Id(schema, typeof(int[,])), 2, 0]));
        Assert.ThrowsExactly<OutOfMemoryException>(() => Array.CreateInstance(typeof(int), [0x10000, 0x10000]));
        Scratch(arena, [0, 0x10000, 0, 0x10000]);
        Assert.AreEqual(WarpPortableSourceArrayLayout.ManagedDimensionsExceeded, Service(nameof(WarpPortableHeapServices.AllocateSourceArray), arena, [Id(schema, typeof(int[,])), 2, 0]));
        Assert.ThrowsExactly<OutOfMemoryException>(() => Array.CreateInstance(typeof(int), [0, int.MaxValue]));
        Scratch(arena, [0, 0, 0, int.MaxValue]);
        Assert.AreEqual(WarpPortableSourceArrayLayout.ManagedDimensionsExceeded, Service(nameof(WarpPortableHeapServices.AllocateSourceArray), arena, [Id(schema, typeof(int[,])), 2, 0]));
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    }

    [TestMethod]
    public void GeneratedConstructorNegativeAndZeroAfterOverflowMatchActualClrCtor()
    {
        var pair = (Func<int, int, Array>)ArrayConstructor(2).CreateDelegate(typeof(Func<int, int, Array>));
        var cube = (Func<int, int, int, Array>)ArrayConstructor(3).CreateDelegate(typeof(Func<int, int, int, Array>));
        Assert.ThrowsExactly<OverflowException>(() => pair(-1, 1));
        Assert.ThrowsExactly<OutOfMemoryException>(() => cube(65536, 65536, 0));
        Assert.IsEmpty(cube(0, 65536, 65536));
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        Scratch(arena, [0, uint.MaxValue, 0, 1]);
        Assert.AreEqual(WarpPortableSourceArrayLayout.ArithmeticOverflow, Service(nameof(WarpPortableHeapServices.AllocateSourceArray), arena, [Id(schema, typeof(int[,])), 2, 0]));
        Scratch(arena, [0, 65536, 0, 65536, 0, 0]);
        Assert.AreEqual(WarpPortableSourceArrayLayout.ManagedDimensionsExceeded, Service(nameof(WarpPortableHeapServices.AllocateSourceArray), arena, [Id(schema, typeof(int[,,])), 3, 0]));
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
        uint[] owner = Allocate(schema, arena, typeof(int[,,]), [0, 0, 0, 65536, 0, 65536]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.SourceArrayDimension), arena, [.. owner, 0, 3]));
        Assert.AreEqual(3u, arena[WarpPortableHeapLayout.Result]);
    }

    private static DynamicMethod ArrayConstructor(int rank)
    {
        Type[] parameters = Enumerable.Repeat(typeof(int), rank).ToArray();
        var method = new DynamicMethod("ActualClrArrayConstructor", typeof(Array), parameters);
        ILGenerator body = method.GetILGenerator();
        for (int index = 0; index < rank; index++) { body.Emit(OpCodes.Ldarg, index); }
        body.Emit(OpCodes.Newobj, typeof(int).MakeArrayType(rank).GetConstructor(parameters)!); body.Emit(OpCodes.Ret);
        return method;
    }

    private static WarpPortableSourceHeapSchema Schema()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Fixtures).GetMethod(nameof(Fixtures.Schema))!,
            concreteTypes: [typeof(int).MakeArrayType(1), typeof(int[,]), typeof(int[,,]), typeof(Packed[,]), typeof(object[,])]);
        return WarpPortableSourceHeapSchema.Create(graph, WarpPortableTypedProgram.Verify(graph));
    }
    private static uint[] Arena(WarpPortableSourceHeapSchema schema) => schema.CreateArena(133, 4096, 32, 32, 2, 4096);
    private static uint Id(WarpPortableSourceHeapSchema schema, Type type) => schema.TypeId(WarpPortableMethodGraphIdentity.Type(type));
    private static void Scratch(uint[] arena, uint[] words) => words.CopyTo(arena, (int)arena[WarpPortableHeapLayout.ScratchStart]);
    private static uint[] Allocate(WarpPortableSourceHeapSchema schema, uint[] arena, Type type, uint[] dimensions)
    {
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue])); Scratch(arena, dimensions);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AllocateSourceArray), arena, [Id(schema, type), (uint)dimensions.Length / 2, 0]));
        uint[] result = arena.Skip((int)WarpPortableHeapLayout.Result).Take(3).ToArray();
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
        return result;
    }
    private static uint Service(string name, uint[] arena, uint[] arguments)
    {
        MethodInfo method = typeof(WarpPortableHeapServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(method);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(64, 10000000);
        uint[][] inputs = arguments.Select(value => new[] { value }).ToArray();
        for (int quantum = 0; quantum < 100000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; quantum++)
        {
            compiled.ExecuteManagedQuantum(inputs, [], 0, state, 64, layout.MaximumBlockCost, arena);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return state[layout.GetResultWordOffset(0, 64)];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Packed { public byte Tag; public long Wide; public Packed(byte tag, long wide) { Tag = tag; Wide = wide; } }
    private static class Fixtures
    {
        public static int Schema(int[] vector) => vector[0];
    }
}
