using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed record WarpCilCallTarget(
    int FunctionId,
    int ParameterCount,
    string Identity);

internal sealed class WarpIntegerMapMethodBody
{
    public WarpIntegerMapMethodBody(
        string identity,
        int parameterCount,
        int inputBufferCount,
        int maxStack,
        int localCount,
        ReadOnlySpan<byte> il,
        WarpReductionOperation? reduction = null,
        bool localsInitialized = false,
        IReadOnlyDictionary<int, WarpCilCallTarget>? callTargets = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentOutOfRangeException.ThrowIfNegative(parameterCount);
        ArgumentOutOfRangeException.ThrowIfNegative(inputBufferCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(inputBufferCount, parameterCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maxStack);
        ArgumentOutOfRangeException.ThrowIfNegative(localCount);
        if (reduction.HasValue && !Enum.IsDefined(reduction.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(reduction));
        }

        Identity = identity;
        ParameterCount = parameterCount;
        InputBufferCount = inputBufferCount;
        MaxStack = maxStack;
        LocalCount = localCount;
        Il = il.ToArray();
        Reduction = reduction;
        LocalsInitialized = localsInitialized;
        CallTargets = callTargets ?? new Dictionary<int, WarpCilCallTarget>();
    }

    public string Identity { get; }

    public int ParameterCount { get; }

    public int InputBufferCount { get; }

    public int MaxStack { get; }

    public int LocalCount { get; }

    public byte[] Il { get; }

    public WarpReductionOperation? Reduction { get; }

    public bool LocalsInitialized { get; }

    public IReadOnlyDictionary<int, WarpCilCallTarget> CallTargets { get; }
}

internal static class WarpIntegerMapCilVerifier
{
    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue = CreateOpCodeMap();

    public static WarpIntegerMapKernel Verify(WarpIntegerMapMethodBody method)
        => Verify(method, []);

    public static WarpIntegerMapKernel Verify(
        WarpIntegerMapMethodBody method,
        IReadOnlyList<WarpIntegerMapMethodBody> functions)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(functions);

        LoweredBody entry = VerifyAndLower(method, isEntry: true);
        var loweredFunctions = new WarpControlFlowFunction[functions.Count];
        for (int functionId = 0; functionId < functions.Count; functionId++)
        {
            WarpIntegerMapMethodBody function = functions[functionId];
            LoweredBody lowered = VerifyAndLower(function, isEntry: false);
            loweredFunctions[functionId] = new WarpControlFlowFunction(
                functionId,
                function.Identity,
                function.ParameterCount,
                lowered.Blocks);
        }

        var controlFlow = new WarpControlFlowKernel(
            method.Identity,
            method.InputBufferCount,
            method.ParameterCount - method.InputBufferCount,
            entry.Blocks,
            method.Reduction,
            loweredFunctions);
        return new WarpIntegerMapKernel(controlFlow);
    }

    public static IReadOnlyList<int> ReadCallTokens(ReadOnlySpan<byte> il) => Decode(il)
        .Where(instruction => Is(instruction.OpCode, OpCodes.Call))
        .Select(instruction => instruction.Operand)
        .ToArray();

    private static LoweredBody VerifyAndLower(
        WarpIntegerMapMethodBody method,
        bool isEntry)
    {
        DecodedInstruction[] instructions = Decode(method.Il);
        if (instructions.Length == 0)
        {
            throw CilError("WRPCIL1005", "The method does not end with ret.", 0);
        }

        ValidateSupportedInstructions(method, instructions);
        CilBlock[] blocks = BuildBlocks(method, instructions);
        FlowShape[] entryShapes = AnalyzeFlow(method, blocks);
        return Lower(method, blocks, entryShapes, isEntry);
    }

    private static DecodedInstruction[] Decode(ReadOnlySpan<byte> il)
    {
        var result = new List<DecodedInstruction>();
        var reader = new IlReader(il);

        while (!reader.IsComplete)
        {
            int offset = reader.Offset;
            OpCode opCode = ReadOpCode(ref reader, offset);
            int operand = 0;
            int? branchTarget = null;
            int[]? switchTargets = null;

            switch (opCode.OperandType)
            {
                case OperandType.InlineNone:
                    break;

                case OperandType.ShortInlineBrTarget:
                    int shortDelta = reader.ReadSByte();
                    branchTarget = ComputeBranchTarget(reader.Offset, shortDelta, offset);
                    break;

                case OperandType.InlineBrTarget:
                    int delta = reader.ReadInt32();
                    branchTarget = ComputeBranchTarget(reader.Offset, delta, offset);
                    break;

                case OperandType.ShortInlineI:
                    operand = reader.ReadSByte();
                    break;

                case OperandType.ShortInlineVar:
                    operand = reader.ReadByte();
                    break;

                case OperandType.InlineVar:
                    operand = reader.ReadUInt16();
                    break;

                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    operand = reader.ReadInt32();
                    break;

                case OperandType.InlineI8:
                case OperandType.InlineR:
                    _ = reader.ReadInt32();
                    _ = reader.ReadInt32();
                    break;

                case OperandType.InlineSwitch:
                    int count = reader.ReadInt32();
                    if (count < 0 || count > reader.Remaining / sizeof(int))
                    {
                        throw CilError(
                            "WRPCIL1011",
                            "The CIL switch operand is incomplete.",
                            offset);
                    }

                    var deltas = new int[count];
                    for (int index = 0; index < count; index++)
                    {
                        deltas[index] = reader.ReadInt32();
                    }

                    int switchBase = reader.Offset;
                    switchTargets = deltas
                        .Select(value => ComputeBranchTarget(switchBase, value, offset))
                        .ToArray();
                    break;

                default:
                    throw CilError(
                        "WRPCIL1010",
                        $"Opcode '{opCode.Name}' has an unknown operand encoding.",
                        offset);
            }

            result.Add(
                new DecodedInstruction(
                    offset,
                    reader.Offset,
                    opCode,
                    operand,
                    branchTarget,
                    switchTargets));
        }

        return result.ToArray();
    }

