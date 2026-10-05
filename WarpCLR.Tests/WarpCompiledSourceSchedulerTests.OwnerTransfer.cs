using System.Security.Cryptography;
using System.Text;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CanonicalCompiledOwnerTransferPublishesCompleteCapabilitiesBeforeReleasingItsLoan(bool cancel)
    {
        var source = CaptureSource(nameof(Kernels.OwnerLoop));
        var plan = new WarpCompiledSourcePlan(source.Graph, source.Schema, source.Program, 7, 1, 4, 10000, 1, Services.Value);
        WarpCompiledSourceContext context = Bind(plan, [new uint[7], new uint[7], new uint[7], new uint[7]]);
        Assert.IsFalse(plan.Layout.RequiresManagedMemory);
        for (uint worker = 0; worker < 7; worker++) { context.QueueEntryAllocation(worker, ObjectType(context)); }
        WarpCompiledWorkerTicket loan = FindCanonicalOwnerWrite(context);
        uint revision = context.Arena[Worker(context, loan.Worker) + WarpPortableSchedulerLayout.RootRevision];
        Assert.AreEqual(1u, context.Arena[WarpPortableHeapLayout.LeaseState]);
        context.Execute(loan);
        AssertCompleteFrameOwners(context, loan.Worker);
        bool pending = context.MachineState(loan.Worker)[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable &&
            context.MachineState(loan.Worker)[WarpCompiledSourceBoundary.StateOffset] != WarpCompiledSourceBoundary.BeforeSource;
        if (cancel) { context.RequestCancellation(); }
        else { context.RequestCollection(); }
        context.Commit(loan);
        if (pending)
        {
            Assert.AreEqual(revision, context.Arena[Worker(context, loan.Worker) + WarpPortableSchedulerLayout.RootRevision]);
            Assert.AreEqual(1u, context.Arena[WarpPortableHeapLayout.LeaseState]);
            if (!cancel) { Assert.AreEqual(WarpPortableSchedulerLayout.NeedGCParking, context.AdvanceCollection()); }
        }
        for (int attempt = 0; context.Arena[WarpPortableHeapLayout.LeaseState] != 0 && attempt < 10000; attempt++)
        {
            context.Advance(0);
            AssertCompleteFrameOwners(context, loan.Worker);
        }
        Assert.AreEqual(0u, context.Arena[WarpPortableHeapLayout.LeaseState]);
        if (!cancel)
        {
            Assert.AreNotEqual(revision, context.Arena[Worker(context, loan.Worker) + WarpPortableSchedulerLayout.RootRevision]);
            Assert.AreEqual(0u, context.AdvanceCollection());
        }
        Drain(context);
        Assert.AreEqual(cancel ? WarpPortableSchedulerLayout.CancelledContext : WarpPortableSchedulerLayout.CompletedContext, context.State);
        if (cancel) { Assert.ThrowsExactly<InvalidOperationException>(() => context.Result(0, context.Dispatch)); }
        else { Assert.AreEqual(context.Arena[WarpPortableHeapLayout.Context], context.Result(0, context.Dispatch)[0]); }
    }

    private static WarpCompiledWorkerTicket FindCanonicalOwnerWrite(WarpCompiledSourceContext context)
    {
        for (int attempt = 0; attempt < 10000; attempt++)
        {
            WarpCompiledWorkerTicket? ticket = context.Claim(0);
            if (ticket is null) { continue; }
            uint[] state = context.MachineState(ticket.Worker).ToArray();
            WarpCompiledSourceLocation location = context.Plan.Location(state);
            if (!context.HasRuntimeHelper(ticket) && state[WarpCompiledSourceBoundary.StateOffset] == WarpCompiledSourceBoundary.BeforeSource &&
                location.Block is { } block && block.Instruction.Effects.Contains(WarpPortableTypedEffect.PrivateWrite) &&
                context.Plan.RequiresSourceLoan(state)) { return ticket; }
            context.Execute(ticket);
            AssertCompleteFrameOwners(context, ticket.Worker);
            context.Commit(ticket);
        }
        throw new AssertFailedException("The exact captured reference assignment did not acquire its source loan.");
    }

    private static void AssertCompleteFrameOwners(WarpCompiledSourceContext context, uint worker)
    {
        ReadOnlySpan<uint> state = context.MachineState(worker);
        for (int depth = 0; depth < state[WarpLogicalMachineLayout.DepthOffset]; depth++)
        {
            int frame = checked(WarpLogicalMachineLayout.HeaderWords + depth * context.Plan.Layout.FrameWords);
            int function = checked((int)state[frame + WarpLogicalMachineLayout.FrameFunctionOffset]);
            WarpPortableWordBody? body = context.Plan.Program.Bodies.FirstOrDefault(candidate => candidate.Function == function);
            if (body is null) { continue; }
            foreach (WarpPortableWordStorageSlot slot in body.Arguments.Concat(body.Locals)
                .Where(slot => slot.Type.Category == WarpPortableStackCategory.Reference))
            {
                int owner = checked(frame + context.Plan.Layout.PrivateOffset + slot.WordOffset);
                if (state[owner] == 0)
                {
                    Assert.AreEqual(0u, state[owner + 1]); Assert.AreEqual(0u, state[owner + 2]);
                    continue;
                }
                Assert.AreEqual(context.Arena[WarpPortableHeapLayout.Context], state[owner]);
                Assert.IsGreaterThan(0u, state[owner + 1]); Assert.IsGreaterThan(0u, state[owner + 2]);
                uint heapSlot = context.Arena[WarpPortableHeapLayout.SlotStart] + (state[owner + 1] - 1) * WarpPortableHeapLayout.SlotWords;
                Assert.AreEqual(WarpPortableHeapLayout.Allocated, context.Arena[heapSlot + WarpPortableHeapLayout.SlotState]);
                Assert.AreEqual(state[owner + 2], context.Arena[heapSlot + WarpPortableHeapLayout.SlotGeneration]);
            }
        }
    }

    // Retained manual IR instrumentation is used only to prove canonical admission rejects it.
    private static WarpPortableWordLoweredProgram DelayOwnerStore(WarpPortableWordLoweredProgram program)
    {
        WarpPortableWordBody body = program.Bodies[0];
        WarpPortableWordSourceBlock source = body.SourceBlocks.First(block => block.Instruction.Effects.Contains(WarpPortableTypedEffect.PrivateWrite) &&
            body.Locals.Any(local => local.Type.Category == WarpPortableStackCategory.Reference && string.Equals(local.Type.Identity, block.Instruction.MemoryType, StringComparison.Ordinal)));
        WarpControlFlowKernel original = program.Kernel;
        WarpControlFlowFunction function = original.Functions[body.Function - 1];
        WarpBasicBlock target = function.Blocks[source.Block];
        int firstStore = target.Instructions.ToList().FindIndex(instruction => instruction.OpCode == WarpManagedFrameOpCode.StorePrivateWord);
        var instructions = target.Instructions.Take(firstStore + 1).ToList();
        instructions.Add(new(function.ValueCount, WarpIrOpCode.Constant, immediate: 64));
        instructions.Add(new(function.ValueCount + 1, original.Functions.Count, [function.ValueCount], 1));
        instructions.AddRange(target.Instructions.Skip(firstStore + 1));
        WarpBasicBlock changed = new(target.Id, target.Parameters, instructions, target.Terminator);
        WarpControlFlowFunction[] functions = original.Functions.Select(item => item.Id == function.Id ?
            new WarpControlFlowFunction(item.Id, item.Name, item.ParameterCount, item.Blocks.Select(block => block.Id == target.Id ? changed : block)) : item).ToArray();
        WarpControlFlowFunction delay = DelayFunction(functions.Length);
        WarpLogicalExecutionMetadata execution = original.Execution!;
        var metadata = new WarpLogicalExecutionMetadata([.. execution.Bodies, new(0, true, Enumerable.Repeat(0, delay.Blocks.Count))],
            execution.RecursiveCalls, execution.FrameOwners, execution.RuntimeStateAccess);
        var kernel = new WarpControlFlowKernel(original.Name + "/delayed-owner-store-witness", original.InputBufferCount,
            original.ScalarArgumentCount, original.Blocks, original.Reduction, [.. functions, delay], metadata);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WarpPortableWordLowerer.Version + "\n" + program.VerifiedHash + "\n" + WarpIrHash.Compute(kernel))));
        return program with { Kernel = kernel, LoweredHash = hash };
    }

    private static WarpControlFlowFunction DelayFunction(int id) => new(id, "test/runtime-owner-transfer-delay", 1,
    [
        new(0, [], [new(0, WarpIrOpCode.LoadArgument, immediate: 0), new(1, WarpIrOpCode.Constant, immediate: 0)], new WarpBranchTerminator(new(1, [0, 1]))),
        new(1, [new(2), new(3)], [new(4, WarpIrOpCode.LessThanUnsigned, 3, 2)], new WarpConditionalBranchTerminator(4, new(2, [2, 3]), new(3, [3]))),
        new(2, [new(5), new(6)], [new(7, WarpIrOpCode.Constant, immediate: 1), new(8, WarpIrOpCode.Add, 6, 7)], new WarpBranchTerminator(new(1, [5, 8]))),
        new(3, [new(9)], [], new WarpReturnTerminator(9)),
    ]);
}
