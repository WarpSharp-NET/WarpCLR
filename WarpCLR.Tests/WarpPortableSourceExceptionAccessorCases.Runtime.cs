using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableSourceExceptionAccessorCases
{
    private sealed record Fixture(WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed,
        WarpPortableSourceHeapSchema Schema, WarpPortableWordLoweredProgram Program, WarpLogicalMachineLayout Layout);
    private sealed record Seed(uint[] Arena, uint[] Owner, uint[] Inner, uint[] Param, uint[] Actual, uint[] TypeName);

    private static Fixture Capture(string name) => Capture(WarpPortableMethodGraph.Discover(typeof(Sources).GetMethod(name)!));
    private static Fixture Capture(WarpPortableMethodGraph graph)
    {
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableClosedExceptionAccessorPlan plan = WarpPortableClosedExceptionAccessorPlan.Capture(graph, typed, schema);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed, schema, new WarpPortableClosedExceptionAccessorBinding(plan));
        return new(graph, typed, schema, program, new(program.Kernel));
    }
    private static Seed Create(Fixture fixture, Type type, bool empty = false)
    {
        uint[] arena = fixture.Schema.CreateArena(812, 8192, 64, 64, 1, 8192);
        uint[] inner = Allocate(fixture.Schema, arena, typeof(Exception));
        Acquire(arena); Scratch(arena, new uint[15]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [.. inner, 0])); Release(arena);
        uint[] param = Text(fixture.Schema, arena, "count\0\uD800\uD801\uDC00\uDC01\uFFFD");
        uint[] actual = Allocate(fixture.Schema, arena, typeof(object));
        uint[] typeName = Text(fixture.Schema, arena, "Captured.Source");
        uint[] owner = Allocate(fixture.Schema, arena, type);
        Acquire(arena);
        Scratch(arena, empty ? new uint[15] : [0, 0, 0, .. inner,
            .. (typeof(ArgumentException).IsAssignableFrom(type) ? param : new uint[3]),
            .. (type == typeof(ArgumentOutOfRangeException) ? actual : new uint[3]),
            .. (type == typeof(TypeInitializationException) ? typeName : new uint[3])]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [.. owner, 0]));
        return new(arena, owner, inner, param, actual, typeName);
    }

    private static uint[] Execute(Fixture fixture, uint[] arena, uint[] arguments, int quantum)
    {
        uint[] state = ExecuteState(fixture, arena, arguments, quantum);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return Enumerable.Range(0, fixture.Layout.ResultWordCount).Select(word => state[fixture.Layout.GetResultWordOffset(word, 64)]).ToArray();
    }
    private static uint[] ExecuteState(Fixture fixture, uint[] arena, uint[] arguments, int quantum, uint resultSeed = 0)
    {
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(fixture.Layout);
        uint[] state = fixture.Layout.CreateInitialState(64, 1000000);
        for (int word = 0; word < fixture.Layout.ResultWordCount; word++) { state[fixture.Layout.GetResultWordOffset(word, 64)] = resultSeed; }
        uint[][] inputs = arguments.Select(word => new[] { word }).ToArray();
        int attempt = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && attempt++ < 1000000)
        {
            compiled.ExecuteManagedQuantum(inputs, [], 0, state, 64, Math.Max(quantum, fixture.Layout.MaximumBlockCost), arena);
        }
        Assert.IsLessThan(1000000, attempt); return state;
    }
    private static uint[] Allocate(WarpPortableSourceHeapSchema schema, uint[] arena, Type type)
    {
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AllocateObject), arena, [schema.TypeId(WarpPortableMethodGraphIdentity.Type(type))]));
        return arena.Skip((int)WarpPortableHeapLayout.Result).Take(3).ToArray();
    }
    private static uint[] Text(WarpPortableSourceHeapSchema schema, uint[] arena, string value)
    {
        Scratch(arena, value.Select(character => (uint)character).ToArray());
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.CreateString), arena,
            [schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(string))), 0, (uint)value.Length]));
        return arena.Skip((int)WarpPortableHeapLayout.Result).Take(3).ToArray();
    }
    private static void Scratch(uint[] arena, uint[] words) => words.CopyTo(arena, (int)arena[WarpPortableHeapLayout.ScratchStart]);
    private static void Acquire(uint[] arena) => Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
    private static void Release(uint[] arena) => Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    private static uint Service(string name, uint[] arena, uint[] arguments)
    {
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableHeapServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout); uint[] state = layout.CreateInitialState(64, 10000000);
        uint[][] inputs = arguments.Length == 0 ? [[0]] : arguments.Select(word => new[] { word }).ToArray(); int attempt = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && attempt++ < 100000)
        {
            compiled.ExecuteManagedQuantum(inputs, [], 0, state, 64, layout.MaximumBlockCost, arena);
        }
        Assert.IsLessThan(100000, attempt); Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return state[layout.GetResultWordOffset(0, 64)];
    }
}