    private static void ValidateSupportedInstructions(
        WarpIntegerMapMethodBody method,
        IReadOnlyList<DecodedInstruction> instructions)
    {
        foreach (DecodedInstruction instruction in instructions)
        {
            OpCode opCode = instruction.OpCode;
            if (Is(opCode, OpCodes.Nop) ||
                Is(opCode, OpCodes.Not) ||
                Is(opCode, OpCodes.Conv_U4) ||
                Is(opCode, OpCodes.Conv_I4) ||
                Is(opCode, OpCodes.Dup) ||
                Is(opCode, OpCodes.Pop) ||
                Is(opCode, OpCodes.Ret) ||
                IsUnconditionalBranch(opCode) ||
                TryGetArgumentIndex(instruction, out _) ||
                TryGetArgumentWriteIndex(instruction, out _) ||
                TryGetConstant(instruction, out _) ||
                TryGetLocalReadIndex(instruction, out _) ||
                TryGetLocalWriteIndex(instruction, out _) ||
                TryGetBinaryOperator(opCode, out _) ||
                TryGetComparisonOperator(opCode, out _) ||
                Is(opCode, OpCodes.Call) && method.CallTargets.ContainsKey(instruction.Operand) ||
                IsConditionalBranch(opCode))
            {
                continue;
            }

            throw CilError(
                "WRPCIL1001",
                $"Opcode '{opCode.Name}' is outside the integer map profile.",
                instruction.Offset);
        }
    }

    private static CilBlock[] BuildBlocks(
        WarpIntegerMapMethodBody method,
        IReadOnlyList<DecodedInstruction> instructions)
    {
        var instructionsByOffset = instructions.ToDictionary(instruction => instruction.Offset);
        var leaders = new HashSet<int> { 0 };

        foreach (DecodedInstruction instruction in instructions)
        {
            if (instruction.BranchTarget.HasValue)
            {
                int target = instruction.BranchTarget.Value;
                ValidateBranchTarget(target, instruction.Offset, instructionsByOffset);
                leaders.Add(target);
            }

            if ((IsUnconditionalBranch(instruction.OpCode) ||
                 IsConditionalBranch(instruction.OpCode) ||
                 Is(instruction.OpCode, OpCodes.Ret)) &&
                instruction.NextOffset < method.Il.Length)
            {
                leaders.Add(instruction.NextOffset);
            }
        }

        int[] starts = leaders.Order().ToArray();
        var blocks = new CilBlock[starts.Length];
        for (int blockIndex = 0; blockIndex < starts.Length; blockIndex++)
        {
            int start = starts[blockIndex];
            int end = blockIndex + 1 < starts.Length
                ? starts[blockIndex + 1]
                : method.Il.Length;
            DecodedInstruction[] blockInstructions = instructions
                .Where(instruction => instruction.Offset >= start && instruction.Offset < end)
                .ToArray();
            if (blockInstructions.Length == 0)
            {
                throw CilError(
                    "WRPCIL1012",
                    "A control-flow block does not begin at an instruction.",
                    start);
            }

            blocks[blockIndex] = new CilBlock(blockIndex, start, blockInstructions);
        }

        Dictionary<int, int> blockByOffset = blocks.ToDictionary(block => block.StartOffset, block => block.Id);
        foreach (CilBlock block in blocks)
        {
            DecodedInstruction last = block.Instructions[^1];
            if (IsUnconditionalBranch(last.OpCode))
            {
                block.SetSuccessors([blockByOffset[last.BranchTarget!.Value]]);
            }
            else if (IsConditionalBranch(last.OpCode))
            {
                if (last.NextOffset >= method.Il.Length || !blockByOffset.TryGetValue(last.NextOffset, out int fallthrough))
                {
                    throw CilError(
                        "WRPCIL1005",
                        "A conditional branch does not have a fallthrough instruction.",
                        last.Offset);
                }

                int branch = blockByOffset[last.BranchTarget!.Value];
                block.SetSuccessors(branch == fallthrough ? [branch] : [branch, fallthrough]);
            }
            else if (Is(last.OpCode, OpCodes.Ret))
            {
                block.SetSuccessors([]);
            }
            else if (last.NextOffset < method.Il.Length &&
                     blockByOffset.TryGetValue(last.NextOffset, out int fallthrough))
            {
                block.SetSuccessors([fallthrough]);
            }
            else
            {
                throw CilError(
                    "WRPCIL1005",
                    "The entry point does not end with ret.",
                    last.NextOffset);
            }

            for (int index = 0; index < block.Instructions.Count - 1; index++)
            {
                OpCode opCode = block.Instructions[index].OpCode;
                if (IsUnconditionalBranch(opCode) || IsConditionalBranch(opCode) || Is(opCode, OpCodes.Ret))
                {
                    throw CilError(
                        "WRPCIL1012",
                        "Control flow does not terminate its basic block.",
                        block.Instructions[index].Offset);
                }
            }
        }

        return blocks;
    }

