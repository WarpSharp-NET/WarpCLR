using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpPortableExceptionTerminalLowerer
{
    internal const string Semantics = "warp.exception.escaped/arena-validated-runtime-roots-atomic-terminal8/0.1";

    internal static WarpControlFlowFunction Create(int id, IEnumerable<WarpStateDispatchTarget> destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        var builder = new Builder();
        return new(id, Semantics, 3, [builder.StatusGuard(), builder.RecordGuard(), builder.Commit(), builder.Reject(destinations.ToArray())]);
    }

    private sealed class Builder
    {
        private int next;
        private readonly List<WarpIrInstruction> operations = [];

        private int Op(WarpIrOpCode operation, int left = -1, int right = -1, uint immediate = 0)
        {
            int value = next++; operations.Add(new(value, operation, left, right, immediate)); return value;
        }
        private int Constant(uint value) => Op(WarpIrOpCode.Constant, immediate: value);
        private int Add(int value, uint offset) => Op(WarpIrOpCode.Add, value, Constant(offset));
        private int Load(int value, uint offset) => Op(WarpManagedMemoryOpCode.LoadWord, Add(value, offset));
        private int Equal(int value, uint constant) => Op(WarpIrOpCode.Equal, value, Constant(constant));
        private int And(int first, int second) => Op(WarpIrOpCode.BitwiseAnd, first, second);
        private WarpBasicBlock Finish(int id, IEnumerable<WarpBlockParameter> parameters, WarpBlockTerminator terminator)
        {
            var block = new WarpBasicBlock(id, parameters, operations.ToArray(), terminator); operations.Clear(); return block;
        }

        internal WarpBasicBlock StatusGuard()
        {
            int status = Op(WarpIrOpCode.LoadArgument, immediate: 1);
            return Finish(0, [], new WarpConditionalBranchTerminator(Equal(status, 0), new(1, []), new(3, [])));
        }

        internal WarpBasicBlock RecordGuard()
        {
            int worker = Op(WarpIrOpCode.LoadArgument, immediate: 0); int raise = Op(WarpIrOpCode.LoadArgument, immediate: 2);
            int descriptor = Op(WarpManagedMemoryOpCode.LoadWord, Constant(WarpPortableExceptionLayout.Descriptor));
            int entry = Op(WarpIrOpCode.Add, descriptor, Op(WarpIrOpCode.Add, Load(descriptor, WarpPortableExceptionLayout.WorkerStart),
                Op(WarpIrOpCode.Multiply, worker, Constant(WarpPortableExceptionLayout.WorkerWords))));
            int index = Load(entry, WarpPortableExceptionLayout.ActiveRecord);
            int record = Op(WarpIrOpCode.Add, descriptor, Op(WarpIrOpCode.Add, Load(descriptor, WarpPortableExceptionLayout.RecordStart),
                Op(WarpIrOpCode.Multiply, Op(WarpIrOpCode.Add, Op(WarpIrOpCode.Multiply, worker,
                    Load(descriptor, WarpPortableExceptionLayout.RecordsPerWorker)), Op(WarpIrOpCode.Subtract, index, Constant(1))), Constant(WarpPortableExceptionLayout.RecordWords))));
            int guard = Equal(Load(record, WarpPortableExceptionLayout.TransferReady), 2);
            guard = And(guard, Equal(Load(record, WarpPortableExceptionLayout.Action), WarpPortableExceptionLayout.Escaped));
            guard = And(guard, Equal(Load(record, WarpPortableExceptionLayout.TerminalCommitted), 0));
            guard = And(guard, Op(WarpIrOpCode.Equal, Load(record, WarpPortableExceptionLayout.RaiseGeneration), raise));
            int owner = Op(WarpManagedStateOpCode.LoadWord, Constant(WarpLogicalMachineLayout.OwnerContextOffset));
            guard = And(guard, Op(WarpIrOpCode.Equal, owner, Load(entry, WarpPortableExceptionLayout.OwnerContext)));
            return Finish(1, [], new WarpConditionalBranchTerminator(guard, new(2, [record]), new(3, [])));
        }

        internal WarpBasicBlock Commit()
        {
            var record = new WarpBlockParameter(next++);
            int context = Load(record.Value, WarpPortableExceptionLayout.ExceptionReference);
            int slot = Load(record.Value, WarpPortableExceptionLayout.ExceptionReference + 1);
            int generation = Load(record.Value, WarpPortableExceptionLayout.ExceptionReference + 2);
            Op(WarpManagedMemoryOpCode.StoreWord, Add(record.Value, WarpPortableExceptionLayout.TerminalCommitted), Constant(1));
            Op(WarpManagedMemoryOpCode.StoreWord, Add(record.Value, WarpPortableExceptionLayout.TransferReady), Constant(0));
            return Finish(2, [record], new WarpManagedExceptionTerminator(context, slot, generation));
        }

        internal WarpBasicBlock Reject(IEnumerable<WarpStateDispatchTarget> destinations)
        {
            Op(WarpManagedStateOpCode.StoreWord, Constant(WarpLogicalMachineLayout.FaultKindOffset), Constant(3));
            Op(WarpManagedStateOpCode.StoreWord, Constant(WarpLogicalMachineLayout.StatusOffset), Constant(WarpLogicalMachineLayout.Faulted));
            return Finish(3, [], new WarpStateDispatchTerminator(destinations));
        }
    }
}
