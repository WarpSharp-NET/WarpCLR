using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class MethodLowerer
    {
        private void InitializeRootInvocation()
        {
            if (!string.Equals(method.Identity, owner.Graph.EntryIdentity, StringComparison.Ordinal) ||
                owner.Program.EntryInitializerTrigger is not { } trigger ||
                owner.Binding is not WarpPortableClosedInitializerSourceBinding actual) { return; }
            uint type = actual.Plan.Schema.TypeId(trigger.DeclaringType);
            int inspect = InvocationBlock(), cached = InvocationBlock(), invoke = InvocationBlock();
            int complete = InvocationBlock(), rejected = InvocationBlock();
            WarpBranchTerminator first = Branch(method.Instructions.First(instruction => instruction.Reachable).Offset);
            int worker = Emit(WarpManagedInvocationOpCode.LoadLogicalWorker);
            int begun = InitializerCall(actual.BeginFunction, [Constant(type), worker], 1);
            blocks[0] = new(0, [], instructions, Zero(begun, inspect, rejected));
            instructions = [];
            int decision = Emit(WarpManagedMemoryOpCode.LoadWord, Constant(WarpPortableHeapLayout.Result));
            blocks[inspect] = new(inspect, [], instructions,
                new WarpConditionalBranchTerminator(Emit(WarpIrOpCode.Equal, decision, Constant(1)), new(invoke, []), new(cached, [])));
            instructions = [];
            int existing = Emit(WarpManagedMemoryOpCode.LoadWord, Constant(WarpPortableHeapLayout.Result));
            int ready = Emit(WarpIrOpCode.BitwiseOr, Emit(WarpIrOpCode.Equal, existing, Constant(2)), Emit(WarpIrOpCode.Equal, existing, Constant(3)));
            blocks[cached] = new(cached, [], instructions, new WarpConditionalBranchTerminator(ready, first.Target, new(rejected, [])));
            instructions = [];
            InitializerCall(owner.FunctionIds[trigger.Initializer], [], 0);
            blocks[invoke] = new(invoke, [], instructions, new WarpBranchTerminator(new(complete, [])));
            instructions = [];
            int finished = InitializerCall(actual.CompleteFunction, [Constant(type), Emit(WarpManagedInvocationOpCode.LoadLogicalWorker)], 1);
            blocks[complete] = new(complete, [], instructions, Zero(finished, first.Target.Block, rejected));
            instructions = [];
            Emit(WarpManagedStateOpCode.StoreWord, Constant(WarpLogicalMachineLayout.FaultKindOffset), Constant(3));
            Emit(WarpManagedStateOpCode.StoreWord, Constant(WarpLogicalMachineLayout.FaultFunctionOffset), Constant((uint)Body.Function));
            Emit(WarpManagedStateOpCode.StoreWord, Constant(WarpLogicalMachineLayout.FaultBlockOffset), Constant(0));
            Emit(WarpManagedStateOpCode.StoreWord, Constant(WarpLogicalMachineLayout.StatusOffset), Constant(WarpLogicalMachineLayout.Faulted));
            blocks[rejected] = new(rejected, [], instructions, new WarpStateDispatchTerminator([new(Body.Function, 0)], owner.Types[method.ReturnType].WordCount));
            Body = Body with
            {
                InvocationPrelude = new(method.Identity, trigger, type, actual.Plan.Initialization.PlanHash,
                    Body.SourceBlocks[0].Roots, [0, inspect, cached, invoke, complete, rejected]),
            };
            owner.Services.Add(WarpPortableWordInvocationPrelude.Semantics);
        }

        private int InvocationBlock() { int block = blocks.Count; blocks.Add(null); charges.Add(0); return block; }
        private int InitializerCall(int target, int[] args, int count)
        {
            int result = count == 0 ? -1 : value++;
            instructions.Add(new(result, target, args, count)); return result;
        }
        private WarpConditionalBranchTerminator Zero(int status, int yes, int no) =>
            new(Emit(WarpIrOpCode.Equal, status, Constant(0)), new(yes, []), new(no, []));
    }
}