    private static FlowShape[] AnalyzeFlow(
        WarpIntegerMapMethodBody method,
        IReadOnlyList<CilBlock> blocks)
    {
        var entries = new FlowShape?[blocks.Count];
        bool[] initialLocals = Enumerable
            .Repeat(method.LocalsInitialized, method.LocalCount)
            .ToArray();
        entries[0] = new FlowShape(0, initialLocals);

        var pending = new Queue<int>();
        var queued = new HashSet<int> { 0 };
        pending.Enqueue(0);

        while (pending.TryDequeue(out int blockId))
        {
            queued.Remove(blockId);
            FlowShape entry = entries[blockId]
                ?? throw new InvalidOperationException("A queued block does not have an entry state.");
            FlowShape exit = SimulateShape(method, blocks[blockId], entry);

            foreach (int successor in blocks[blockId].Successors)
            {
                if (MergeShape(entries, successor, exit, blocks[successor].StartOffset) &&
                    queued.Add(successor))
                {
                    pending.Enqueue(successor);
                }
            }
        }

        for (int blockId = 0; blockId < blocks.Count; blockId++)
        {
            if (entries[blockId] is null)
            {
                throw CilError(
                    "WRPCIL1004",
                    "Unreachable CIL is not permitted.",
                    blocks[blockId].StartOffset);
            }
        }

        return entries.Select(entry => entry!).ToArray();
    }

    private static FlowShape SimulateShape(
        WarpIntegerMapMethodBody method,
        CilBlock block,
        FlowShape entry)
    {
        int stackDepth = entry.StackDepth;
        bool[] assigned = (bool[])entry.AssignedLocals.Clone();

        foreach (DecodedInstruction instruction in block.Instructions)
        {
            OpCode opCode = instruction.OpCode;
            int offset = instruction.Offset;

            if (Is(opCode, OpCodes.Nop) || IsUnconditionalBranch(opCode))
            {
                continue;
            }

            if (TryGetArgumentIndex(instruction, out int argumentIndex))
            {
                ValidateArgument(method, argumentIndex, offset);
                stackDepth++;
            }
            else if (TryGetArgumentWriteIndex(instruction, out argumentIndex))
            {
                ValidateArgument(method, argumentIndex, offset);
                RequireStack(stackDepth, 1, offset);
                stackDepth--;
            }
            else if (TryGetConstant(instruction, out _))
            {
                stackDepth++;
            }
            else if (TryGetLocalReadIndex(instruction, out int localIndex))
            {
                ValidateLocal(method, localIndex, offset);
                if (!assigned[localIndex])
                {
                    throw CilError(
                        "WRPCIL1008",
                        "The CIL reads a local variable before definite assignment.",
                        offset);
                }

                stackDepth++;
            }
            else if (TryGetLocalWriteIndex(instruction, out localIndex))
            {
                ValidateLocal(method, localIndex, offset);
                RequireStack(stackDepth, 1, offset);
                stackDepth--;
                assigned[localIndex] = true;
            }
            else if (TryGetBinaryOperator(opCode, out _) ||
                     TryGetComparisonOperator(opCode, out _))
            {
                RequireStack(stackDepth, 2, offset);
                stackDepth--;
            }
            else if (Is(opCode, OpCodes.Call))
            {
                WarpCilCallTarget target = method.CallTargets[instruction.Operand];
                RequireStack(stackDepth, target.ParameterCount, offset);
                stackDepth = stackDepth - target.ParameterCount + 1;
            }
            else if (Is(opCode, OpCodes.Not) ||
                     Is(opCode, OpCodes.Conv_U4) ||
                     Is(opCode, OpCodes.Conv_I4))
            {
                RequireStack(stackDepth, 1, offset);
            }
            else if (Is(opCode, OpCodes.Dup))
            {
                RequireStack(stackDepth, 1, offset);
                stackDepth++;
            }
            else if (Is(opCode, OpCodes.Pop))
            {
                RequireStack(stackDepth, 1, offset);
                stackDepth--;
            }
            else if (IsConditionalBranch(opCode))
            {
                int consumed = IsBooleanBranch(opCode) ? 1 : 2;
                RequireStack(stackDepth, consumed, offset);
                stackDepth -= consumed;
            }
            else if (Is(opCode, OpCodes.Ret))
            {
                if (stackDepth != 1)
                {
                    throw CilError(
                        "WRPCIL1002",
                        "The evaluation stack must contain one result at ret.",
                        offset);
                }
            }
            else
            {
                throw new InvalidOperationException("An unsupported opcode reached flow analysis.");
            }

            if (stackDepth > method.MaxStack)
            {
                throw CilError(
                    "WRPCIL1002",
                    "The CIL evaluation stack exceeds maxstack.",
                    offset);
            }
        }

        return new FlowShape(stackDepth, assigned);
    }

