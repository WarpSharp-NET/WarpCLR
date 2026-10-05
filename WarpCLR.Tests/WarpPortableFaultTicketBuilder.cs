using System.Collections.Immutable;
using System.Reflection;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

// Protocol-only word bodies use captured CIL/type/EH tables. They do not execute
// or admit the original implicit-fault source, and possess no registry grant.
internal sealed class WarpPortableFaultTicketBuilder
{
    private readonly List<WarpControlFlowFunction> functions = [];
    private readonly Dictionary<string, int> reserved = new(StringComparer.Ordinal);
    private readonly List<WarpIrInstruction> operations = [];
    private int next;
    private int capture, take, raise, acknowledge;

    internal static WarpPortableFaultTicketProgram Create(string method)
    {
        var builder = new WarpPortableFaultTicketBuilder();
        return builder.Build(typeof(WarpPortableFaultTicketSources).GetMethod(method)!);
    }

    private WarpPortableFaultTicketProgram Build(MethodInfo source)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableWordBody body = WarpPortableWordLowerer.PlanPrivateStorage(graph, typed, graph.EntryIdentity, 1);
        WarpPortableSourceFaultFactoryContract contract = WarpPortableSourceFaultFactoryContract.CaptureFinite(graph, typed, schema);
        WarpPortableSourceFaultPoolPlan pool = WarpPortableSourceFaultPoolPlan.Create(contract, schema, [body], 2);
        WarpPortableSourceFaultPoolRow row = pool.Rows.First();
        Reserve("source-protocol");
        var importer = new WarpPortableGeneratedServiceImporter(schema.SchemaHash, Reserve, Store);
        capture = Import(importer, WarpPortableExceptionServices.Method(nameof(WarpPortableExceptionServices.CaptureFrames)));
        take = Import(importer, WarpPortableSourceFaultServices.Method(nameof(WarpPortableSourceFaultServices.TakePreparedFault)));
        raise = Import(importer, WarpPortableSourceFaultServices.Method(nameof(WarpPortableSourceFaultServices.RaiseTakenPreparedFault)));
        acknowledge = Import(importer, WarpPortableSourceFaultServices.Method(nameof(WarpPortableSourceFaultServices.AcknowledgeRaisedFault)));
        int index = body.SourceBlocks.Select((block, index) => (block, index))
            .First(pair => pair.block.Instruction.Offset == row.Factory.SourceOffset).index;
        int entry = index + 1, waiting = body.SourceBlocks.Length + 1;
        (WarpBasicBlock[] blocks, ImmutableArray<WarpPortableExceptionSiteBinding> sites) = Source(body, entry, waiting);
        functions[0] = new(0, "fault-ticket-consistency-protocol/" + graph.EntryIdentity, 0, blocks);
        WarpBasicBlock wrapper = new(0, [], [new WarpIrInstruction(0, 0, [], 1)], new WarpReturnTerminator(0));
        int[] costs = [0, .. Enumerable.Repeat(1, body.SourceBlocks.Length), .. new int[9]];
        WarpLogicalBodyMetadata[] metadata = [new(0, true, [0]), new(body.PrivateWordCount, false, costs),
            .. functions.Skip(1).Select(function => new WarpLogicalBodyMetadata(0, true, new int[function.Blocks.Count]))];
        var execution = new WarpLogicalExecutionMetadata(metadata, frameOwners: true, runtimeStateAccess: true);
        var layout = new WarpLogicalMachineLayout(new WarpControlFlowKernel("fault-ticket-consistency-protocol/" + pool.PlanHash,
            1, 0, [wrapper], null, functions, execution));
        WarpPortableExceptionPlan exceptions = WarpPortableExceptionPlan.Create(graph, typed, schema, layout,
            [new(graph.EntryIdentity, 1, body.PrivateWordCount, body.EvaluationWordOffset, sites) { PrivateTemporaries = body.PrivateTemporaries }]);
        return new(graph, typed, schema, body, pool, row, layout, exceptions, waiting, entry);
    }

    private static int Import(WarpPortableGeneratedServiceImporter importer, MethodInfo method) => importer.Import(method,
        "fault-ticket-consistency-protocol", WarpPortableGeneratedServiceKind.StateAndArena, WarpPortableSourceServiceBanks.Capture(method)).Function;

    private int Reserve(string name)
    {
        if (reserved.TryGetValue(name, out int existing)) { return existing; }
        int id = functions.Count; reserved.Add(name, id);
        functions.Add(new(id, "unpublished-protocol-function", 0, [new WarpBasicBlock(0, [], [new WarpIrInstruction(0, WarpIrOpCode.Constant)], new WarpReturnTerminator(0))]));
        return id;
    }

    private void Store(WarpControlFlowFunction function, WarpLogicalBodyMetadata metadata)
    {
        Assert.IsTrue(metadata.RuntimeHelper); functions[function.Id] = function;
    }

    private (WarpBasicBlock[], ImmutableArray<WarpPortableExceptionSiteBinding>) Source(WarpPortableWordBody body, int faultEntry, int waiting)
    {
        int count = body.SourceBlocks.Length; var blocks = new WarpBasicBlock[count + 10];
        var sites = ImmutableArray.CreateBuilder<WarpPortableExceptionSiteBinding>(count);
        blocks[0] = Finish(0, new WarpBranchTerminator(new(1, [])));
        for (int index = 0; index < count; index++)
        {
            int entry = index + 1;
            if (entry == faultEntry)
            {
                Call(capture, [Constant(WarpPortableExceptionTestBuilder.Controller), Constant(0), Constant(1), Constant(1)]);
            }
            blocks[entry] = Finish(entry, new WarpBranchTerminator(new(entry < count ? entry + 1 : waiting, [])));
            int[] owned = entry == faultEntry ? [entry, .. Enumerable.Range(waiting, 9)] : [entry];
            if (index == 0) { owned = [.. owned, 0]; }
            sites.Add(new(body.SourceBlocks[index].Instruction.Offset, owned.ToImmutableArray()));
        }
        blocks[waiting] = Choice(waiting, 1, waiting + 3, waiting + 1);
        blocks[waiting + 1] = Choice(waiting + 1, 2, waiting + 4, waiting + 2);
        blocks[waiting + 2] = Choice(waiting + 2, 3, waiting + 5, waiting + 7);
        blocks[waiting + 3] = Invoke(waiting + 3, take, 23, waiting + 6);
        blocks[waiting + 4] = Invoke(waiting + 4, raise, 23, waiting + 6);
        blocks[waiting + 5] = Invoke(waiting + 5, acknowledge, 24, waiting + 6);
        Op(WarpManagedMemoryOpCode.StoreWord, Scratch(), Constant(0));
        blocks[waiting + 6] = Finish(waiting + 6, new WarpBranchTerminator(new(waiting, [])));
        blocks[waiting + 7] = Choice(waiting + 7, 4, waiting + 8, waiting);
        blocks[waiting + 8] = Finish(waiting + 8, new WarpReturnTerminator(Constant(0)));
        return (blocks, sites.MoveToImmutable());
    }

    private WarpBasicBlock Choice(int block, uint command, int yes, int no) => Finish(block,
        new WarpConditionalBranchTerminator(Op(WarpIrOpCode.Equal, Load(Scratch(), 0), Constant(command)), new(yes, []), new(no, [])));

    private WarpBasicBlock Invoke(int block, int function, int count, int completed)
    {
        int result = Call(function, Enumerable.Range(0, count).Select(index => Load(Scratch(), (uint)index + 8)).ToArray());
        Op(WarpManagedMemoryOpCode.StoreWord, Add(Scratch(), 1), result);
        return Finish(block, new WarpBranchTerminator(new(completed, [])));
    }

    private int Op(WarpIrOpCode opcode, int left = -1, int right = -1, uint immediate = 0)
    { int result = next++; operations.Add(new(result, opcode, left, right, immediate)); return result; }
    private int Constant(uint value) => Op(WarpIrOpCode.Constant, immediate: value);
    private int Add(int source, uint offset) => Op(WarpIrOpCode.Add, source, Constant(offset));
    private int Scratch() => Op(WarpManagedMemoryOpCode.LoadWord, Constant(WarpPortableHeapLayout.ScratchStart));
    private int Load(int source, uint offset) => Op(WarpManagedMemoryOpCode.LoadWord, Add(source, offset));
    private int Call(int function, int[] arguments) { int result = next++; operations.Add(new(result, function, arguments, 1)); return result; }
    private WarpBasicBlock Finish(int block, WarpBlockTerminator terminal)
    { var result = new WarpBasicBlock(block, [], operations.ToArray(), terminal); operations.Clear(); return result; }
}
