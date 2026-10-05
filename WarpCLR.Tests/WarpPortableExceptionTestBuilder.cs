using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

// This compiles a runtime protocol witness, not a CIL interpreter or a frontend
// substitute. Source CIL supplies immutable type/EH/site tables; the separately
// written word bodies expose capture/action boundaries under the real machine.
internal sealed partial class WarpPortableExceptionTestBuilder
{
    private readonly WarpPortableMethodGraph graph;
    private readonly WarpPortableTypedProgram typed;
    private readonly WarpPortableSourceHeapSchema schema;
    private readonly WarpPortableMethodGraphMethod[] methods;
    private readonly WarpPortableExceptionServiceLibrary services = new();
    private readonly List<WarpControlFlowFunction> functions = [];
    private readonly List<WarpLogicalBodyMetadata> metadata = [];
    private readonly List<WarpPortableExceptionBodyBinding> bindings = [];
    private readonly Dictionary<(int Function, int Offset), int> entries = [];
    private readonly bool stopHandlers;
    private int capture;
    private int apply;
    private int discard;
    private int transfer;
    private int terminal;
    private int next;
    private List<WarpIrInstruction> operations = [];

    private WarpPortableExceptionTestBuilder(MethodInfo source, bool stopHandlers)
    {
        this.stopHandlers = stopHandlers;
        graph = WarpPortableMethodGraph.Discover(source, concreteTypes: [typeof(StackOverflowException), typeof(InvalidOperationException)]);
        typed = WarpPortableTypedProgram.Verify(graph); schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        methods = graph.Methods.Where(method => method.Intrinsic is null && !method.Instructions.IsEmpty)
            .OrderBy(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal) ? 0 : 1).ThenBy(method => method.Id).ToArray();
        for (int function = 0; function < methods.Length; function++)
        {
            WarpPortableTypedMethod body = Typed(methods[function]);
            foreach ((WarpPortableTypedInstruction instruction, int block) in body.Instructions.Select((instruction, index) => (instruction, index + 1)))
            {
                entries.Add((function, instruction.Offset), block);
            }
        }
    }

    internal static WarpPortableExceptionTestProgram Create(string method, bool stopHandlers = true)
    {
        var builder = new WarpPortableExceptionTestBuilder(typeof(WarpPortableExceptionFixtureSources).GetMethod(method)!, stopHandlers);
        return builder.Build();
    }

    internal static WarpPortableExceptionTestProgram Create(MethodInfo source, bool stopHandlers = true) => new WarpPortableExceptionTestBuilder(source, stopHandlers).Build();

    private WarpPortableExceptionTestProgram Build()
    {
        int shift = methods.Length - 1;
        capture = services.Import(nameof(WarpPortableExceptionServices.CaptureFrames)) + shift;
        apply = services.Import(nameof(WarpPortableExceptionServices.ApplyAction)) + shift;
        discard = services.Import(nameof(WarpPortableExceptionServices.DiscardPrepared)) + shift;
        WarpStateDispatchTarget[] destinations = entries.Select(pair => new WarpStateDispatchTarget(pair.Key.Function, pair.Value)).ToArray();
        var sources = new List<WarpBasicBlock[]>();
        transfer = services.Functions.Count + shift; terminal = transfer + 1;
        for (int function = 0; function < methods.Length; function++) { sources.Add(Source(function)); }
        for (int function = 1; function < methods.Length; function++)
        {
            functions.Add(new(function - 1, "capture-only-source-protocol/" + methods[function].Identity, 3, sources[function]));
        }
        functions.AddRange(services.Functions.Select(function => Rebase(function, shift)));
        functions.Add(WarpPortableExceptionTransferLowerer.Create(transfer, destinations));
        functions.Add(WarpPortableExceptionTerminalLowerer.Create(terminal, destinations));
        foreach (WarpControlFlowFunction function in functions.Skip(shift)) { metadata.Add(new(0, true, new int[function.Blocks.Count])); }
        var execution = new WarpLogicalExecutionMetadata(metadata, recursiveCalls: false, frameOwners: true,
            runtimeStateAccess: true, nonlocalStateDispatch: true, managedExceptionTermination: true);
        var kernel = new WarpControlFlowKernel("capture-only-EH-protocol/" + graph.GraphHash, bindings[0].PrivateTemporaries.IsEmpty ? 3 : 6,
            0, sources[0], null, functions, execution);
        var layout = new WarpLogicalMachineLayout(kernel);
        WarpPortableExceptionPlan plan = WarpPortableExceptionPlan.Create(graph, typed, schema, layout, bindings);
        return new(graph, typed, schema, layout, bindings.ToImmutableArray(), plan, entries.ToImmutableDictionary());
    }

    private WarpPortableTypedMethod Typed(WarpPortableMethodGraphMethod method) => typed.Methods.First(body => string.Equals(body.Identity, method.Identity, StringComparison.Ordinal));

    private static WarpControlFlowFunction Rebase(WarpControlFlowFunction source, int shift) => new(source.Id + shift, source.Name,
        source.ParameterCount, source.Blocks.Select(block => new WarpBasicBlock(block.Id, block.Parameters,
            block.Instructions.Select(instruction => instruction.OpCode == WarpIrOpCode.Call ? new WarpIrInstruction(instruction.Result,
                instruction.Callee + shift, instruction.Arguments, instruction.ResultWordCount) : instruction), block.Terminator)));

    private WarpBasicBlock[] Source(int function)
    {
        next = 0; WarpPortableMethodGraphMethod method = methods[function]; WarpPortableTypedMethod body = Typed(method);
        int count = body.Instructions.Length;
        int[] returns = body.Instructions.Select((instruction, index) => (instruction, index))
            .Where(pair => !stopHandlers && method.ExceptionRegions.Any(region => region.HandlerOffset == pair.instruction.Offset && region.Kind == 0))
            .Select(pair => pair.index).ToArray();
        var blocks = new WarpBasicBlock[checked(1 + count * 7 + returns.Length)];
        operations = [];
        WarpPortableWordBody planned = InitializeStorage(function, method);
        WarpPortableTypedInstruction start = body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Throw.Value) ??
            body.Instructions.FirstOrDefault(instruction => instruction.Effects.Contains(WarpPortableTypedEffect.Call) &&
                methods.Any(callee => string.Equals(callee.Identity, method.Instructions.First(source => source.Offset == instruction.Offset).Method, StringComparison.Ordinal))) ??
            body.Instructions.FirstOrDefault(instruction => instruction.Effects.Contains(WarpPortableTypedEffect.Call)) ??
            body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Leave.Value || instruction.OpCode == OpCodes.Leave_S.Value) ?? body.Instructions[0];
        blocks[0] = Finish(0, [], new WarpBranchTerminator(new(entries[(function, start.Offset)], [])));
        var maps = ImmutableArray.CreateBuilder<WarpPortableExceptionSiteBinding>(count);
        for (int index = 0; index < count; index++)
        {
            int entry = index + 1; int waiting = 1 + count + index * 6;
            int returning = Array.IndexOf(returns, index);
            int completed = returning < 0 ? -1 : 1 + count * 7 + returning;
            int[] owned = index == 0 ? [entry, 0, waiting, waiting + 1, waiting + 2, waiting + 3, waiting + 4, waiting + 5] : [entry, waiting, waiting + 1, waiting + 2, waiting + 3, waiting + 4, waiting + 5];
            if (completed >= 0)
            {
                owned = [.. owned, completed];
                blocks[completed] = Finish(completed, [], new WarpReturnTerminator(Constant(101)));
            }
            maps.Add(new(body.Instructions[index].Offset, owned.ToImmutableArray()));
            blocks[entry] = CaptureBlock(function, method, body.Instructions[index], entry, waiting, completed);
            blocks[waiting] = WaitBlock(waiting);
            blocks[waiting + 1] = ApplyBlock(waiting + 1);
            blocks[waiting + 2] = CommitBlock(waiting + 2, terminal);
            blocks[waiting + 3] = CommitBlock(waiting + 3, transfer);
            blocks[waiting + 4] = MoveBlock(function, waiting + 4);
            blocks[waiting + 5] = RouteBlock(waiting + 5);
        }
        int[] charges = [0, .. Enumerable.Repeat(1, count), .. new int[count * 6 + returns.Length]];
        int privateWords = planned.PrivateTemporaries.IsEmpty ? 24 : planned.PrivateWordCount;
        int evaluation = planned.PrivateTemporaries.IsEmpty ? 12 : planned.EvaluationWordOffset;
        metadata.Add(new(privateWords, false, charges)); bindings.Add(new(method.Identity, function, privateWords, evaluation,
            maps.MoveToImmutable()) { PrivateTemporaries = planned.PrivateTemporaries });
        return blocks;
    }

    private WarpPortableWordBody InitializeStorage(int function, WarpPortableMethodGraphMethod method)
    {
        for (uint word = 0; word < 3; word++)
        {
            int owner = Op(function == 0 ? WarpIrOpCode.LoadInput : WarpIrOpCode.LoadArgument, immediate: word);
            Op(WarpManagedFrameOpCode.StorePrivateWord, owner, immediate: word);
        }
        WarpPortableWordBody planned = WarpPortableWordLowerer.PlanPrivateStorage(graph, typed, method.Identity, Math.Max(1, function));
        if (function == 0)
        {
            foreach (WarpPortableWordPrivateTemporary temporary in planned.PrivateTemporaries)
            {
                foreach (WarpPortableWordTemporaryOwner field in temporary.Owners)
                {
                    for (uint word = 0; word < 3; word++)
                    { Op(WarpManagedFrameOpCode.StorePrivateWord, Op(WarpIrOpCode.LoadInput, immediate: word + 3), immediate: (uint)field.PrivateWordOffset + word); }
                }
            }
        }
        return planned;
    }

    private int Op(WarpIrOpCode opcode, int left = -1, int right = -1, uint immediate = 0)
    {
        int value = next++; operations.Add(new(value, opcode, left, right, immediate)); return value;
    }
    private int Constant(uint value) => Op(WarpIrOpCode.Constant, immediate: value);
    private int Add(int left, uint offset) => Op(WarpIrOpCode.Add, left, Constant(offset));
    private int Load(int pointer, uint offset) => Op(WarpManagedMemoryOpCode.LoadWord, Add(pointer, offset));
    private int Scratch() => Op(WarpManagedMemoryOpCode.LoadWord, Constant(WarpPortableHeapLayout.ScratchStart));
    private int Call(int id, params int[] arguments) { int value = next++; operations.Add(new(value, id, arguments, 1)); return value; }
    private WarpBasicBlock Finish(int block, IEnumerable<WarpBlockParameter> parameters, WarpBlockTerminator terminator)
    {
        var result = new WarpBasicBlock(block, parameters, operations.ToArray(), terminator); operations = []; return result;
    }
}