    private static bool MergeShape(
        FlowShape?[] entries,
        int blockId,
        FlowShape incoming,
        int offset)
    {
        FlowShape? existing = entries[blockId];
        if (existing is null)
        {
            entries[blockId] = incoming.Clone();
            return true;
        }

        if (existing.StackDepth != incoming.StackDepth)
        {
            throw CilError(
                "WRPCIL1002",
                "Control-flow paths reach an instruction with different stack depths.",
                offset);
        }

        bool changed = false;
        bool[] assigned = (bool[])existing.AssignedLocals.Clone();
        for (int index = 0; index < assigned.Length; index++)
        {
            bool merged = assigned[index] && incoming.AssignedLocals[index];
            changed |= merged != assigned[index];
            assigned[index] = merged;
        }

        if (changed)
        {
            entries[blockId] = new FlowShape(existing.StackDepth, assigned);
        }

        return changed;
    }

    private static LoweredBody Lower(
        WarpIntegerMapMethodBody method,
        IReadOnlyList<CilBlock> blocks,
        IReadOnlyList<FlowShape> entryShapes,
        bool isEntry)
    {
        int nextValue = 0;
        var prologueInstructions = new List<WarpIrInstruction>();
        var initialArguments = new int[method.ParameterCount];
        for (int argumentIndex = 0; argumentIndex < method.ParameterCount; argumentIndex++)
        {
            int value = nextValue++;
            initialArguments[argumentIndex] = value;
            WarpIrOpCode load = isEntry
                ? argumentIndex < method.InputBufferCount
                    ? WarpIrOpCode.LoadInput
                    : WarpIrOpCode.LoadScalar
                : WarpIrOpCode.LoadArgument;
            int sourceIndex = isEntry && argumentIndex >= method.InputBufferCount
                ? argumentIndex - method.InputBufferCount
                : argumentIndex;
            prologueInstructions.Add(
                new WarpIrInstruction(
                    value,
                    load,
                    immediate: checked((uint)sourceIndex)));
        }

        int initialLocalValue = -1;
        if (method.LocalsInitialized && method.LocalCount != 0)
        {
            initialLocalValue = nextValue++;
            prologueInstructions.Add(
                new WarpIrInstruction(initialLocalValue, WarpIrOpCode.Constant));
        }

        var parameterStates = new BlockParameterState[blocks.Count];
        for (int blockId = 0; blockId < blocks.Count; blockId++)
        {
            FlowShape shape = entryShapes[blockId];
            var parameters = new List<WarpBlockParameter>();
            var arguments = new int[method.ParameterCount];
            var locals = new int?[method.LocalCount];

            for (int argumentIndex = 0; argumentIndex < arguments.Length; argumentIndex++)
            {
                int value = nextValue++;
                arguments[argumentIndex] = value;
                parameters.Add(new WarpBlockParameter(value));
            }

            for (int localIndex = 0; localIndex < method.LocalCount; localIndex++)
            {
                if (!shape.AssignedLocals[localIndex])
                {
                    continue;
                }

                int value = nextValue++;
                locals[localIndex] = value;
                parameters.Add(new WarpBlockParameter(value));
            }

            var stack = new int[shape.StackDepth];
            for (int stackIndex = 0; stackIndex < stack.Length; stackIndex++)
            {
                int value = nextValue++;
                stack[stackIndex] = value;
                parameters.Add(new WarpBlockParameter(value));
            }

            parameterStates[blockId] = new BlockParameterState(parameters, arguments, locals, stack);
        }

        var prologueLocals = new int?[method.LocalCount];
        if (initialLocalValue >= 0)
        {
            Array.Fill(prologueLocals, initialLocalValue);
        }

        var prologueState = new ValueState(initialArguments, prologueLocals, []);
        WarpBranchTarget prologueTarget = CreateTarget(
            0,
            prologueState,
            entryShapes,
            parameterStates);
        var loweredBlocks = new List<WarpBasicBlock>(blocks.Count + 1)
        {
            new(
                0,
                [],
                prologueInstructions,
                new WarpBranchTerminator(prologueTarget)),
        };

        for (int blockId = 0; blockId < blocks.Count; blockId++)
        {
            CilBlock block = blocks[blockId];
            BlockParameterState parameterState = parameterStates[blockId];
            var state = new ValueState(
                (int[])parameterState.Arguments.Clone(),
                (int?[])parameterState.Locals.Clone(),
                new List<int>(parameterState.Stack));
            var loweredInstructions = new List<WarpIrInstruction>();
            WarpBlockTerminator? terminator = null;

            foreach (DecodedInstruction instruction in block.Instructions)
            {
                OpCode opCode = instruction.OpCode;
                int offset = instruction.Offset;

                if (Is(opCode, OpCodes.Nop))
                {
                    continue;
                }

                if (TryGetArgumentIndex(instruction, out int argumentIndex))
                {
                    state.Stack.Add(state.Arguments[argumentIndex]);
                    continue;
                }

                if (TryGetArgumentWriteIndex(instruction, out argumentIndex))
                {
                    state.Arguments[argumentIndex] = Pop(state.Stack, offset);
                    continue;
                }

                if (TryGetConstant(instruction, out uint constant))
                {
                    int result = nextValue++;
                    loweredInstructions.Add(
                        new WarpIrInstruction(result, WarpIrOpCode.Constant, immediate: constant));
                    state.Stack.Add(result);
                    continue;
                }

                if (TryGetLocalReadIndex(instruction, out int localIndex))
                {
                    state.Stack.Add(
                        state.Locals[localIndex]
                        ?? throw CilError(
                            "WRPCIL1008",
                            "The CIL reads a local variable before definite assignment.",
                            offset));
                    continue;
                }

                if (TryGetLocalWriteIndex(instruction, out localIndex))
                {
                    state.Locals[localIndex] = Pop(state.Stack, offset);
                    continue;
                }

                if (TryGetBinaryOperator(opCode, out WarpIrOpCode binary))
                {
                    int right = Pop(state.Stack, offset);
                    int left = Pop(state.Stack, offset);
                    int result = nextValue++;
                    loweredInstructions.Add(new WarpIrInstruction(result, binary, left, right));
                    state.Stack.Add(result);
                    continue;
                }

                if (TryGetComparisonOperator(opCode, out WarpIrOpCode comparison))
                {
                    int right = Pop(state.Stack, offset);
                    int left = Pop(state.Stack, offset);
                    int result = nextValue++;
                    loweredInstructions.Add(new WarpIrInstruction(result, comparison, left, right));
                    state.Stack.Add(result);
                    continue;
                }

                if (Is(opCode, OpCodes.Call))
                {
                    WarpCilCallTarget target = method.CallTargets[instruction.Operand];
                    var arguments = new int[target.ParameterCount];
                    for (int argument = arguments.Length - 1; argument >= 0; argument--)
                    {
                        arguments[argument] = Pop(state.Stack, offset);
                    }

                    int result = nextValue++;
                    loweredInstructions.Add(
                        new WarpIrInstruction(
                            result,
                            WarpIrOpCode.Call,
                            callee: target.FunctionId,
                            arguments: arguments));
                    state.Stack.Add(result);
                    continue;
                }

                if (Is(opCode, OpCodes.Not))
                {
                    int operand = Pop(state.Stack, offset);
                    int result = nextValue++;
                    loweredInstructions.Add(
                        new WarpIrInstruction(result, WarpIrOpCode.BitwiseNot, left: operand));
                    state.Stack.Add(result);
                    continue;
                }

                if (Is(opCode, OpCodes.Conv_U4) || Is(opCode, OpCodes.Conv_I4))
                {
                    _ = Peek(state.Stack, offset);
                    continue;
                }

                if (Is(opCode, OpCodes.Dup))
                {
                    state.Stack.Add(Peek(state.Stack, offset));
                    continue;
                }

                if (Is(opCode, OpCodes.Pop))
                {
                    _ = Pop(state.Stack, offset);
                    continue;
                }

                if (IsUnconditionalBranch(opCode))
                {
                    int targetBlock = FindBlock(blocks, instruction.BranchTarget!.Value);
                    terminator = new WarpBranchTerminator(
                        CreateTarget(targetBlock, state, entryShapes, parameterStates));
                    break;
                }

                if (IsConditionalBranch(opCode))
                {
                    int targetBlock = FindBlock(blocks, instruction.BranchTarget!.Value);
                    int fallthroughBlock = FindBlock(blocks, instruction.NextOffset);
                    int condition;

                    if (IsBooleanBranch(opCode))
                    {
                        condition = Pop(state.Stack, offset);
                    }
                    else
                    {
                        int right = Pop(state.Stack, offset);
                        int left = Pop(state.Stack, offset);
                        if (targetBlock == fallthroughBlock)
                        {
                            terminator = new WarpBranchTerminator(
                                CreateTarget(targetBlock, state, entryShapes, parameterStates));
                            break;
                        }

                        condition = nextValue++;
                        loweredInstructions.Add(
                            new WarpIrInstruction(
                                condition,
                                GetBranchComparisonOperator(opCode),
                                left,
                                right));
                    }

                    if (targetBlock == fallthroughBlock)
                    {
                        terminator = new WarpBranchTerminator(
                            CreateTarget(targetBlock, state, entryShapes, parameterStates));
                        break;
                    }

                    WarpBranchTarget target = CreateTarget(
                        targetBlock,
                        state,
                        entryShapes,
                        parameterStates);
                    WarpBranchTarget fallthrough = CreateTarget(
                        fallthroughBlock,
                        state,
                        entryShapes,
                        parameterStates);
                    terminator = Is(opCode, OpCodes.Brfalse) || Is(opCode, OpCodes.Brfalse_S)
                        ? new WarpConditionalBranchTerminator(condition, fallthrough, target)
                        : new WarpConditionalBranchTerminator(condition, target, fallthrough);
                    break;
                }

                if (Is(opCode, OpCodes.Ret))
                {
                    terminator = new WarpReturnTerminator(Peek(state.Stack, offset));
                    break;
                }

                throw new InvalidOperationException("An unsupported opcode reached lowering.");
            }

            if (terminator is null)
            {
                int fallthroughBlock = FindBlock(blocks, block.Instructions[^1].NextOffset);
                terminator = new WarpBranchTerminator(
                    CreateTarget(fallthroughBlock, state, entryShapes, parameterStates));
            }

            loweredBlocks.Add(
                new WarpBasicBlock(
                    blockId + 1,
                    parameterState.Parameters,
                    loweredInstructions,
                    terminator));
        }

        return new LoweredBody(loweredBlocks);
    }

