using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableSourceArrayLengthCases
{
    internal static void GeneratedArrayLengthsMaterializeAnExactNativeUnsignedPair()
    {
        foreach (Fixture fixture in Fixtures())
        {
            Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReadSourceArrayLength), fixture.Arena, fixture.Owner));
            Assert.AreEqual(fixture.ExpectedLength, fixture.Arena[WarpPortableHeapLayout.Result], fixture.Name);
            Assert.AreEqual(0u, fixture.Arena[WarpPortableHeapLayout.Result + 1], fixture.Name);
        }
    }

    internal static void GeneratedArrayLengthRejectsNullStaleAndUnboundShapesBeforePublishing()
    {
        Fixture fixture = Fixtures().First(); uint[] arena = fixture.Arena; uint[] owner = fixture.Owner;
        foreach ((uint[] reference, uint fault) in new (uint[], uint)[]
        {
            ([0, 0, 0], WarpPortableHeapLayout.NullReference),
            ([owner[0] + 1, owner[1], owner[2]], WarpPortableHeapLayout.WrongContext),
            ([owner[0], owner[1], owner[2] + 1], WarpPortableHeapLayout.InvalidReference),
        })
        {
            arena[WarpPortableHeapLayout.Result] = uint.MaxValue; arena[WarpPortableHeapLayout.Result + 1] = uint.MaxValue;
            Assert.AreEqual(fault, Service(nameof(WarpPortableHeapServices.ReadSourceArrayLength), arena, reference));
            Assert.AreEqual(0u, arena[WarpPortableHeapLayout.Result]); Assert.AreEqual(0u, arena[WarpPortableHeapLayout.Result + 1]);
        }
        uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
        uint row = arena[descriptor + WarpPortableSourceArrayLayout.ShapeStart] + (owner[1] - 1) * WarpPortableSourceArrayLayout.ShapeWords;
        arena[row + WarpPortableSourceArrayLayout.Generation]++;
        Assert.AreEqual(WarpPortableHeapLayout.InvalidType, Service(nameof(WarpPortableHeapServices.ReadSourceArrayLength), arena, owner));
        arena[row + WarpPortableSourceArrayLayout.Generation]--;
        arena[descriptor + WarpPortableSourceArrayLayout.ShapeStart] = uint.MaxValue;
        Assert.AreEqual(WarpPortableHeapLayout.InvalidType, Service(nameof(WarpPortableHeapServices.ReadSourceArrayLength), arena, owner));
    }

    private static Fixture[] Fixtures()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Source).GetMethod(nameof(Source.Read))!,
            concreteTypes: [typeof(int[,]), typeof(int).MakeArrayType(1), typeof(byte[,])]);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, WarpPortableTypedProgram.Verify(graph));
        return [Create(schema, "vector", typeof(int[]), [5], [0]),
            Create(schema, "matrix-signed-lower", typeof(int[,]), [2, 3], [-2, 4]),
            Create(schema, "empty-matrix", typeof(int[,]), [0, 17], [0, -2]),
            Create(schema, "bounded-rank-one", typeof(int).MakeArrayType(1), [5], [-8]),
            Create(schema, "rank-one-vector-morph", typeof(int).MakeArrayType(1), [7], [0]),
            Create(schema, "byte-matrix", typeof(byte[,]), [2, 3], [0, 0])];
    }

    private static Fixture Create(WarpPortableSourceHeapSchema schema, string name, Type type, int[] lengths, int[] lower)
    {
        // CLR arrays are an oracle only. The generated service reads the independent arena.
        Array reference = Array.CreateInstance(type.GetElementType()!, lengths, lower);
        uint[] arena = schema.CreateArena(187, 4096, 32, 32, 2, 4096);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        uint scratch = arena[WarpPortableHeapLayout.ScratchStart];
        for (int index = 0; index < lengths.Length; index++)
        {
            arena[scratch + (uint)index * 2] = unchecked((uint)lower[index]);
            arena[scratch + (uint)index * 2 + 1] = (uint)lengths[index];
        }
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AllocateSourceArray), arena,
            [schema.TypeId(WarpPortableMethodGraphIdentity.Type(type)), (uint)lengths.Length, 0]));
        uint[] owner = arena.Skip((int)WarpPortableHeapLayout.Result).Take(3).ToArray();
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
        Assert.AreEqual((long)reference.Length, reference.LongLength);
        return new(name, schema, arena, owner, (uint)reference.LongLength);
    }

    private static uint Service(string name, uint[] arena, uint[] words)
    {
        MethodInfo method = typeof(WarpPortableHeapServices).GetMethod(name)!;
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(method); CoreCLRResumableKernel kernel = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(64, 10000000); uint[][] inputs = words.Select(word => new[] { word }).ToArray(); int steps = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && steps++ < 100000)
        {
            kernel.ExecuteManagedQuantum(inputs, [], 0, state, 64, layout.MaximumBlockCost, arena);
        }
        Assert.IsLessThan(100000, steps); Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return state[layout.GetResultWordOffset(0, 64)];
    }

    private sealed record Fixture(string Name, WarpPortableSourceHeapSchema Schema, uint[] Arena, uint[] Owner, uint ExpectedLength);
    private static class Source { public static int Read(int[] values) => values[0]; }
}
