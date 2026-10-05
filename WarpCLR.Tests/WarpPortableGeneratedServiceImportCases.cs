using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableGeneratedServiceImportCases
{
    internal static void ImportedArithmeticAndArenaServicesExecuteInsideTheCallerWordGraph()
    {
        var composition = new Composition(new string('A', 64));
        WarpPortableGeneratedServiceImport add = composition.Import(typeof(WarpPortableInteger64), nameof(WarpPortableInteger64.AddLow),
            WarpPortableInteger64.Semantics, WarpPortableGeneratedServiceKind.Words);
        WarpLogicalMachineLayout layout = composition.Caller(add);
        uint[] result = Execute(layout, [uint.MaxValue, 1, 2, 3], []);
        Assert.AreEqual(1u, result[0]); Assert.AreEqual(1u, result[1]);
        Assert.IsTrue(composition.Functions.All(function => function!.Blocks.SelectMany(block => block.Instructions).All(instruction => instruction.OpCode != WarpIrOpCode.LoadInput)));
        Assert.IsTrue(layout.Kernel.Execution!.Bodies.Skip(1).All(body => body.RuntimeHelper && body.SourceBlockCosts.All(cost => cost == 0)));
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = schema.CreateArena(111, 128, 4, 4, 1, 128);
        var heap = new Composition(schema.SchemaHash);
        WarpPortableGeneratedServiceImport allocate = heap.Import(typeof(WarpPortableHeapServices), nameof(WarpPortableHeapServices.AllocateObject),
            WarpPortableHeapLayout.Semantics, WarpPortableGeneratedServiceKind.Arena);
        Assert.AreEqual(0u, Execute(heap.Caller(allocate), [schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(object)))], arena)[0]);
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
    }

    internal static void ExactSchemaSemanticVersionAndBankKindChangeImportedIdentity()
    {
        var first = new Composition(new string('A', 64)); var second = new Composition(new string('B', 64));
        WarpPortableGeneratedServiceImport arena = first.Import(typeof(WarpPortableHeapServices), nameof(WarpPortableHeapServices.ValidateSourceByteOwner),
            WarpPortableSourceMemoryLayout.Semantics, WarpPortableGeneratedServiceKind.Arena);
        WarpPortableGeneratedServiceImport state = first.Import(typeof(WarpPortableFrameServices), nameof(WarpPortableFrameServices.ValidateOwner),
            WarpPortableFrameServices.Semantics, WarpPortableGeneratedServiceKind.State);
        WarpPortableGeneratedServiceImport schema = second.Import(typeof(WarpPortableHeapServices), nameof(WarpPortableHeapServices.ValidateSourceByteOwner),
            WarpPortableSourceMemoryLayout.Semantics, WarpPortableGeneratedServiceKind.Arena);
        WarpPortableGeneratedServiceImport version = first.Import(typeof(WarpPortableHeapServices), nameof(WarpPortableHeapServices.ValidateSourceByteOwner),
            WarpPortableSourceMemoryLayout.Semantics + "/variant", WarpPortableGeneratedServiceKind.Arena);
        Assert.AreNotEqual(arena.Identity, state.Identity, StringComparer.Ordinal); Assert.AreNotEqual(arena.SemanticIrHash, state.SemanticIrHash, StringComparer.Ordinal);
        Assert.AreNotEqual(arena.Identity, schema.Identity, StringComparer.Ordinal); Assert.AreNotEqual(arena.Identity, version.Identity, StringComparer.Ordinal);
        Assert.IsTrue(state.RequiresStateAccess); Assert.IsFalse(arena.RequiresStateAccess);
        Assert.ThrowsExactly<ArgumentException>(() => first.Import(typeof(WarpPortableFrameServices), nameof(WarpPortableFrameServices.ValidateOwner),
            WarpPortableFrameServices.Semantics, WarpPortableGeneratedServiceKind.Arena));
    }

    internal static void RepeatedImportReusesExactHelpersAndRetainsImmutableBindingRows()
    {
        var composition = new Composition(new string('C', 64));
        WarpPortableGeneratedServiceImport first = composition.Import(typeof(WarpPortableHeapServices), nameof(WarpPortableHeapServices.ReadSourceValue),
            WarpPortableSourceValueLayout.Semantics, WarpPortableGeneratedServiceKind.Arena);
        int functions = composition.Functions.Count;
        WarpPortableGeneratedServiceImport repeated = composition.Import(typeof(WarpPortableHeapServices), nameof(WarpPortableHeapServices.ReadSourceValue),
            WarpPortableSourceValueLayout.Semantics, WarpPortableGeneratedServiceKind.Arena);
        Assert.AreEqual(first, repeated); Assert.HasCount(functions, composition.Functions);
        Assert.HasCount(1, composition.Importer.Imports);
        Assert.AreEqual(8, first.ParameterWords); Assert.AreEqual(new string('C', 64), first.TypeSchemaHash, StringComparer.Ordinal);
        Assert.HasCount(64, first.SemanticIrHash); Assert.HasCount(64, first.Signature);
    }

    internal static void ArbitraryHostMethodsUnknownBanksAndMalformedSchemaAreRejected()
    {
        var composition = new Composition(new string('D', 64));
        MethodInfo host = typeof(Math).GetMethod(nameof(Math.Abs), [typeof(int)])!;
        Assert.ThrowsExactly<ArgumentException>(() => composition.Importer.Import(host, "host.abs", WarpPortableGeneratedServiceKind.Words));
        MethodInfo arena = typeof(WarpPortableHeapServices).GetMethod(nameof(WarpPortableHeapServices.AllocateObject), BindingFlags.Public | BindingFlags.Static)!;
        Assert.ThrowsExactly<ArgumentException>(() => composition.Importer.Import(arena, WarpPortableHeapLayout.Semantics, WarpPortableGeneratedServiceKind.Words));
        Assert.ThrowsExactly<ArgumentException>(() => composition.Importer.Import(arena, WarpPortableHeapLayout.Semantics, (WarpPortableGeneratedServiceKind)99));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpPortableGeneratedServiceImporter("not-a-schema", _ => 0, (_, _) => { }));
    }

    internal static void MixedFrameServiceClosureRequiresEveryExactStateAndArenaBinding()
    {
        MethodInfo read = typeof(WarpPortableHeapServices).GetMethod(nameof(WarpPortableHeapServices.ReadSourceFrameValue), BindingFlags.Public | BindingFlags.Static)!;
        var banks = WarpPortableSourceServiceBanks.Capture(read);
        CollectionAssert.AreEqual(new[] { WarpRuntimeWordBank.State, WarpRuntimeWordBank.Arena, WarpRuntimeWordBank.Word, WarpRuntimeWordBank.Word,
            WarpRuntimeWordBank.Word, WarpRuntimeWordBank.Word, WarpRuntimeWordBank.Word, WarpRuntimeWordBank.Word, WarpRuntimeWordBank.Word }, banks[read].ToArray());
        MethodInfo owner = typeof(WarpPortableFrameServices).GetMethod(nameof(WarpPortableFrameServices.ValidateOwner))!;
        Assert.AreEqual(WarpRuntimeWordBank.State, banks[owner][0]);
        var composition = new Composition(new string('E', 64));
        Assert.ThrowsExactly<ArgumentException>(() => composition.Importer.Import(read, WarpPortableSourceValueLayout.Semantics, WarpPortableGeneratedServiceKind.StateAndArena));
        WarpPortableGeneratedServiceImport imported = composition.Importer.Import(read, WarpPortableSourceValueLayout.Semantics, WarpPortableGeneratedServiceKind.StateAndArena, banks);
        Assert.IsTrue(imported.RequiresStateAccess); Assert.AreEqual(7, imported.ParameterWords);
        Assert.IsTrue(composition.Functions.SelectMany(function => function!.Blocks).SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == WarpManagedStateOpCode.LoadWord));
        Assert.IsTrue(composition.Functions.SelectMany(function => function!.Blocks).SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == WarpManagedMemoryOpCode.LoadWord));
    }

    internal static void SourceAliasPrefixEndsBeforeItsEvaluationStorage()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Fixtures).GetMethod(nameof(Fixtures.Source))!);
        WarpPortableWordLoweredProgram lowered = WarpPortableWordLowerer.Lower(graph, WarpPortableTypedProgram.Verify(graph));
        WarpPortableWordBody body = lowered.Bodies[0];
        Assert.AreEqual(body.Arguments.Sum(slot => slot.Type.WordCount) + body.Locals.Sum(slot => slot.Type.WordCount), body.StoragePrefixWords);
        Assert.AreEqual(body.EvaluationWordOffset, body.StoragePrefixWords); Assert.IsLessThan(body.PrivateWordCount, body.StoragePrefixWords);
    }

    private static WarpPortableSourceHeapSchema Schema()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Fixtures).GetMethod(nameof(Fixtures.Source))!);
        return WarpPortableSourceHeapSchema.Create(graph, WarpPortableTypedProgram.Verify(graph));
    }
    private static uint[] Execute(WarpLogicalMachineLayout layout, uint[] arguments, uint[] arena)
    {
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout); uint[] state = layout.CreateInitialState(64, 1);
        uint[][] inputs = arguments.Select(word => new[] { word }).ToArray();
        for (int quantum = 0; quantum < 100000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; quantum++)
        {
            compiled.ExecuteManagedQuantum(inputs, [], 0, state, 64, layout.MaximumBlockCost, arena);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return [state[layout.GetResultWordOffset(0, 64)], 1 - state[WarpLogicalMachineLayout.RemainingStepsLowOffset]];
    }
    private sealed class Composition
    {
        private readonly Dictionary<string, int> identities = new(StringComparer.Ordinal);
        private readonly List<WarpLogicalBodyMetadata?> metadata = [];
        internal Composition(string schema) => Importer = new(schema, Reserve, Store);
        internal WarpPortableGeneratedServiceImporter Importer { get; }
        internal List<WarpControlFlowFunction?> Functions { get; } = [];
        internal WarpPortableGeneratedServiceImport Import(Type type, string name, string semantics, WarpPortableGeneratedServiceKind kind) =>
            Importer.Import(type.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!, semantics, kind);
        private int Reserve(string identity)
        {
            if (identities.TryGetValue(identity, out int id)) { return id; }
            id = Functions.Count; identities.Add(identity, id); Functions.Add(null); metadata.Add(null); return id;
        }
        private void Store(WarpControlFlowFunction function, WarpLogicalBodyMetadata body)
        {
            if (Functions[function.Id] is { } previous) { Assert.IsTrue(WarpPortableWordServiceComparison.Equal(previous, function)); return; }
            Functions[function.Id] = function; metadata[function.Id] = body;
        }
        internal WarpLogicalMachineLayout Caller(WarpPortableGeneratedServiceImport service)
        {
            var instructions = new List<WarpIrInstruction>();
            for (int index = 0; index < service.ParameterWords; index++) { instructions.Add(new(index, WarpIrOpCode.LoadInput, immediate: (uint)index)); }
            int result = service.ParameterWords;
            instructions.Add(new(result, service.Function, Enumerable.Range(0, result), 1));
            instructions.Add(new(result + 1, WarpManagedFrameOpCode.StorePrivateWord, result, immediate: 0));
            instructions.Add(new(result + 2, WarpManagedFrameOpCode.LoadPrivateWord, immediate: 0));
            var kernel = new WarpControlFlowKernel(service.Identity, Math.Max(1, service.ParameterWords), 0,
                [new WarpBasicBlock(0, [], instructions, new WarpReturnTerminator(result + 2))], null,
                Functions.Select(function => function!), new WarpLogicalExecutionMetadata([new(1, false, [1]), .. metadata.Select(body => body!)], runtimeStateAccess: service.RequiresStateAccess));
            return new(kernel);
        }
    }
    private static class Fixtures { public static long Source(byte tag, long value) => value + tag; }
}