    private static WarpBranchTarget CreateTarget(
        int cilBlockId,
        ValueState state,
        IReadOnlyList<FlowShape> entryShapes,
        IReadOnlyList<BlockParameterState> parameterStates)
    {
        FlowShape shape = entryShapes[cilBlockId];
        BlockParameterState parameters = parameterStates[cilBlockId];
        var arguments = new List<int>(parameters.Parameters.Count);

        arguments.AddRange(state.Arguments);

        for (int localIndex = 0; localIndex < shape.AssignedLocals.Length; localIndex++)
        {
            if (!shape.AssignedLocals[localIndex])
            {
                continue;
            }

            arguments.Add(
                state.Locals[localIndex]
                ?? throw new InvalidOperationException("A definitely assigned local has no SSA value."));
        }

        if (state.Stack.Count != shape.StackDepth)
        {
            throw new InvalidOperationException("A branch stack does not match its analyzed target shape.");
        }

        arguments.AddRange(state.Stack);
        return new WarpBranchTarget(cilBlockId + 1, arguments);
    }

    private static int FindBlock(IReadOnlyList<CilBlock> blocks, int offset)
    {
        foreach (CilBlock block in blocks)
        {
            if (block.StartOffset == offset)
            {
                return block.Id;
            }
        }

        throw new InvalidOperationException("A validated branch target does not identify a block.");
    }

