using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableSourceInitializerFailureCases
{
    private static Fixture Capture(Type type, string method, uint quota = 4096)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(type.GetMethod(method)!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph); WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableSourceInitializerFailurePlan plan = WarpPortableSourceInitializerFailurePlan.Capture(graph, typed, schema);
        WarpPortableSourceInitializerFailureRow entry = plan.Rows.First(row => row.Origin.Kind == WarpPortableSourceOperationOriginKind.EntryInvocation ||
            string.Equals(row.Origin.MethodIdentity, graph.EntryIdentity, StringComparison.Ordinal));
        uint[] arena = plan.AttachToEmptyHeap(schema.CreateArena(301, 4096, 32, 32, 1, quota), 1);
        uint record = arena[arena[WarpPortableSourceInitializerFailureLayout.Descriptor] + WarpPortableSourceInitializerFailureLayout.TypeStart] +
            (entry.TypeRecord - 1) * WarpPortableSourceInitializerFailureLayout.TypeWords;
        return new(graph, typed, schema, plan, arena, entry, record);
    }

    private static Fixture Ready()
    {
        Fixture fixture = Capture(typeof(CapturedFailure), nameof(CapturedFailure.ProtectedRead)); Acquire(fixture);
        Assert.AreEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.PrepareSourceInitializerFailure), [fixture.Entry.TypeRecord]));
        Assert.AreEqual(WarpPortableSourceInitializerFailureLayout.Ready, fixture.Arena[fixture.Record + WarpPortableSourceInitializerFailureLayout.State]);
        Assert.AreEqual(3u, fixture.Arena[WarpPortableHeapLayout.LiveObjects]); return fixture;
    }

    private static uint[] Inner(Fixture fixture, Type type)
    {
        Assert.AreEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.AllocateObject), [fixture.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(type))]));
        uint[] owner = Reference(fixture.Arena, WarpPortableHeapLayout.Result); Acknowledge(fixture);
        uint scratch = fixture.Arena[WarpPortableHeapLayout.ScratchStart];
        for (uint word = 0; word < WarpPortableSourceExceptionLayout.InputWords; word++) { fixture.Arena[scratch + word] = 0; }
        Assert.AreEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.InitializeSourceExceptionData), [.. owner, 0]));
        return owner;
    }

    private static uint[] Trace(Fixture fixture)
    {
        Assert.AreEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.AllocateArray),
            [fixture.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(uint[]))), 4]));
        uint[] reference = Reference(fixture.Arena, WarpPortableHeapLayout.Result); Acknowledge(fixture); return reference;
    }

    private static uint Cache(Fixture fixture, uint[] inner, bool wrongContext = false)
    {
        WarpLogicalMachineLayout layout = MixedLayout(fixture); uint[] state = layout.CreateInitialState(64, 1);
        uint type = TypeRecord(fixture);
        if (fixture.Arena[type + WarpPortableHeapLayout.InitializerState] != 3)
        {
            fixture.Arena[type + WarpPortableHeapLayout.InitializerState] = 1;
            fixture.Arena[type + WarpPortableHeapLayout.InitializerOwner] = 0;
            fixture.Arena[type + WarpPortableHeapServices.SourceInitializerContext] = state[WarpLogicalMachineLayout.OwnerContextOffset] + (wrongContext ? 1u : 0u);
        }
        return Run(layout, fixture.Arena, [fixture.Entry.Id, 0, .. inner], state);
    }

    private static WarpLogicalMachineLayout MixedLayout(Fixture fixture)
    {
        const string name = nameof(WarpPortableHeapServices.CacheSourceInitializerFailure);
        if (fixture.Layouts.TryGetValue(name, out WarpLogicalMachineLayout? existing)) { return existing; }
        MethodInfo method = typeof(WarpPortableHeapServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
        WarpLogicalMachineLayout compiled = WarpWordStateArenaServiceLowerer.Lower(method, WarpPortableSourceServiceBanks.Capture(method));
        WarpLogicalMachineLayout owned = Owned(compiled.Kernel, state: true); fixture.Layouts.Add(name, owned); return owned;
    }

    private static uint Service(Fixture fixture, string name, uint[] arguments)
    {
        if (!fixture.Layouts.TryGetValue(name, out WarpLogicalMachineLayout? layout))
        {
            WarpLogicalMachineLayout compiled = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableHeapServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!);
            layout = Owned(compiled.Kernel, state: false); fixture.Layouts.Add(name, layout);
        }
        return Run(layout, fixture.Arena, arguments, layout.CreateInitialState(64, 1));
    }

    private static WarpLogicalMachineLayout Owned(WarpControlFlowKernel kernel, bool state)
    {
        WarpLogicalBodyMetadata[] bodies = [new(0, true, Enumerable.Repeat(0, kernel.Blocks.Count)),
            .. kernel.Functions.Select(function => new WarpLogicalBodyMetadata(0, true, Enumerable.Repeat(0, function.Blocks.Count)))];
        var metadata = new WarpLogicalExecutionMetadata(bodies, recursiveCalls: false, frameOwners: true, runtimeStateAccess: state);
        return new(new WarpControlFlowKernel(kernel.Name, kernel.InputBufferCount, kernel.ScalarArgumentCount, kernel.Blocks, kernel.Reduction, kernel.Functions, metadata));
    }

    private static uint Run(WarpLogicalMachineLayout layout, uint[] arena, uint[] arguments, uint[] state)
    {
        uint[][] inputs = arguments.Length == 0 ? [[0]] : arguments.Select(value => new[] { value }).ToArray();
        uint expectedLease = arena[WarpPortableHeapLayout.LeaseState];
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout); int attempts = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && attempts++ < 100000)
        {
            compiled.ExecuteManagedQuantum(inputs, [], 0, state, 64, layout.MaximumBlockCost, arena);
            // Acquire/Release are exact consistency helpers, not private grants.
            if (layout.Kernel.Name.Contains("AcquireServiceLease", StringComparison.Ordinal)) { expectedLease = 1; }
            if (layout.Kernel.Name.Contains("ReleaseServiceLease", StringComparison.Ordinal)) { expectedLease = 0; }
            if (!layout.Kernel.Name.Contains("AcquireServiceLease", StringComparison.Ordinal) &&
                !layout.Kernel.Name.Contains("ReleaseServiceLease", StringComparison.Ordinal))
            {
                Assert.AreEqual(expectedLease, arena[WarpPortableHeapLayout.LeaseState]);
            }
        }
        Assert.IsLessThan(100000, attempts); Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(1u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        Assert.AreEqual(expectedLease, arena[WarpPortableHeapLayout.LeaseState]);
        return state[layout.GetResultWordOffset(0, 64)];
    }

    private static uint TypeRecord(Fixture fixture) => fixture.Arena[WarpPortableHeapLayout.TypeStart] +
        (fixture.Arena[fixture.Record + WarpPortableSourceInitializerFailureLayout.TypeId] - 1) * WarpPortableHeapLayout.TypeWords;
    private static uint Payload(uint[] arena, uint[] owner) => arena[arena[WarpPortableHeapLayout.SlotStart] + (owner[1] - 1) * WarpPortableHeapLayout.SlotWords + WarpPortableHeapLayout.SlotPayload];
    private static uint[] Reference(uint[] arena, uint start) => arena.AsSpan((int)start, 3).ToArray();
    private static ushort[] Text(uint[] arena, uint[] owner)
    {
        uint slot = arena[WarpPortableHeapLayout.SlotStart] + (owner[1] - 1) * WarpPortableHeapLayout.SlotWords;
        uint payload = arena[slot + WarpPortableHeapLayout.SlotPayload], length = arena[slot + WarpPortableHeapLayout.SlotLength];
        return Enumerable.Range(0, checked((int)length)).Select(index => (ushort)(arena[payload + (uint)index / 2] >> ((index & 1) * 16))).ToArray();
    }
    private static void Acquire(Fixture fixture) => Assert.AreEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.AcquireServiceLease), [uint.MaxValue]));
    private static void Release(Fixture fixture) => Assert.AreEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.ReleaseServiceLease), [uint.MaxValue]));
    private static void Acknowledge(Fixture fixture) => Assert.AreEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.AcknowledgeServiceResult), [uint.MaxValue]));
    private sealed record Fixture(WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed, WarpPortableSourceHeapSchema Schema,
        WarpPortableSourceInitializerFailurePlan Plan, uint[] Arena, WarpPortableSourceInitializerFailureRow Entry, uint Record)
    {
        internal Dictionary<string, WarpLogicalMachineLayout> Layouts { get; } = new(StringComparer.Ordinal);
    }
}
