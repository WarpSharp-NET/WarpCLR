using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpPortableExceptionFilterLowerer
{
    internal const string Semantics = "warp.exception.filter/owned-prefix-alias-null-tail-atomic-driver-resume/0.1";

    internal static WarpControlFlowFunction Create(int id, int maximumPrivateWords, IEnumerable<WarpStateDispatchTarget> destinations)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPrivateWords);
        var builder = new Builder(maximumPrivateWords, destinations.ToArray());
        return new(id, Semantics, 3, [builder.Status(), builder.Guard(), builder.Routing(), builder.Enter(), builder.Finish(), builder.Reject()]);
    }

    private sealed class Builder(int maximumPrivateWords, WarpStateDispatchTarget[] destinations)
    {
        private int next;
        private readonly List<WarpIrInstruction> code = [];
        private int Op(WarpIrOpCode operation, int left = -1, int right = -1, uint immediate = 0)
        { int result = next++; code.Add(new(result, operation, left, right, immediate)); return result; }
        private int Constant(uint value) => Op(WarpIrOpCode.Constant, immediate: value);
        private int Add(int address, uint offset) => Op(WarpIrOpCode.Add, address, Constant(offset));
        private int Load(int address, uint offset) => Op(WarpManagedMemoryOpCode.LoadWord, Add(address, offset));
        private int State(int address, uint offset) => Op(WarpManagedStateOpCode.LoadWord, Add(address, offset));
        private void Store(int address, uint offset, int value) => Op(WarpManagedMemoryOpCode.StoreWord, Add(address, offset), value);
        private void StoreState(int address, uint offset, int value) => Op(WarpManagedStateOpCode.StoreWord, Add(address, offset), value);
        private int Equal(int value, uint constant) => Op(WarpIrOpCode.Equal, value, Constant(constant));
        private int And(int first, int second) => Op(WarpIrOpCode.BitwiseAnd, first, second);
        private int Select(int test, int yes, int no)
        { int value = next++; code.Add(new(value, WarpIrOpCode.Select, test, yes, third: no)); return value; }
        private WarpBasicBlock Block(int id, IEnumerable<WarpBlockParameter> parameters, WarpBlockTerminator terminator)
        { var result = new WarpBasicBlock(id, parameters, code.ToArray(), terminator); code.Clear(); return result; }
        private WarpBlockParameter[] Parameters() => Enumerable.Range(0, 9).Select(_ => new WarpBlockParameter(next++)).ToArray();
        private int Address(int descriptor, int worker, int index) => Op(WarpIrOpCode.Add, descriptor, Op(WarpIrOpCode.Add,
            Load(descriptor, WarpPortableExceptionLayout.RecordStart), Op(WarpIrOpCode.Multiply, Op(WarpIrOpCode.Add,
                Op(WarpIrOpCode.Multiply, worker, Load(descriptor, WarpPortableExceptionLayout.RecordsPerWorker)),
                Op(WarpIrOpCode.Subtract, index, Constant(1))), Constant(WarpPortableExceptionLayout.RecordWords))));
        private int Frame(int descriptor, int physical) => Op(WarpIrOpCode.Add, Constant(WarpLogicalMachineLayout.HeaderWords),
            Op(WarpIrOpCode.Multiply, Op(WarpIrOpCode.Subtract, physical, Constant(1)), Load(descriptor, WarpPortableExceptionLayout.StateStride)));

        internal WarpBasicBlock Status() => Block(0, [], new WarpConditionalBranchTerminator(
            Equal(Op(WarpIrOpCode.LoadArgument, immediate: 1), 0), new(1, []), new(5, [])));

        internal WarpBasicBlock Guard()
        {
            int worker = Op(WarpIrOpCode.LoadArgument, immediate: 0); int raise = Op(WarpIrOpCode.LoadArgument, immediate: 2);
            int descriptor = Op(WarpManagedMemoryOpCode.LoadWord, Constant(WarpPortableExceptionLayout.Descriptor));
            int entry = Op(WarpIrOpCode.Add, descriptor, Op(WarpIrOpCode.Add, Load(descriptor, WarpPortableExceptionLayout.WorkerStart),
                Op(WarpIrOpCode.Multiply, worker, Constant(WarpPortableExceptionLayout.WorkerWords))));
            int record = Address(descriptor, worker, Load(entry, WarpPortableExceptionLayout.ActiveRecord));
            int ready = Load(record, WarpPortableExceptionLayout.TransferReady);
            int physical = Load(record, WarpPortableExceptionLayout.TransferPhysical);
            int function = Load(record, WarpPortableExceptionLayout.TransferFunction); int pc = Load(record, WarpPortableExceptionLayout.TransferPc);
            // Entering this commit helper has itself consumed an activation.
            // Mint the alias/driver identity at the atomic commit, after every
            // preparation helper has returned, rather than reserving its tag.
            int activation = Add(State(Constant(0), WarpLogicalMachineLayout.NextActivationOffset), 1);
            int preparedActivation = Load(record, WarpPortableExceptionLayout.TransferActivation);
            int logical = Load(record, WarpPortableExceptionLayout.TransferLogicalDepth);
            int valid = Op(WarpIrOpCode.BitwiseOr, Equal(ready, 3), Op(WarpIrOpCode.BitwiseOr, Equal(ready, 4), Equal(ready, 5)));
            valid = And(valid, Op(WarpIrOpCode.Equal, Load(record, WarpPortableExceptionLayout.RaiseGeneration), raise));
            valid = And(valid, Op(WarpIrOpCode.Equal, State(Constant(0), WarpLogicalMachineLayout.OwnerContextOffset), Load(entry, WarpPortableExceptionLayout.OwnerContext)));
            valid = And(valid, Op(WarpIrOpCode.LessThanOrEqualUnsigned, preparedActivation, activation));
            valid = And(valid, Op(WarpIrOpCode.NotEqual, preparedActivation, Constant(0)));
            valid = And(valid, Op(WarpIrOpCode.NotEqual, State(Constant(0), WarpLogicalMachineLayout.NextActivationOffset), Constant(uint.MaxValue)));
            int[] values = [record, entry, descriptor, physical, function, pc, activation, logical, ready];
            return Block(1, [], new WarpConditionalBranchTerminator(valid, new(2, values), new(5, [])));
        }

        internal WarpBasicBlock Routing()
        {
            WarpBlockParameter[] p = Parameters(); int[] values = p.Select(parameter => parameter.Value).ToArray();
            return Block(2, p, new WarpConditionalBranchTerminator(Equal(p[8].Value, 3), new(3, values), new(4, values)));
        }

        internal WarpBasicBlock Enter()
        {
            WarpBlockParameter[] p = Parameters(); int record = p[0].Value; int descriptor = p[2].Value;
            int frame = Frame(descriptor, p[3].Value); int zero = Constant(0);
            Initialize(p, frame, Load(record, WarpPortableExceptionLayout.FilterPrivateWords),
                Load(record, WarpPortableExceptionLayout.FilterOwnerPhysical), Load(record, WarpPortableExceptionLayout.FilterOwnerActivation));
            int bank = Op(WarpIrOpCode.Add, frame, Load(descriptor, WarpPortableExceptionLayout.PrivateOffset));
            for (uint index = 0; index < maximumPrivateWords; index++) { StoreState(bank, index, zero); }
            int evaluation = Op(WarpIrOpCode.Add, bank, Load(record, WarpPortableExceptionLayout.FilterEvaluationOffset));
            for (uint index = 0; index < 3; index++) { StoreState(evaluation, index, Load(record, WarpPortableExceptionLayout.ExceptionReference + index)); }
            Store(record, WarpPortableExceptionLayout.FilterPhysical, p[3].Value);
            Store(record, WarpPortableExceptionLayout.FilterActivation, p[6].Value);
            Store(record, WarpPortableExceptionLayout.TransferActivation, p[6].Value);
            Store(record, WarpPortableExceptionLayout.TransferReady, zero);
            Store(record, WarpPortableExceptionLayout.Flags, Op(WarpIrOpCode.BitwiseOr, Load(record, WarpPortableExceptionLayout.Flags), Constant(2)));
            return Block(3, p, new WarpStateDispatchTerminator(destinations));
        }

        internal WarpBasicBlock Finish()
        {
            WarpBlockParameter[] p = Parameters(); int record = p[0].Value; int entry = p[1].Value; int descriptor = p[2].Value;
            int rejecting = Equal(p[8].Value, 5); int zero = Constant(0);
            int parent = Load(record, WarpPortableExceptionLayout.FilterParentRecord);
            int worker = Op(WarpIrOpCode.LoadArgument, immediate: 0);
            int resumed = Select(rejecting, Address(descriptor, worker, parent), record);
            int decision = Select(rejecting, zero, Load(record, WarpPortableExceptionLayout.FilterDecision));
            int root = Load(record, WarpPortableExceptionLayout.RecordRoot);
            for (uint owner = 0; owner < 2; owner++)
            {
                int row = Op(WarpIrOpCode.Add, Op(WarpManagedMemoryOpCode.LoadWord, Constant(WarpPortableHeapLayout.RootStart)),
                    Op(WarpIrOpCode.Multiply, Op(WarpIrOpCode.Subtract, Add(root, owner), Constant(1)), Constant(WarpPortableHeapLayout.RootWords)));
                for (uint word = 0; word < 3; word++) { uint field = WarpPortableHeapLayout.RootReference + word; Store(row, field, Select(rejecting, zero, Load(row, field))); }
            }
            for (uint word = 0; word < WarpPortableExceptionLayout.RecordWords; word++)
            {
                if (word != WarpPortableExceptionLayout.RecordRoot) { Store(record, word, Select(rejecting, zero, Load(record, word))); }
            }
            Store(resumed, WarpPortableExceptionLayout.FilterPhysical, zero); Store(resumed, WarpPortableExceptionLayout.FilterActivation, zero);
            Store(resumed, WarpPortableExceptionLayout.SelectedFrame, Select(decision, Load(resumed, WarpPortableExceptionLayout.SelectedFrame), zero));
            Store(resumed, WarpPortableExceptionLayout.SelectedClause, Select(decision, Load(resumed, WarpPortableExceptionLayout.SelectedClause), zero));
            Store(resumed, WarpPortableExceptionLayout.Phase, Select(decision, Constant(WarpPortableExceptionLayout.Unwinding), Constant(WarpPortableExceptionLayout.Searching)));
            Store(resumed, WarpPortableExceptionLayout.Flags, And(Load(resumed, WarpPortableExceptionLayout.Flags), Constant(1)));
            Store(resumed, WarpPortableExceptionLayout.Action, Constant(WarpPortableExceptionLayout.Continue));
            Store(resumed, WarpPortableExceptionLayout.TransferReady, zero);
            Store(entry, WarpPortableExceptionLayout.WorkerAction, Constant(WarpPortableExceptionLayout.Continue));
            Store(entry, WarpPortableExceptionLayout.ActiveRecord, Select(rejecting, parent, Load(entry, WarpPortableExceptionLayout.ActiveRecord)));
            Initialize(p, Frame(descriptor, p[3].Value), zero, zero, zero);
            return Block(4, p, new WarpStateDispatchTerminator(destinations));
        }

        private void Initialize(WarpBlockParameter[] p, int frame, int privateWords, int owner, int ownerActivation)
        {
            int zero = Constant(0);
            StoreState(frame, WarpLogicalMachineLayout.FrameFunctionOffset, p[4].Value);
            StoreState(frame, WarpLogicalMachineLayout.FrameProgramCounterOffset, p[5].Value);
            StoreState(frame, WarpLogicalMachineLayout.FrameReturnValueOffset, zero);
            // Aliases never ordinarily return. The driver is an ordinary
            // one-word helper whose unused return destination still satisfies
            // the common machine's exact frame ABI preflight.
            StoreState(frame, WarpLogicalMachineLayout.FrameReturnWordCountOffset, Select(Equal(p[8].Value, 3), zero, Constant(1)));
            StoreState(frame, WarpLogicalMachineLayout.FrameActivationOffset, p[6].Value);
            StoreState(frame, WarpLogicalMachineLayout.FramePrivateWordsOffset, privateWords);
            StoreState(frame, WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset, owner);
            StoreState(frame, WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset, ownerActivation);
            StoreState(zero, WarpLogicalMachineLayout.NextActivationOffset, p[6].Value);
            StoreState(zero, WarpLogicalMachineLayout.DepthOffset, p[3].Value);
            StoreState(zero, WarpLogicalMachineLayout.LogicalDepthOffset, p[7].Value);
            StoreState(zero, WarpLogicalMachineLayout.SourceBoundaryStateOffset, zero);
        }

        internal WarpBasicBlock Reject()
        {
            StoreState(Constant(0), WarpLogicalMachineLayout.FaultKindOffset, Constant(3));
            StoreState(Constant(0), WarpLogicalMachineLayout.StatusOffset, Constant(WarpLogicalMachineLayout.Faulted));
            return Block(5, [], new WarpStateDispatchTerminator(destinations));
        }
    }
}