    private static int Pop(List<int> stack, int offset)
    {
        int value = Peek(stack, offset);
        stack.RemoveAt(stack.Count - 1);
        return value;
    }

    private static int Peek(IReadOnlyList<int> stack, int offset)
    {
        if (stack.Count == 0)
        {
            throw CilError("WRPCIL1002", "The CIL evaluation stack is empty.", offset);
        }

        return stack[^1];
    }

    private static void RequireStack(int stackDepth, int required, int offset)
    {
        if (stackDepth < required)
        {
            throw CilError("WRPCIL1002", "The CIL evaluation stack is empty.", offset);
        }
    }

    private static void ValidateArgument(
        WarpIntegerMapMethodBody method,
        int argumentIndex,
        int offset)
    {
        if ((uint)argumentIndex >= (uint)method.ParameterCount)
        {
            throw CilError("WRPCIL1006", "The CIL references an invalid argument index.", offset);
        }
    }

    private static void ValidateLocal(
        WarpIntegerMapMethodBody method,
        int localIndex,
        int offset)
    {
        if ((uint)localIndex >= (uint)method.LocalCount)
        {
            throw CilError("WRPCIL1007", "The CIL references an invalid local variable index.", offset);
        }
    }

    private static bool TryGetArgumentIndex(DecodedInstruction instruction, out int index)
    {
        if (TryGetFixedIndex(
                instruction.OpCode,
                OpCodes.Ldarg_0,
                OpCodes.Ldarg_1,
                OpCodes.Ldarg_2,
                OpCodes.Ldarg_3,
                out index))
        {
            return true;
        }

        if (Is(instruction.OpCode, OpCodes.Ldarg_S) || Is(instruction.OpCode, OpCodes.Ldarg))
        {
            index = instruction.Operand;
            return true;
        }

        index = 0;
        return false;
    }

