using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableSourceFrameValueCases
{
    internal static void GeneratedMixedReadUsesTheLiveSourceActivationAndExactWideType()
    {
        Fixture fixture = Capture(); uint[] arena = Arena(fixture);
        WarpLogicalMachineLayout layout = Caller(fixture, nameof(WarpPortableHeapServices.ReadSourceFrameValue), 0, 8, Id(fixture, typeof(long)));
        uint[] input = [0x89ABCDEF, 0xFEDCBA98, 0, 0, 0, 0x7FA12345];
        uint[] result = Execute(layout, input, arena);
        Assert.AreEqual(0u, result[0]); CollectionAssert.AreEqual(input, result.Skip(1).ToArray());
        CollectionAssert.AreEqual(input.Take(2).ToArray(), arena.AsSpan((int)arena[WarpPortableHeapLayout.ScratchStart], 2).ToArray());
    }

    internal static void GeneratedMixedWriteChangesOnlyTheCapturedPrivateWideSlot()
    {
        Fixture fixture = Capture(); uint[] arena = Arena(fixture); uint scratch = arena[WarpPortableHeapLayout.ScratchStart];
        arena[scratch] = 0x76543210; arena[scratch + 1] = 0xF0E1D2C3;
        WarpLogicalMachineLayout layout = Caller(fixture, nameof(WarpPortableHeapServices.WriteSourceFrameValue), 0, 8, Id(fixture, typeof(long)));
        uint[] result = Execute(layout, [1, 2, 0, 0, 0, 0x7FA12345], arena);
        CollectionAssert.AreEqual(new uint[] { 0, 0x76543210, 0xF0E1D2C3, 0, 0, 0, 0x7FA12345 }, result);
    }

    internal static void StaleFrameOwnersAndEqualWidthRetypingCannotMutateSourceStorage()
    {
        Fixture fixture = Capture(); uint[] arena = Arena(fixture); uint[] input = [1, 2, 0, 0, 0, 0x7FA12345];
        uint[] stale = Execute(Caller(fixture, nameof(WarpPortableHeapServices.WriteSourceFrameValue), 0, 8, Id(fixture, typeof(long)), stale: true), input, arena);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, stale[0]); CollectionAssert.AreEqual(input, stale.Skip(1).ToArray());
        uint[] retyped = Execute(Caller(fixture, nameof(WarpPortableHeapServices.WriteSourceFrameValue), 0, 8, Id(fixture, typeof(double))), input, arena);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, retyped[0]); CollectionAssert.AreEqual(input, retyped.Skip(1).ToArray());
    }

    internal static void ReferenceOutWriteRetainsItsLeaseUntilTheWholeOwnerIsCopied()
    {
        Fixture fixture = Capture(); uint[] arena = Arena(fixture);
        uint[] owner = Object(fixture, arena); owner.CopyTo(arena, (int)arena[WarpPortableHeapLayout.ScratchStart]);
        Assert.AreEqual(0u, ArenaService(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        WarpLogicalMachineLayout layout = Caller(fixture, nameof(WarpPortableHeapServices.WriteSourceFrameValue), 8, 12, Id(fixture, typeof(object)));
        uint[] result = Execute(layout, [1, 2, 0, 0, 0, 0x7FA12345], arena, retainLease: true);
        CollectionAssert.AreEqual(new uint[] { 0, 1, 2, owner[0], owner[1], owner[2], 0x7FA12345 }, result);
        Assert.AreEqual(0u, ArenaService(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    }

    internal static void InvalidReferenceScratchFailsBeforeAnOutOwnerBeginsChanging()
    {
        Fixture fixture = Capture(); uint[] arena = Arena(fixture); uint[] owner = Object(fixture, arena);
        owner[0]++; owner.CopyTo(arena, (int)arena[WarpPortableHeapLayout.ScratchStart]);
        Assert.AreEqual(0u, ArenaService(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        uint[] input = [1, 2, 0, 0, 0, 0x7FA12345];
        uint[] result = Execute(Caller(fixture, nameof(WarpPortableHeapServices.WriteSourceFrameValue), 8, 12, Id(fixture, typeof(object))), input, arena, retainLease: true);
        Assert.AreEqual(WarpPortableHeapLayout.WrongContext, result[0]); CollectionAssert.AreEqual(input, result.Skip(1).ToArray());
        Assert.AreEqual(0u, ArenaService(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    }

    internal static void RuntimeFrameServicesRemainZeroSourceCostWithExactBankBindings()
    {
        Fixture fixture = Capture(); WarpLogicalMachineLayout layout = Caller(fixture, nameof(WarpPortableHeapServices.ReadSourceFrameValue), 0, 8, Id(fixture, typeof(long)));
        Assert.IsTrue(layout.Kernel.Execution!.RuntimeStateAccess); Assert.IsTrue(layout.Kernel.Execution.FrameOwners);
        Assert.IsFalse(layout.Kernel.Execution.Bodies[1].RuntimeHelper);
        Assert.IsTrue(layout.Kernel.Execution.Bodies.Skip(2).All(body => body.RuntimeHelper && body.SourceBlockCosts.All(cost => cost == 0)));
        foreach (WarpBackendKind backend in new[] { WarpBackendKind.NVPTX, WarpBackendKind.AMDGPU, WarpBackendKind.SPIRV })
        {
            string source = WarpPortableMachineEmitter.Emit(layout, backend);
            Assert.IsTrue(source.Contains("load i32", StringComparison.Ordinal)); Assert.IsFalse(source.Contains("inttoptr", StringComparison.Ordinal));
        }
    }

    private static Fixture Capture()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Fixtures).GetMethod(nameof(Fixtures.Source))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph); WarpPortableWordLoweredProgram lowered = WarpPortableWordLowerer.Lower(graph, typed);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        return new(schema, lowered, WarpPortableSourceFrameSchema.Create(schema, lowered));
    }
    private static uint[] Arena(Fixture fixture) => fixture.Schema.CreateArena(181, 1024, 16, 16, 1, 1024, fixture.Frames);
    private static uint Id(Fixture fixture, Type type) => fixture.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(type));
    private static WarpLogicalMachineLayout Caller(Fixture fixture, string service, uint offset, uint span, uint type, bool stale = false)
    {
        var composition = new Composition(fixture); MethodInfo target = typeof(WarpPortableHeapServices).GetMethod(service, BindingFlags.Public | BindingFlags.Static)!;
        WarpPortableGeneratedServiceImport imported = composition.Importer.Import(target, WarpPortableSourceValueLayout.Semantics + "/" + WarpPortableSourceServiceBanks.Semantics,
            WarpPortableGeneratedServiceKind.StateAndArena, WarpPortableSourceServiceBanks.Capture(target));
        var instructions = new List<WarpIrInstruction>(); int next = 0;
        for (int index = 0; index < 6; index++)
        {
            int word = next++; instructions.Add(new(word, WarpIrOpCode.LoadArgument, immediate: (uint)index));
            instructions.Add(new(next++, WarpManagedFrameOpCode.StorePrivateWord, word, immediate: (uint)index));
        }
        int context = next++; instructions.Add(new(context, WarpManagedFrameOpCode.OwnerContext));
        int frame = next++; instructions.Add(new(frame, WarpManagedFrameOpCode.OwnerFrame));
        int generation = next++; instructions.Add(new(generation, WarpManagedFrameOpCode.OwnerGeneration));
        if (stale)
        {
            int one = next++; instructions.Add(new(one, WarpIrOpCode.Constant, immediate: 1));
            int replacement = next++; instructions.Add(new(replacement, WarpIrOpCode.Add, generation, one)); generation = replacement;
        }
        int address = next++; instructions.Add(new(address, WarpIrOpCode.Constant, immediate: offset));
        int bytes = next++; instructions.Add(new(bytes, WarpIrOpCode.Constant, immediate: span));
        int element = next++; instructions.Add(new(element, WarpIrOpCode.Constant, immediate: type));
        int scratch = next++; instructions.Add(new(scratch, WarpIrOpCode.Constant, immediate: 0));
        int fault = next++; instructions.Add(new(fault, imported.Function, [context, frame, generation, address, bytes, element, scratch], 1));
        int[] result = [fault, .. Enumerable.Range(0, 6).Select(index =>
        {
            int word = next++; instructions.Add(new(word, WarpManagedFrameOpCode.LoadPrivateWord, immediate: (uint)index)); return word;
        })];
        var function = new WarpControlFlowFunction(0, "generated-frame-service-caller/" + fixture.Lowered.LoweredHash, 6,
            [new WarpBasicBlock(0, [], instructions, new WarpTupleReturnTerminator(result))]);
        return composition.Finish(function, imported.Identity);
    }
    private static uint[] Execute(WarpLogicalMachineLayout layout, uint[] words, uint[] arena, bool retainLease = false)
    {
        uint[] state = layout.CreateInitialState(64, 1); CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[][] inputs = words.Select(word => new[] { word }).ToArray();
        for (int quantum = 0; quantum < 100000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; quantum++)
        {
            compiled.ExecuteManagedQuantum(inputs, [], 0, state, 64, layout.MaximumBlockCost, arena);
            if (retainLease) { Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LeaseState]); }
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        return Enumerable.Range(0, 7).Select(index => state[layout.GetResultWordOffset(index, 64)]).ToArray();
    }
    private static uint[] Object(Fixture fixture, uint[] arena)
    {
        Assert.AreEqual(0u, ArenaService(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, ArenaService(nameof(WarpPortableHeapServices.AllocateObject), arena, [Id(fixture, typeof(object))]));
        uint[] reference = arena.AsSpan((int)WarpPortableHeapLayout.Result, 3).ToArray();
        Assert.AreEqual(0u, ArenaService(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, ArenaService(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue])); return reference;
    }
    private static uint ArenaService(string name, uint[] arena, uint[] arguments)
    {
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableHeapServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!);
        uint[] state = layout.CreateInitialState(64, 10000000); CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        for (int quantum = 0; quantum < 100000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; quantum++)
        {
            compiled.ExecuteManagedQuantum(arguments.Select(word => new[] { word }).ToArray(), [], 0, state, 64, layout.MaximumBlockCost, arena);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]); return state[layout.GetResultWordOffset(0, 64)];
    }
    private sealed class Composition
    {
        private readonly Fixture fixture;
        private readonly List<WarpControlFlowFunction?> functions = [null];
        private readonly List<WarpLogicalBodyMetadata?> metadata = [null];
        private readonly Dictionary<string, int> identities = new(StringComparer.Ordinal);
        internal Composition(Fixture fixture) { this.fixture = fixture; Importer = new(fixture.Schema.SchemaHash, Reserve, Store); }
        internal WarpPortableGeneratedServiceImporter Importer { get; }
        private int Reserve(string identity)
        {
            if (identities.TryGetValue(identity, out int existing)) { return existing; }
            int id = functions.Count; functions.Add(null); metadata.Add(null); identities.Add(identity, id); return id;
        }
        private void Store(WarpControlFlowFunction function, WarpLogicalBodyMetadata body)
        {
            if (functions[function.Id] is { } previous) { Assert.IsTrue(WarpPortableWordServiceComparison.Equal(previous, function)); return; }
            functions[function.Id] = function; metadata[function.Id] = body;
        }
        internal WarpLogicalMachineLayout Finish(WarpControlFlowFunction source, string identity)
        {
            functions[0] = source; metadata[0] = new(fixture.Lowered.Bodies[0].PrivateWordCount, false, [1]);
            WarpIrInstruction[] entry = [.. Enumerable.Range(0, 6).Select(index => new WarpIrInstruction(index, WarpIrOpCode.LoadInput, immediate: (uint)index)),
                new WarpIrInstruction(6, 0, Enumerable.Range(0, 6), 7)];
            var execution = new WarpLogicalExecutionMetadata([new(0, true, [0]), .. metadata.Select(body => body!)], frameOwners: true, runtimeStateAccess: true);
            return new(new WarpControlFlowKernel(identity + "/" + fixture.Frames.FrameSchemaHash, 6, 0,
                [new WarpBasicBlock(0, [], entry, new WarpTupleReturnTerminator(Enumerable.Range(6, 7)))], null, functions.Select(function => function!), execution));
        }
    }
    private sealed record Fixture(WarpPortableSourceHeapSchema Schema, WarpPortableWordLoweredProgram Lowered, WarpPortableSourceFrameSchema Frames);
    private static class Fixtures { public static long Source(long value, object? reference, float number) => value + (reference is null ? 0 : 1) + (number == 0 ? 0 : 1); }
}
