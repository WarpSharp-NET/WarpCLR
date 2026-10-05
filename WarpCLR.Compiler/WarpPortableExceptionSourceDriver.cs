using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpPortableExceptionSourceDriver
{
    internal const string Semantics = "warp.exception.driver/compiled-bounded-two-pass-actions-invocation-owned-source-bank/0.2";

    internal static WarpControlFlowFunction Create(int id, IReadOnlyDictionary<string, int> services, int transfer, int terminal,
        uint controller, IReadOnlyCollection<WarpStateDispatchTarget> destinations, int filtering = -1)
    {
        var builder = new Builder(services, controller, destinations, filtering);
        List<WarpBasicBlock> blocks = [builder.SearchChoice(), builder.Search(), builder.UnwindChoice(), builder.Unwind(),
            builder.Apply(), builder.Commit(5, terminal), builder.Commit(6, transfer), builder.Reject()];
        if (filtering >= 0) { blocks.Add(builder.FilterChoice()); blocks.Add(builder.Commit(9, filtering)); }
        return new(id, Semantics, 0, blocks);
    }

    private sealed class Builder(IReadOnlyDictionary<string, int> services,
        uint controller, IReadOnlyCollection<WarpStateDispatchTarget> destinations, int filtering)
    {
        private int next;
        private readonly List<WarpIrInstruction> code = [];

        private int Op(WarpIrOpCode operation, int left = -1, int right = -1, uint immediate = 0)
        {
            int result = next++; code.Add(new(result, operation, left, right, immediate)); return result;
        }
        private int Constant(uint value) => Op(WarpIrOpCode.Constant, immediate: value);
        private int Add(int word, uint offset) => Op(WarpIrOpCode.Add, word, Constant(offset));
        private int Load(int word, uint offset) => Op(WarpManagedMemoryOpCode.LoadWord, Add(word, offset));
        private int Equal(int value, uint constant) => Op(WarpIrOpCode.Equal, value, Constant(constant));
        private int Call(int function, params int[] arguments) { int result = next++; code.Add(new(result, function, arguments, 1)); return result; }
        private WarpBasicBlock Finish(int block, IEnumerable<WarpBlockParameter> parameters, WarpBlockTerminator end)
        {
            var result = new WarpBasicBlock(block, parameters, code.ToArray(), end); code.Clear(); return result;
        }

        private int Record()
        {
            int descriptor = Op(WarpManagedMemoryOpCode.LoadWord, Constant(WarpPortableExceptionLayout.Descriptor));
            int worker = Op(WarpManagedInvocationOpCode.LoadLogicalWorker);
            int entry = Op(WarpIrOpCode.Add, descriptor, Op(WarpIrOpCode.Add, Load(descriptor, WarpPortableExceptionLayout.WorkerStart),
                Op(WarpIrOpCode.Multiply, worker, Constant(WarpPortableExceptionLayout.WorkerWords))));
            int index = Op(WarpIrOpCode.Subtract, Load(entry, WarpPortableExceptionLayout.ActiveRecord), Constant(1));
            return Op(WarpIrOpCode.Add, descriptor, Op(WarpIrOpCode.Add, Load(descriptor, WarpPortableExceptionLayout.RecordStart),
                Op(WarpIrOpCode.Multiply, Op(WarpIrOpCode.Add, Op(WarpIrOpCode.Multiply, worker,
                    Load(descriptor, WarpPortableExceptionLayout.RecordsPerWorker)), index), Constant(WarpPortableExceptionLayout.RecordWords))));
        }

        private int[] Ticket()
        {
            int scheduler = Op(WarpManagedMemoryOpCode.LoadWord, Constant(WarpPortableSchedulerLayout.HeapDescriptor));
            int worker = Op(WarpManagedInvocationOpCode.LoadLogicalWorker);
            int row = Op(WarpIrOpCode.Add, scheduler, Op(WarpIrOpCode.Add, Load(scheduler, WarpPortableSchedulerLayout.WorkerStart),
                Op(WarpIrOpCode.Multiply, worker, Constant(WarpPortableSchedulerLayout.WorkerWords))));
            return [Constant(controller), worker, Load(row, WarpPortableSchedulerLayout.RunGeneration),
                Load(scheduler, WarpPortableSchedulerLayout.DispatchGeneration), Load(Record(), WarpPortableExceptionLayout.RaiseGeneration)];
        }

        internal WarpBasicBlock SearchChoice() => Finish(0, [], new WarpConditionalBranchTerminator(
            Equal(Load(Record(), WarpPortableExceptionLayout.Phase), WarpPortableExceptionLayout.Searching), new(1, []), new(2, [])));

        internal WarpBasicBlock Search()
        {
            int status = Call(services[nameof(WarpPortableExceptionServices.AdvanceSearch)], Ticket());
            return Finish(1, [], new WarpConditionalBranchTerminator(Equal(status, 0), new(2, []), new(7, [])));
        }

        internal WarpBasicBlock UnwindChoice()
        {
            int phase = Load(Record(), WarpPortableExceptionLayout.Phase);
            int unwind = Op(WarpIrOpCode.BitwiseOr, Equal(phase, WarpPortableExceptionLayout.Unwinding), Equal(phase, WarpPortableExceptionLayout.Leaving));
            return Finish(2, [], new WarpConditionalBranchTerminator(unwind, new(3, []), new(4, [])));
        }

        internal WarpBasicBlock Unwind()
        {
            int status = Call(services[nameof(WarpPortableExceptionServices.AdvanceUnwind)], Ticket());
            return Finish(3, [], new WarpConditionalBranchTerminator(Equal(status, 0), new(4, []), new(7, [])));
        }

        internal WarpBasicBlock Apply()
        {
            int status = Call(services[nameof(WarpPortableExceptionServices.ApplyAction)], Ticket());
            int escaped = Equal(Load(Record(), WarpPortableExceptionLayout.Action), WarpPortableExceptionLayout.Escaped);
            return Finish(4, [], new WarpConditionalBranchTerminator(escaped, new(5, [status]), new(filtering >= 0 ? 8 : 6, [status])));
        }

        internal WarpBasicBlock FilterChoice()
        {
            var status = new WarpBlockParameter(next++); int action = Load(Record(), WarpPortableExceptionLayout.Action);
            int filter = Op(WarpIrOpCode.BitwiseOr, Equal(action, WarpPortableExceptionLayout.RunFilter), Equal(action, WarpPortableExceptionLayout.RejectFilter));
            return Finish(8, [status], new WarpConditionalBranchTerminator(filter, new(9, [status.Value]), new(6, [status.Value])));
        }

        internal WarpBasicBlock Commit(int block, int function)
        {
            var status = new WarpBlockParameter(next++);
            int result = Call(function, Op(WarpManagedInvocationOpCode.LoadLogicalWorker), status.Value, Load(Record(), WarpPortableExceptionLayout.RaiseGeneration));
            return Finish(block, [status], new WarpReturnTerminator(result));
        }

        internal WarpBasicBlock Reject()
        {
            Op(WarpManagedStateOpCode.StoreWord, Constant(WarpLogicalMachineLayout.FaultKindOffset), Constant(3));
            Op(WarpManagedStateOpCode.StoreWord, Constant(WarpLogicalMachineLayout.StatusOffset), Constant(WarpLogicalMachineLayout.Faulted));
            return Finish(7, [], new WarpStateDispatchTerminator(destinations));
        }
    }
}
