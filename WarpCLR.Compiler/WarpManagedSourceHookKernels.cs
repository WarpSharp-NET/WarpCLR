using System.Reflection;
using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpManagedSourceHookKernels
{
    internal static IReadOnlyList<WarpLogicalMachineLayout> Create() =>
        [CreateByteAccess(write: false), CreateByteAccess(write: true), CreateStateBounds(), CreateBoundaries()];

    internal static WarpLogicalMachineLayout CreateByteAccess(bool write)
    {
        string operation = write ? nameof(WarpPortableFrameServices.WriteByte) : nameof(WarpPortableFrameServices.ReadByte);
        MethodInfo method = typeof(WarpPortableFrameServices).GetMethod(operation, BindingFlags.Public | BindingFlags.Static)!;
        WarpLogicalMachineLayout service = WarpWordStateServiceLowerer.Lower(method);
        WarpControlFlowFunction[] functions = Import(service.Kernel);
        var instructions = new List<WarpIrInstruction>
        {
            new(0, WarpIrOpCode.LoadInput, immediate: 0),
            new(1, WarpIrOpCode.LoadInput, immediate: 1),
            new(2, WarpIrOpCode.Constant, immediate: 0xA1B2C3D4),
            new(3, WarpManagedFrameOpCode.StorePrivateWord, left: 2),
            new(4, WarpManagedFrameOpCode.OwnerContext),
            new(5, WarpManagedFrameOpCode.OwnerFrame),
            new(6, WarpManagedFrameOpCode.OwnerGeneration),
            new(7, WarpIrOpCode.Constant, immediate: 0),
            new(8, WarpIrOpCode.Constant, immediate: 4),
            new(9, WarpIrOpCode.Constant, immediate: 1),
            new(10, WarpIrOpCode.Call, callee: 0, arguments: write ? [4, 5, 6, 7, 8, 9, 0, 1] : [4, 5, 6, 7, 8, 9, 0]),
            new(11, WarpIrOpCode.Constant, immediate: WarpLogicalMachineLayout.InteriorResultOffset),
            new(12, WarpManagedStateOpCode.LoadWord, left: 11),
        };
        WarpBasicBlock[] blocks = [new(0, [], instructions, new WarpTupleReturnTerminator([10, 12]))];
        WarpLogicalBodyMetadata[] bodies = [new(1, false, [1]), .. service.Kernel.Execution!.Bodies];
        var execution = new WarpLogicalExecutionMetadata(bodies, frameOwners: true, runtimeStateAccess: true);
        return new(new WarpControlFlowKernel("portable.source-hooks.frame-byte." + operation + "/" + WarpPortableFrameServices.Semantics,
            2, 0, blocks, null, functions, execution));
    }

    internal static WarpLogicalMachineLayout CreateStateBounds()
    {
        WarpBasicBlock[] blocks = [new(0, [], [new(0, WarpIrOpCode.LoadInput),
            new(1, WarpManagedStateOpCode.LoadWord, left: 0)], new WarpReturnTerminator(1))];
        return new(new WarpControlFlowKernel("portable.source-hooks.state-bounds", 1, 0, blocks, null, null,
            new([new(0, false, [1])], runtimeStateAccess: true)));
    }

    internal static WarpLogicalMachineLayout CreateBoundaries()
    {
        WarpBasicBlock[] blocks =
        [
            new(0, [], [new(0, WarpIrOpCode.LoadInput), new(1, WarpManagedFrameOpCode.StorePrivateWord, left: 0)],
                new WarpBranchTerminator(new(1, []))),
            new(1, [], [new(2, WarpManagedFrameOpCode.LoadPrivateWord), new(3, WarpIrOpCode.Constant, immediate: 1),
                new(4, WarpIrOpCode.Add, 2, 3), new(5, WarpManagedFrameOpCode.StorePrivateWord, left: 4)],
                new WarpBranchTerminator(new(2, []))),
            new(2, [], [new(6, WarpManagedFrameOpCode.LoadPrivateWord)], new WarpReturnTerminator(6)),
        ];
        return new(new WarpControlFlowKernel("portable.source-hooks.boundaries", 1, 0, blocks, null, null,
            new([new(1, false, [1, 1, 1])])));
    }

    private static WarpControlFlowFunction[] Import(WarpControlFlowKernel source) =>
        [new(0, source.Name, source.InputBufferCount, source.Blocks.Select(block => Remap(block, entry: true))),
            .. source.Functions.Select(function => new WarpControlFlowFunction(function.Id + 1, function.Name,
                function.ParameterCount, function.Blocks.Select(block => Remap(block, entry: false))))];

    private static WarpBasicBlock Remap(WarpBasicBlock block, bool entry) => new(block.Id, block.Parameters,
        block.Instructions.Select(instruction =>
        {
            if (entry && instruction.OpCode == WarpIrOpCode.LoadInput)
            {
                return new WarpIrInstruction(instruction.Result, WarpIrOpCode.LoadArgument, immediate: instruction.Immediate);
            }
            return instruction.OpCode == WarpIrOpCode.Call ? new WarpIrInstruction(instruction.Result, instruction.Callee + 1,
                instruction.Arguments, instruction.ResultWordCount) : instruction;
        }), block.Terminator);
}
