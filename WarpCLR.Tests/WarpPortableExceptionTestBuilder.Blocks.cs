using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTestBuilder
{
    internal const uint Controller = 0xCAFE;

    private WarpBasicBlock CaptureBlock(int function, WarpPortableMethodGraphMethod method, WarpPortableTypedInstruction instruction, int block, int waiting, int completed)
    {
        if (completed >= 0)
        {
            // Keep both protocol continuations in the immutable CFG. The test's
            // completion choice is constant; no production reachability rule is
            // relaxed to accommodate its inspection-only handler body.
            return Finish(block, [], new WarpConditionalBranchTerminator(Constant(1), new(completed, []), new(waiting, [])));
        }
        WarpPortableMethodGraphInstruction source = method.Instructions.First(source => source.Offset == instruction.Offset);
        // A nonlocal transfer has consumed the inspection command. Retire it
        // inside the compiled fixture before recapture/its next wait, so a large
        // operational quantum cannot accidentally replay ApplyAction.
        Op(WarpManagedMemoryOpCode.StoreWord, Scratch(), Constant(0));
        int callee = source.Method is null ? -1 : Array.FindIndex(methods, method => string.Equals(method.Identity, source.Method, StringComparison.Ordinal));
        if (callee > 0)
        {
            int[] arguments = [Op(WarpManagedFrameOpCode.LoadPrivateWord, immediate: 0), Op(WarpManagedFrameOpCode.LoadPrivateWord, immediate: 1), Op(WarpManagedFrameOpCode.LoadPrivateWord, immediate: 2)];
            Call(callee - 1, arguments);
        }
        else
        {
            Call(capture, Constant(Controller), Constant(0), RuntimeRun(), Constant(1));
        }
        return Finish(block, [], new WarpBranchTerminator(new(waiting, [])));
    }

    private WarpBasicBlock WaitBlock(int block)
    {
        int command = Load(Scratch(), 0);
        int moving = Op(WarpIrOpCode.Equal, command, Constant(2));
        // A finite operational quantum is the fixture's inspection boundary;
        // this test-only pause never stands in for a production source boundary.
        return Finish(block, [], new WarpConditionalBranchTerminator(moving, new(block + 4, []), new(block + 5, [])));
    }

    private WarpBasicBlock RouteBlock(int block)
    {
        int applying = Op(WarpIrOpCode.Equal, Load(Scratch(), 0), Constant(1));
        return Finish(block, [], new WarpConditionalBranchTerminator(applying, new(block - 4, []), new(block - 5, [])));
    }

    private WarpBasicBlock ApplyBlock(int block)
    {
        int status = Call(apply, Constant(Controller), Constant(0), RuntimeRun(), Constant(1), Raise());
        int action = ActiveRecordField(WarpPortableExceptionLayout.Action);
        int escaped = Op(WarpIrOpCode.Equal, action, Constant(WarpPortableExceptionLayout.Escaped));
        return Finish(block, [], new WarpConditionalBranchTerminator(escaped, new(block + 1, [status]), new(block + 2, [status])));
    }

    private WarpBasicBlock CommitBlock(int block, int function)
    {
        var status = new WarpBlockParameter(next++);
        // Ordinary action paths enter the actual transfer helper. Move is a
        // test-only admitted nonlocal continuation, not a source-method call.
        int result = Call(function, Constant(0), status.Value, Raise());
        return Finish(block, [status], new WarpReturnTerminator(result));
    }

    private WarpBasicBlock MoveBlock(int function, int block)
    {
        // A previously chosen branch can reach another capture before this
        // inspection command. Retire that exact prepared snapshot here, inside
        // the compiled continuation, immediately before moving the fixture PC.
        Call(discard, Constant(Controller), Constant(0), RuntimeRun(), Constant(1));
        int depth = Op(WarpManagedStateOpCode.LoadWord, Constant(WarpLogicalMachineLayout.DepthOffset));
        int stride = Op(WarpManagedStateOpCode.LoadWord, Constant(WarpLogicalMachineLayout.FrameStrideOffset));
        int frame = Op(WarpIrOpCode.Add, Constant(WarpLogicalMachineLayout.HeaderWords), Op(WarpIrOpCode.Multiply, Op(WarpIrOpCode.Subtract, depth, Constant(1)), stride));
        Op(WarpManagedStateOpCode.StoreWord, Add(frame, WarpLogicalMachineLayout.FrameProgramCounterOffset), Load(Scratch(), 1));
        Op(WarpManagedMemoryOpCode.StoreWord, Scratch(), Constant(0));
        return Finish(block, [], new WarpStateDispatchTerminator(entries.Where(pair => pair.Key.Function == function)
            .Select(pair => new WarpStateDispatchTarget(function, pair.Value))));
    }

    private int ActiveRecordField(uint field)
    {
        int descriptor = Op(WarpManagedMemoryOpCode.LoadWord, Constant(WarpPortableExceptionLayout.Descriptor));
        int worker = Op(WarpIrOpCode.Add, descriptor, Load(descriptor, WarpPortableExceptionLayout.WorkerStart));
        int record = Op(WarpIrOpCode.Add, descriptor, Op(WarpIrOpCode.Add, Load(descriptor, WarpPortableExceptionLayout.RecordStart),
            Op(WarpIrOpCode.Multiply, Op(WarpIrOpCode.Subtract, Load(worker, WarpPortableExceptionLayout.ActiveRecord), Constant(1)), Constant(WarpPortableExceptionLayout.RecordWords))));
        return Load(record, field);
    }
    private int Raise() => ActiveRecordField(WarpPortableExceptionLayout.RaiseGeneration);

    private int RuntimeRun()
    {
        int scheduler = Op(WarpManagedMemoryOpCode.LoadWord, Constant(WarpPortableSchedulerLayout.HeapDescriptor));
        int worker = Op(WarpIrOpCode.Add, scheduler, Load(scheduler, WarpPortableSchedulerLayout.WorkerStart));
        return Load(worker, WarpPortableSchedulerLayout.RunGeneration);
    }
}
