using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpPortableExceptionTransferLowerer
{
    internal const string Semantics = "warp.exception.transfer/status-before-memory-owned-triple-clear-leave-atomic-state-dispatch/0.2";

    // Arguments are the admitted worker index, ApplyAction status and raise ID.
    // It is a trusted helper with zero source charge, imported into that worker.
    internal static WarpControlFlowFunction Create(int id, IEnumerable<WarpStateDispatchTarget> destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        WarpStateDispatchTarget[] admitted = destinations.ToArray();
        var builder = new Builder();
        WarpBasicBlock status = builder.Status();
        WarpBasicBlock guard = builder.Guard();
        WarpBasicBlock commit = builder.Commit(admitted);
        WarpBasicBlock reject = builder.Reject(admitted);
        return new(id, Semantics, 3, [status, guard, commit, reject]);
    }

    private sealed class Builder
    {
        private int next;
        private readonly List<WarpIrInstruction> operations = [];
        private int record;
        private int worker;
        private int target;
        private int physical;
        private int pc;
        private int logical;

        private int Op(WarpIrOpCode operation, int left = -1, int right = -1, uint immediate = 0)
        {
            int value = next++; operations.Add(new(value, operation, left, right, immediate)); return value;
        }

        private int Constant(uint value) => Op(WarpIrOpCode.Constant, immediate: value);
        private int Add(int value, uint offset) => Op(WarpIrOpCode.Add, value, Constant(offset));
        private int LoadArena(int value, uint offset) => Op(WarpManagedMemoryOpCode.LoadWord, Add(value, offset));
        private int LoadState(int value, uint offset) => Op(WarpManagedStateOpCode.LoadWord, Add(value, offset));
        private int Equal(int value, uint constant) => Op(WarpIrOpCode.Equal, value, Constant(constant));
        private void StoreArena(int value, uint offset, int data) => Op(WarpManagedMemoryOpCode.StoreWord, Add(value, offset), data);
        private void StoreState(int value, uint offset, int data) => Op(WarpManagedStateOpCode.StoreWord, Add(value, offset), data);

        internal WarpBasicBlock Status()
        {
            int status = Op(WarpIrOpCode.LoadArgument, immediate: 1);
            int valid = Equal(status, 0);
            var block = new WarpBasicBlock(0, [], operations.ToArray(), new WarpConditionalBranchTerminator(valid, new(1, []), new(3, [])));
            operations.Clear(); return block;
        }

        internal WarpBasicBlock Guard()
        {
            int zero = Constant(0); int workerIndex = Op(WarpIrOpCode.LoadArgument, immediate: 0);
            int raise = Op(WarpIrOpCode.LoadArgument, immediate: 2);
            int descriptor = Op(WarpManagedMemoryOpCode.LoadWord, Constant(WarpPortableExceptionLayout.Descriptor));
            worker = Op(WarpIrOpCode.Add, descriptor, Op(WarpIrOpCode.Add, LoadArena(descriptor, WarpPortableExceptionLayout.WorkerStart),
                Op(WarpIrOpCode.Multiply, workerIndex, Constant(WarpPortableExceptionLayout.WorkerWords))));
            int index = LoadArena(worker, WarpPortableExceptionLayout.ActiveRecord);
            record = Op(WarpIrOpCode.Add, descriptor, Op(WarpIrOpCode.Add, LoadArena(descriptor, WarpPortableExceptionLayout.RecordStart),
                Op(WarpIrOpCode.Multiply, Op(WarpIrOpCode.Add, Op(WarpIrOpCode.Multiply, workerIndex,
                    LoadArena(descriptor, WarpPortableExceptionLayout.RecordsPerWorker)), Op(WarpIrOpCode.Subtract, index, Constant(1))), Constant(WarpPortableExceptionLayout.RecordWords))));
            physical = LoadArena(record, WarpPortableExceptionLayout.TransferPhysical); pc = LoadArena(record, WarpPortableExceptionLayout.TransferPc);
            logical = LoadArena(record, WarpPortableExceptionLayout.TransferLogicalDepth);
            target = Op(WarpIrOpCode.Add, Constant(WarpLogicalMachineLayout.HeaderWords), Op(WarpIrOpCode.Multiply,
                Op(WarpIrOpCode.Subtract, physical, Constant(1)), LoadArena(descriptor, WarpPortableExceptionLayout.StateStride)));
            int guard = Equal(LoadArena(record, WarpPortableExceptionLayout.TransferReady), 1);
            guard = And(guard, Op(WarpIrOpCode.Equal, LoadArena(record, WarpPortableExceptionLayout.RaiseGeneration), raise));
            guard = And(guard, Op(WarpIrOpCode.Equal, LoadState(zero, WarpLogicalMachineLayout.OwnerContextOffset), LoadArena(worker, WarpPortableExceptionLayout.OwnerContext)));
            guard = And(guard, Op(WarpIrOpCode.Equal, LoadState(target, WarpLogicalMachineLayout.FrameActivationOffset), LoadArena(record, WarpPortableExceptionLayout.TransferActivation)));
            guard = And(guard, Op(WarpIrOpCode.Equal, LoadState(target, WarpLogicalMachineLayout.FrameFunctionOffset), LoadArena(record, WarpPortableExceptionLayout.TransferFunction)));
            guard = And(guard, SupportedAction(LoadArena(record, WarpPortableExceptionLayout.Action)));
            int[] values = [record, worker, target, physical, pc, logical];
            var block = new WarpBasicBlock(1, [], operations.ToArray(), new WarpConditionalBranchTerminator(guard, new(2, values), new(3, [])));
            operations.Clear(); return block;
        }

        private int And(int first, int second) => Op(WarpIrOpCode.BitwiseAnd, first, second);

        private int SupportedAction(int action) => Op(WarpIrOpCode.BitwiseOr,
            Op(WarpIrOpCode.BitwiseOr, Equal(action, WarpPortableExceptionLayout.EnterCatch), Equal(action, WarpPortableExceptionLayout.RunFinally)),
            Op(WarpIrOpCode.BitwiseOr, Equal(action, WarpPortableExceptionLayout.RunFault), Equal(action, WarpPortableExceptionLayout.ResumeLeave)));

        internal WarpBasicBlock Commit(IEnumerable<WarpStateDispatchTarget> destinations)
        {
            WarpBlockParameter[] parameters = [new(next++), new(next++), new(next++), new(next++), new(next++), new(next++)];
            record = parameters[0].Value; worker = parameters[1].Value; target = parameters[2].Value;
            physical = parameters[3].Value; pc = parameters[4].Value; logical = parameters[5].Value;
            int zero = Constant(0);
            StoreState(zero, WarpLogicalMachineLayout.DepthOffset, physical);
            StoreState(zero, WarpLogicalMachineLayout.LogicalDepthOffset, logical);
            StoreState(zero, WarpLogicalMachineLayout.SourceBoundaryStateOffset, zero);
            StoreState(target, WarpLogicalMachineLayout.FrameProgramCounterOffset, pc);
            CommitRecord();
            var block = new WarpBasicBlock(2, parameters, operations.ToArray(), new WarpStateDispatchTerminator(destinations));
            operations.Clear(); return block;
        }

        private void CommitRecord()
        {
            int action = LoadArena(record, WarpPortableExceptionLayout.Action);
            int caught = Equal(action, WarpPortableExceptionLayout.EnterCatch); int leave = Equal(action, WarpPortableExceptionLayout.ResumeLeave);
            SelectStore(WarpPortableExceptionLayout.CaughtFrame, caught, physical);
            SelectStore(WarpPortableExceptionLayout.CaughtActivation, caught, LoadArena(record, WarpPortableExceptionLayout.TransferActivation));
            SelectStore(WarpPortableExceptionLayout.CaughtClause, caught, LoadArena(record, WarpPortableExceptionLayout.SelectedClause));
            int phase = Select(caught, Constant(WarpPortableExceptionLayout.Caught), LoadArena(record, WarpPortableExceptionLayout.Phase));
            StoreArena(record, WarpPortableExceptionLayout.Phase, Select(leave, Constant(0), phase));
            int restoreParent = Op(WarpIrOpCode.BitwiseOr, caught, leave);
            StoreArena(worker, WarpPortableExceptionLayout.ActiveRecord, Select(restoreParent,
                LoadArena(record, WarpPortableExceptionLayout.ParentRecord), LoadArena(worker, WarpPortableExceptionLayout.ActiveRecord)));
            StoreArena(record, WarpPortableExceptionLayout.TransferReady, Constant(0));
            StoreArena(record, WarpPortableExceptionLayout.Flags, Op(WarpIrOpCode.BitwiseOr, LoadArena(record, WarpPortableExceptionLayout.Flags), Constant(2)));
            ClearLeave(leave);
        }

        private void ClearLeave(int leave)
        {
            int root = LoadArena(record, WarpPortableExceptionLayout.RecordRoot);
            int start = Op(WarpManagedMemoryOpCode.LoadWord, Constant(WarpPortableHeapLayout.RootStart));
            for (uint index = 0; index < 2; index++)
            {
                int row = Op(WarpIrOpCode.Add, start, Op(WarpIrOpCode.Multiply,
                    Op(WarpIrOpCode.Subtract, Add(root, index), Constant(1)), Constant(WarpPortableHeapLayout.RootWords)));
                for (uint word = 0; word < 3; word++)
                {
                    uint offset = WarpPortableHeapLayout.RootReference + word;
                    StoreArena(row, offset, Select(leave, Constant(0), LoadArena(row, offset)));
                }
            }
            for (uint word = 0; word < WarpPortableExceptionLayout.RecordWords; word++)
            {
                if (word == WarpPortableExceptionLayout.RecordRoot) { continue; }
                StoreArena(record, word, Select(leave, Constant(0), LoadArena(record, word)));
            }
        }

        private int Select(int condition, int first, int second)
        {
            int value = next++; operations.Add(new(value, WarpIrOpCode.Select, condition, first, third: second)); return value;
        }

        private void SelectStore(uint field, int condition, int value) => StoreArena(record, field, Select(condition, value, LoadArena(record, field)));

        internal WarpBasicBlock Reject(IEnumerable<WarpStateDispatchTarget> destinations)
        {
            int zero = Constant(0);
            StoreState(zero, WarpLogicalMachineLayout.FaultKindOffset, Constant(3));
            StoreState(zero, WarpLogicalMachineLayout.StatusOffset, Constant(WarpLogicalMachineLayout.Faulted));
            var block = new WarpBasicBlock(3, [], operations.ToArray(), new WarpStateDispatchTerminator(destinations));
            operations.Clear(); return block;
        }
    }
}