    private static bool TryGetConstant(DecodedInstruction instruction, out uint value)
    {
        if (Is(instruction.OpCode, OpCodes.Ldc_I4_M1))
        {
            value = uint.MaxValue;
            return true;
        }

        OpCode[] smallConstants =
        [
            OpCodes.Ldc_I4_0,
            OpCodes.Ldc_I4_1,
            OpCodes.Ldc_I4_2,
            OpCodes.Ldc_I4_3,
            OpCodes.Ldc_I4_4,
            OpCodes.Ldc_I4_5,
            OpCodes.Ldc_I4_6,
            OpCodes.Ldc_I4_7,
            OpCodes.Ldc_I4_8,
        ];

        for (int index = 0; index < smallConstants.Length; index++)
        {
            if (Is(instruction.OpCode, smallConstants[index]))
            {
                value = (uint)index;
                return true;
            }
        }

        if (Is(instruction.OpCode, OpCodes.Ldc_I4_S) || Is(instruction.OpCode, OpCodes.Ldc_I4))
        {
            value = unchecked((uint)instruction.Operand);
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryGetArgumentWriteIndex(
        DecodedInstruction instruction,
        out int index)
    {
        if (Is(instruction.OpCode, OpCodes.Starg_S) || Is(instruction.OpCode, OpCodes.Starg))
        {
            index = instruction.Operand;
            return true;
        }

        index = 0;
        return false;
    }

    private static bool TryGetLocalReadIndex(
        DecodedInstruction instruction,
        out int index)
    {
        if (TryGetFixedIndex(
                instruction.OpCode,
                OpCodes.Ldloc_0,
                OpCodes.Ldloc_1,
                OpCodes.Ldloc_2,
                OpCodes.Ldloc_3,
                out index))
        {
            return true;
        }

        if (Is(instruction.OpCode, OpCodes.Ldloc_S) || Is(instruction.OpCode, OpCodes.Ldloc))
        {
            index = instruction.Operand;
            return true;
        }

        index = 0;
        return false;
    }

    private static bool TryGetLocalWriteIndex(
        DecodedInstruction instruction,
        out int index)
    {
        if (TryGetFixedIndex(
                instruction.OpCode,
                OpCodes.Stloc_0,
                OpCodes.Stloc_1,
                OpCodes.Stloc_2,
                OpCodes.Stloc_3,
                out index))
        {
            return true;
        }

        if (Is(instruction.OpCode, OpCodes.Stloc_S) || Is(instruction.OpCode, OpCodes.Stloc))
        {
            index = instruction.Operand;
            return true;
        }

        index = 0;
        return false;
    }

    private static bool TryGetFixedIndex(
        OpCode actual,
        OpCode zero,
        OpCode one,
        OpCode two,
        OpCode three,
        out int index)
    {
        if (Is(actual, zero))
        {
            index = 0;
            return true;
        }

        if (Is(actual, one))
        {
            index = 1;
            return true;
        }

        if (Is(actual, two))
        {
            index = 2;
            return true;
        }

        if (Is(actual, three))
        {
            index = 3;
            return true;
        }

        index = 0;
        return false;
    }

    private static bool TryGetBinaryOperator(OpCode opCode, out WarpIrOpCode result)
    {
        if (Is(opCode, OpCodes.Add))
        {
            result = WarpIrOpCode.Add;
            return true;
        }

        if (Is(opCode, OpCodes.Sub))
        {
            result = WarpIrOpCode.Subtract;
            return true;
        }

        if (Is(opCode, OpCodes.Mul))
        {
            result = WarpIrOpCode.Multiply;
            return true;
        }

        if (Is(opCode, OpCodes.And))
        {
            result = WarpIrOpCode.BitwiseAnd;
            return true;
        }

        if (Is(opCode, OpCodes.Or))
        {
            result = WarpIrOpCode.BitwiseOr;
            return true;
        }

        if (Is(opCode, OpCodes.Xor))
        {
            result = WarpIrOpCode.ExclusiveOr;
            return true;
        }

        if (Is(opCode, OpCodes.Shl))
        {
            result = WarpIrOpCode.ShiftLeft;
            return true;
        }

        if (Is(opCode, OpCodes.Shr_Un))
        {
            result = WarpIrOpCode.ShiftRightLogical;
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryGetComparisonOperator(OpCode opCode, out WarpIrOpCode result)
    {
        if (Is(opCode, OpCodes.Ceq))
        {
            result = WarpIrOpCode.Equal;
            return true;
        }

        if (Is(opCode, OpCodes.Clt_Un))
        {
            result = WarpIrOpCode.LessThanUnsigned;
            return true;
        }

        if (Is(opCode, OpCodes.Cgt_Un))
        {
            result = WarpIrOpCode.GreaterThanUnsigned;
            return true;
        }

        result = default;
        return false;
    }

    private static bool IsUnconditionalBranch(OpCode opCode) =>
        Is(opCode, OpCodes.Br) || Is(opCode, OpCodes.Br_S);

    private static bool IsBooleanBranch(OpCode opCode) =>
        Is(opCode, OpCodes.Brtrue) ||
        Is(opCode, OpCodes.Brtrue_S) ||
        Is(opCode, OpCodes.Brfalse) ||
        Is(opCode, OpCodes.Brfalse_S);

    private static bool IsConditionalBranch(OpCode opCode) =>
        IsBooleanBranch(opCode) ||
        Is(opCode, OpCodes.Beq) ||
        Is(opCode, OpCodes.Beq_S) ||
        Is(opCode, OpCodes.Bne_Un) ||
        Is(opCode, OpCodes.Bne_Un_S) ||
        Is(opCode, OpCodes.Blt_Un) ||
        Is(opCode, OpCodes.Blt_Un_S) ||
        Is(opCode, OpCodes.Ble_Un) ||
        Is(opCode, OpCodes.Ble_Un_S) ||
        Is(opCode, OpCodes.Bgt_Un) ||
        Is(opCode, OpCodes.Bgt_Un_S) ||
        Is(opCode, OpCodes.Bge_Un) ||
        Is(opCode, OpCodes.Bge_Un_S);

    private static WarpIrOpCode GetBranchComparisonOperator(OpCode opCode)
    {
        if (Is(opCode, OpCodes.Beq) || Is(opCode, OpCodes.Beq_S))
        {
            return WarpIrOpCode.Equal;
        }

        if (Is(opCode, OpCodes.Bne_Un) || Is(opCode, OpCodes.Bne_Un_S))
        {
            return WarpIrOpCode.NotEqual;
        }

        if (Is(opCode, OpCodes.Blt_Un) || Is(opCode, OpCodes.Blt_Un_S))
        {
            return WarpIrOpCode.LessThanUnsigned;
        }

        if (Is(opCode, OpCodes.Ble_Un) || Is(opCode, OpCodes.Ble_Un_S))
        {
            return WarpIrOpCode.LessThanOrEqualUnsigned;
        }

        if (Is(opCode, OpCodes.Bgt_Un) || Is(opCode, OpCodes.Bgt_Un_S))
        {
            return WarpIrOpCode.GreaterThanUnsigned;
        }

        if (Is(opCode, OpCodes.Bge_Un) || Is(opCode, OpCodes.Bge_Un_S))
        {
            return WarpIrOpCode.GreaterThanOrEqualUnsigned;
        }

        throw new ArgumentOutOfRangeException(nameof(opCode));
    }

    private static int ComputeBranchTarget(int baseOffset, int delta, int instructionOffset)
    {
        long target = (long)baseOffset + delta;
        if (target is < 0 or > int.MaxValue)
        {
            throw CilError(
                "WRPCIL1012",
                "The CIL branch target is outside the method body.",
                instructionOffset);
        }

        return (int)target;
    }

    private static void ValidateBranchTarget(
        int target,
        int branchOffset,
        IReadOnlyDictionary<int, DecodedInstruction> instructionsByOffset)
    {
        if (!instructionsByOffset.ContainsKey(target))
        {
            throw CilError(
                "WRPCIL1012",
                "The CIL branch target is not an instruction boundary.",
                branchOffset);
        }
    }

    private static OpCode ReadOpCode(ref IlReader reader, int offset)
    {
        byte first = reader.ReadByte();
        short value = first == 0xFE
            ? unchecked((short)(0xFE00 | reader.ReadByte()))
            : first;

        return OpCodesByValue.TryGetValue(value, out OpCode opCode)
            ? opCode
            : throw CilError("WRPCIL1010", "The CIL contains an unknown opcode.", offset);
    }

    private static IReadOnlyDictionary<short, OpCode> CreateOpCodeMap()
    {
        return typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);
    }

    private static bool Is(OpCode left, OpCode right) => left.Value == right.Value;

    private static WarpVerificationException CilError(string code, string message, int offset)
    {
        return new WarpVerificationException(code, $"{message} IL offset: 0x{offset:X4}.", offset);
    }

    private sealed record DecodedInstruction(
        int Offset,
        int NextOffset,
        OpCode OpCode,
        int Operand,
        int? BranchTarget,
        IReadOnlyList<int>? SwitchTargets);

    private sealed record LoweredBody(IReadOnlyList<WarpBasicBlock> Blocks);

    private sealed class CilBlock
    {
        public CilBlock(
            int id,
            int startOffset,
            IReadOnlyList<DecodedInstruction> instructions)
        {
            Id = id;
            StartOffset = startOffset;
            Instructions = instructions;
        }

        public int Id { get; }

        public int StartOffset { get; }

        public IReadOnlyList<DecodedInstruction> Instructions { get; }

        public IReadOnlyList<int> Successors { get; private set; } = [];

        public void SetSuccessors(IReadOnlyList<int> successors) => Successors = successors;
    }

    private sealed class FlowShape
    {
        public FlowShape(int stackDepth, bool[] assignedLocals)
        {
            StackDepth = stackDepth;
            AssignedLocals = assignedLocals;
        }

        public int StackDepth { get; }

        public bool[] AssignedLocals { get; }

        public FlowShape Clone() => new(StackDepth, (bool[])AssignedLocals.Clone());
    }

    private sealed class BlockParameterState
    {
        public BlockParameterState(
            IReadOnlyList<WarpBlockParameter> parameters,
            int[] arguments,
            int?[] locals,
            int[] stack)
        {
            Parameters = parameters;
            Arguments = arguments;
            Locals = locals;
            Stack = stack;
        }

        public IReadOnlyList<WarpBlockParameter> Parameters { get; }

        public int[] Arguments { get; }

        public int?[] Locals { get; }

        public int[] Stack { get; }
    }

    private sealed class ValueState
    {
        public ValueState(int[] arguments, int?[] locals, List<int> stack)
        {
            Arguments = arguments;
            Locals = locals;
            Stack = stack;
        }

        public int[] Arguments { get; }

        public int?[] Locals { get; }

        public List<int> Stack { get; }
    }

    private ref struct IlReader
    {
        private readonly ReadOnlySpan<byte> bytes;

        public IlReader(ReadOnlySpan<byte> bytes)
        {
            this.bytes = bytes;
            Offset = 0;
        }

        public int Offset { get; private set; }

        public bool IsComplete => Offset == bytes.Length;

        public int Remaining => bytes.Length - Offset;

        public byte ReadByte()
        {
            if ((uint)Offset >= (uint)bytes.Length)
            {
                throw CilError("WRPCIL1011", "The CIL operand is incomplete.", Offset);
            }

            return bytes[Offset++];
        }

        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

        public ushort ReadUInt16()
        {
            uint low = ReadByte();
            uint high = ReadByte();
            return (ushort)(low | (high << 8));
        }

        public int ReadInt32()
        {
            uint value = ReadByte();
            value |= (uint)ReadByte() << 8;
            value |= (uint)ReadByte() << 16;
            value |= (uint)ReadByte() << 24;
            return unchecked((int)value);
        }
    }
}
