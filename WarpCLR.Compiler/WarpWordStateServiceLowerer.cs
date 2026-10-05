using System.Reflection;
using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpWordStateServiceLowerer
{
    internal const string Semantics = "warp.portable-runtime.word-state-cil/0.1";

    internal static WarpLogicalMachineLayout Lower(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        WarpLogicalMachineLayout arena = WarpWordArenaServiceLowerer.Lower(method);
        WarpControlFlowKernel original = arena.Kernel;
        WarpBasicBlock[] blocks = Remap(original.Blocks);
        WarpControlFlowFunction[] functions = original.Functions.Select(function =>
            new WarpControlFlowFunction(function.Id, function.Name, function.ParameterCount, Remap(function.Blocks))).ToArray();
        WarpLogicalBodyMetadata[] bodies = [new(0, true, Enumerable.Repeat(0, blocks.Length)),
            .. functions.Select(function => new WarpLogicalBodyMetadata(0, true, Enumerable.Repeat(0, function.Blocks.Count)))];
        var metadata = new WarpLogicalExecutionMetadata(bodies, recursiveCalls: false, runtimeStateAccess: true);
        return new(new WarpControlFlowKernel(original.Name + "/" + Semantics, original.InputBufferCount,
            original.ScalarArgumentCount, blocks, original.Reduction, functions, metadata));
    }

    private static WarpBasicBlock[] Remap(IEnumerable<WarpBasicBlock> blocks) => blocks.Select(block =>
        new WarpBasicBlock(block.Id, block.Parameters, block.Instructions.Select(Remap), block.Terminator)).ToArray();

    private static WarpIrInstruction Remap(WarpIrInstruction instruction)
    {
        WarpIrOpCode operation = instruction.OpCode switch
        {
            WarpManagedMemoryOpCode.WordCount => WarpManagedStateOpCode.WordCount,
            WarpManagedMemoryOpCode.LoadWord => WarpManagedStateOpCode.LoadWord,
            WarpManagedMemoryOpCode.StoreWord => WarpManagedStateOpCode.StoreWord,
            WarpManagedMemoryOpCode.WordAddress => WarpManagedStateOpCode.WordAddress,
            _ => instruction.OpCode,
        };
        return operation == instruction.OpCode ? instruction : new(instruction.Result, operation,
            instruction.Left, instruction.Right, instruction.Immediate, instruction.Third, instruction.ResultType,
            instruction.Callee, instruction.Arguments);
    }
}
