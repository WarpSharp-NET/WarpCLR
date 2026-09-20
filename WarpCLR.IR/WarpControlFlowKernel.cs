using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public enum WarpIrValueType
{
    UInt32,
}

public enum WarpIrOpCode
{
    LoadInput,
    LoadScalar,
    LoadArgument,
    Constant,
    BitwiseNot,
    Add,
    Subtract,
    Multiply,
    BitwiseAnd,
    BitwiseOr,
    ExclusiveOr,
    ShiftLeft,
    ShiftRightLogical,
    Equal,
    NotEqual,
    LessThanUnsigned,
    LessThanOrEqualUnsigned,
    GreaterThanUnsigned,
    GreaterThanOrEqualUnsigned,
    Select,
    Call,
}

public enum WarpControlFlowOperation
{
    BlockArguments,
    Branch,
    ConditionalBranch,
    Return,
}

public readonly record struct WarpBlockParameter(
    int Value,
    WarpIrValueType Type = WarpIrValueType.UInt32);

public readonly record struct WarpIrInstruction
{
    private readonly ReadOnlyCollection<int>? arguments;

    public WarpIrInstruction(
        int result,
        WarpIrOpCode opCode,
        int left = -1,
        int right = -1,
        uint immediate = 0,
        int third = -1,
        WarpIrValueType resultType = WarpIrValueType.UInt32,
        int callee = -1,
        IEnumerable<int>? arguments = null)
    {
        Result = result;
        ResultType = resultType;
        OpCode = opCode;
        Left = left;
        Right = right;
        Immediate = immediate;
        Third = third;
        Callee = callee;
        this.arguments = Array.AsReadOnly(arguments?.ToArray() ?? []);
    }

    public int Result { get; }

    public WarpIrValueType ResultType { get; }

    public WarpIrOpCode OpCode { get; }

    public int Left { get; }

    public int Right { get; }

    public uint Immediate { get; }

    public int Third { get; }

    public int Callee { get; }

    public IReadOnlyList<int> Arguments => arguments is null
        ? Array.Empty<int>()
        : arguments;
}

public sealed class WarpBranchTarget
{
    private readonly ReadOnlyCollection<int> arguments;

    public WarpBranchTarget(int block, IEnumerable<int> arguments)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(block);
        ArgumentNullException.ThrowIfNull(arguments);

        Block = block;
        this.arguments = Array.AsReadOnly(arguments.ToArray());
    }

    public int Block { get; }

    public IReadOnlyList<int> Arguments => arguments;
}

public abstract class WarpBlockTerminator
{
    private protected WarpBlockTerminator()
    {
    }
}

public sealed class WarpBranchTerminator : WarpBlockTerminator
{
    public WarpBranchTerminator(WarpBranchTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
    }

    public WarpBranchTarget Target { get; }
}

public sealed class WarpConditionalBranchTerminator : WarpBlockTerminator
{
    public WarpConditionalBranchTerminator(
        int condition,
        WarpBranchTarget whenNonZero,
        WarpBranchTarget whenZero)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(condition);
        ArgumentNullException.ThrowIfNull(whenNonZero);
        ArgumentNullException.ThrowIfNull(whenZero);

        Condition = condition;
        WhenNonZero = whenNonZero;
        WhenZero = whenZero;
    }

    public int Condition { get; }

    public WarpBranchTarget WhenNonZero { get; }

    public WarpBranchTarget WhenZero { get; }
}

public sealed class WarpReturnTerminator : WarpBlockTerminator
{
    public WarpReturnTerminator(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    public int Value { get; }
}

public sealed class WarpBasicBlock
{
    public WarpBasicBlock(
        int id,
        IEnumerable<WarpBlockParameter> parameters,
        IEnumerable<WarpIrInstruction> instructions,
        WarpBlockTerminator terminator)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(terminator);

        Id = id;
        Parameters = Array.AsReadOnly(parameters.ToArray());
        Instructions = Array.AsReadOnly(instructions.ToArray());
        Terminator = terminator;
    }

    public int Id { get; }

    public ReadOnlyCollection<WarpBlockParameter> Parameters { get; }

    public ReadOnlyCollection<WarpIrInstruction> Instructions { get; }

    public WarpBlockTerminator Terminator { get; }
}

public sealed class WarpControlFlowFunction
{
    public WarpControlFlowFunction(
        int id,
        string name,
        int parameterCount,
        IEnumerable<WarpBasicBlock> blocks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(parameterCount);
        ArgumentNullException.ThrowIfNull(blocks);

        WarpBasicBlock[] blockArray = blocks.ToArray();
        WarpControlFlowKernel.ValidateBodyShape(blockArray, nameof(blocks));
        Dictionary<int, WarpIrValueType> valueTypes =
            WarpControlFlowKernel.CollectBodyDefinitions(blockArray);
        WarpControlFlowKernel.ValidateBodyValues(valueTypes);

        Id = id;
        Name = name;
        ParameterCount = parameterCount;
        Blocks = Array.AsReadOnly(blockArray);
        Instructions = Array.AsReadOnly(blockArray.SelectMany(block => block.Instructions).ToArray());
        ValueCount = valueTypes.Count;
    }

    public int Id { get; }

    public string Name { get; }

    public int ParameterCount { get; }

    public ReadOnlyCollection<WarpBasicBlock> Blocks { get; }

    public ReadOnlyCollection<WarpIrInstruction> Instructions { get; }

    public int ValueCount { get; }
}

public sealed class WarpControlFlowKernel
{
    public WarpControlFlowKernel(
        string name,
        int inputBufferCount,
        int scalarArgumentCount,
        IEnumerable<WarpBasicBlock> blocks,
        WarpReductionOperation? reduction = null,
        IEnumerable<WarpControlFlowFunction>? functions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputBufferCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(scalarArgumentCount);
        ArgumentNullException.ThrowIfNull(blocks);
        if (reduction.HasValue && !Enum.IsDefined(reduction.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(reduction));
        }

        WarpBasicBlock[] blockArray = blocks.ToArray();
        ValidateBodyShape(blockArray, nameof(blocks));

        WarpControlFlowFunction[] functionArray = functions?.ToArray() ?? [];
        for (int index = 0; index < functionArray.Length; index++)
        {
            if (functionArray[index].Id != index)
            {
                throw new ArgumentException(
                    "Control-flow function identifiers must be sequential and start at zero.",
                    nameof(functions));
            }
        }

        if (functionArray.Select(function => function.Name).Distinct(StringComparer.Ordinal).Count() !=
            functionArray.Length)
        {
            throw new ArgumentException("Control-flow function names must be unique.", nameof(functions));
        }

        Dictionary<int, WarpIrValueType> valueTypes = CollectBodyDefinitions(blockArray);
        ValidateBodyValues(valueTypes);
        ValidateBlocks(
            blockArray,
            valueTypes,
            inputBufferCount,
            scalarArgumentCount,
            argumentCount: 0,
            functionArray,
            isEntry: true);
        ValidateReachability(blockArray);

        foreach (WarpControlFlowFunction function in functionArray)
        {
            Dictionary<int, WarpIrValueType> functionValueTypes =
                CollectBodyDefinitions(function.Blocks);
            ValidateBlocks(
                function.Blocks,
                functionValueTypes,
                inputBufferCount: 0,
                scalarArgumentCount: 0,
                function.ParameterCount,
                functionArray,
                isEntry: false);
            ValidateReachability(function.Blocks);
        }

        ValidateAcyclicCallGraph(blockArray, functionArray);

        Name = name;
        InputBufferCount = inputBufferCount;
        ScalarArgumentCount = scalarArgumentCount;
        Blocks = Array.AsReadOnly(blockArray);
        Instructions = Array.AsReadOnly(blockArray.SelectMany(block => block.Instructions).ToArray());
        ValueCount = valueTypes.Count;
        Reduction = reduction;
        Functions = Array.AsReadOnly(functionArray);
    }

    public string Name { get; }

    public int InputBufferCount { get; }

    public int ScalarArgumentCount { get; }

    public ReadOnlyCollection<WarpBasicBlock> Blocks { get; }

    public ReadOnlyCollection<WarpIrInstruction> Instructions { get; }

    public int ValueCount { get; }

    public WarpReductionOperation? Reduction { get; }

    public ReadOnlyCollection<WarpControlFlowFunction> Functions { get; }

    internal static void ValidateBodyShape(
        IReadOnlyList<WarpBasicBlock> blocks,
        string parameterName)
    {
        if (blocks.Count == 0)
        {
            throw new ArgumentException("A control-flow body requires an entry block.", parameterName);
        }

        for (int index = 0; index < blocks.Count; index++)
        {
            if (blocks[index].Id != index)
            {
                throw new ArgumentException(
                    "Control-flow block identifiers must be sequential and entry must be block zero.",
                    parameterName);
            }
        }

        if (blocks[0].Parameters.Count != 0)
        {
            throw new ArgumentException("A control-flow entry block cannot declare block parameters.", parameterName);
        }
    }

    internal static Dictionary<int, WarpIrValueType> CollectBodyDefinitions(
        IReadOnlyList<WarpBasicBlock> blocks)
    {
        var result = new Dictionary<int, WarpIrValueType>();
        foreach (WarpBasicBlock block in blocks)
        {
            foreach (WarpBlockParameter parameter in block.Parameters)
            {
                AddDefinition(result, parameter.Value, parameter.Type);
            }

            foreach (WarpIrInstruction instruction in block.Instructions)
            {
                AddDefinition(result, instruction.Result, instruction.ResultType);
            }
        }

        return result;
    }

    private static void AddDefinition(
        IDictionary<int, WarpIrValueType> definitions,
        int value,
        WarpIrValueType type)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        if (!definitions.TryAdd(value, type))
        {
            throw new ArgumentException($"SSA value {value} has more than one definition.");
        }
    }

    internal static void ValidateBodyValues(
        IReadOnlyDictionary<int, WarpIrValueType> valueTypes)
    {
        for (int value = 0; value < valueTypes.Count; value++)
        {
            if (!valueTypes.ContainsKey(value))
            {
                throw new ArgumentException("SSA value identifiers must be contiguous.");
            }
        }
    }

    private static void ValidateBlocks(
        IReadOnlyList<WarpBasicBlock> blocks,
        IReadOnlyDictionary<int, WarpIrValueType> valueTypes,
        int inputBufferCount,
        int scalarArgumentCount,
        int argumentCount,
        IReadOnlyList<WarpControlFlowFunction> functions,
        bool isEntry)
    {
        bool hasReturn = false;
        foreach (WarpBasicBlock block in blocks)
        {
            var available = new HashSet<int>();
            foreach (WarpBlockParameter parameter in block.Parameters)
            {
                available.Add(parameter.Value);
            }

            foreach (WarpIrInstruction instruction in block.Instructions)
            {
                ValidateInstruction(
                    instruction,
                    available,
                    inputBufferCount,
                    scalarArgumentCount,
                    argumentCount,
                    functions,
                    isEntry);
                available.Add(instruction.Result);
            }

            switch (block.Terminator)
            {
                case WarpBranchTerminator branch:
                    ValidateTarget(branch.Target, blocks, valueTypes, available);
                    break;

                case WarpConditionalBranchTerminator conditional:
                    RequireAvailable(conditional.Condition, available);
                    if (conditional.WhenNonZero.Block == conditional.WhenZero.Block)
                    {
                        throw new ArgumentException(
                            "A conditional branch must have distinct successor blocks.");
                    }

                    ValidateTarget(conditional.WhenNonZero, blocks, valueTypes, available);
                    ValidateTarget(conditional.WhenZero, blocks, valueTypes, available);
                    break;

                case WarpReturnTerminator @return:
                    RequireAvailable(@return.Value, available);
                    hasReturn = true;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(blocks),
                        block.Terminator,
                        "The block terminator is not registered.");
            }
        }

        if (!hasReturn)
        {
            throw new ArgumentException("A control-flow kernel requires a return terminator.", nameof(blocks));
        }
    }

    private static void ValidateInstruction(
        WarpIrInstruction instruction,
        IReadOnlySet<int> available,
        int inputBufferCount,
        int scalarArgumentCount,
        int argumentCount,
        IReadOnlyList<WarpControlFlowFunction> functions,
        bool isEntry)
    {
        if (instruction.ResultType != WarpIrValueType.UInt32)
        {
            throw new ArgumentException("The current profile only defines UInt32 SSA values.");
        }

        if (instruction.OpCode != WarpIrOpCode.Call)
        {
            RequireNoCallMetadata(instruction);
        }

        switch (instruction.OpCode)
        {
            case WarpIrOpCode.LoadInput:
                RequireNoOperands(instruction);
                RequireEntrySource(instruction, isEntry);
                if (instruction.Immediate >= (uint)inputBufferCount)
                {
                    throw new ArgumentException("An instruction references an invalid input buffer.");
                }

                break;

            case WarpIrOpCode.LoadScalar:
                RequireNoOperands(instruction);
                RequireEntrySource(instruction, isEntry);
                if (instruction.Immediate >= (uint)scalarArgumentCount)
                {
                    throw new ArgumentException("An instruction references an invalid scalar argument.");
                }

                break;

            case WarpIrOpCode.LoadArgument:
                RequireNoOperands(instruction);
                if (isEntry || instruction.Immediate >= (uint)argumentCount)
                {
                    throw new ArgumentException("An instruction references an invalid function argument.");
                }

                break;

            case WarpIrOpCode.Constant:
                RequireNoOperands(instruction);
                break;

            case WarpIrOpCode.BitwiseNot:
                RequireAvailable(instruction.Left, available);
                if (instruction.Right != -1 ||
                    instruction.Third != -1 ||
                    instruction.Immediate != 0)
                {
                    throw new ArgumentException("A unary instruction has an unexpected operand.");
                }

                break;

            case WarpIrOpCode.Add:
            case WarpIrOpCode.Subtract:
            case WarpIrOpCode.Multiply:
            case WarpIrOpCode.BitwiseAnd:
            case WarpIrOpCode.BitwiseOr:
            case WarpIrOpCode.ExclusiveOr:
            case WarpIrOpCode.ShiftLeft:
            case WarpIrOpCode.ShiftRightLogical:
            case WarpIrOpCode.Equal:
            case WarpIrOpCode.NotEqual:
            case WarpIrOpCode.LessThanUnsigned:
            case WarpIrOpCode.LessThanOrEqualUnsigned:
            case WarpIrOpCode.GreaterThanUnsigned:
            case WarpIrOpCode.GreaterThanOrEqualUnsigned:
                RequireAvailable(instruction.Left, available);
                RequireAvailable(instruction.Right, available);
                if (instruction.Third != -1 || instruction.Immediate != 0)
                {
                    throw new ArgumentException("A binary instruction has an unexpected third operand.");
                }

                break;

            case WarpIrOpCode.Select:
                RequireAvailable(instruction.Left, available);
                RequireAvailable(instruction.Right, available);
                RequireAvailable(instruction.Third, available);
                if (instruction.Immediate != 0)
                {
                    throw new ArgumentException("A select instruction has an unexpected immediate.");
                }

                break;

            case WarpIrOpCode.Call:
                if (instruction.Left != -1 ||
                    instruction.Right != -1 ||
                    instruction.Third != -1 ||
                    instruction.Immediate != 0)
                {
                    throw new ArgumentException("A call instruction has an unexpected fixed operand.");
                }

                if ((uint)instruction.Callee >= (uint)functions.Count)
                {
                    throw new ArgumentException("A call instruction references an invalid function.");
                }

                WarpControlFlowFunction callee = functions[instruction.Callee];
                if (instruction.Arguments.Count != callee.ParameterCount)
                {
                    throw new ArgumentException("A call argument count does not match its function signature.");
                }

                foreach (int argument in instruction.Arguments)
                {
                    RequireAvailable(argument, available);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(instruction),
                    instruction.OpCode,
                    "The instruction opcode is not registered.");
        }
    }

    private static void ValidateTarget(
        WarpBranchTarget target,
        IReadOnlyList<WarpBasicBlock> blocks,
        IReadOnlyDictionary<int, WarpIrValueType> valueTypes,
        IReadOnlySet<int> available)
    {
        if ((uint)target.Block >= (uint)blocks.Count)
        {
            throw new ArgumentException("A branch references an invalid block.");
        }

        WarpBasicBlock destination = blocks[target.Block];
        if (target.Arguments.Count != destination.Parameters.Count)
        {
            throw new ArgumentException("A branch argument count does not match its target block.");
        }

        for (int index = 0; index < target.Arguments.Count; index++)
        {
            int argument = target.Arguments[index];
            RequireAvailable(argument, available);
            if (valueTypes[argument] != destination.Parameters[index].Type)
            {
                throw new ArgumentException("A branch argument type does not match its target parameter.");
            }
        }
    }

    private static void ValidateReachability(IReadOnlyList<WarpBasicBlock> blocks)
    {
        var reachable = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(0);

        while (pending.TryPop(out int blockId))
        {
            if (!reachable.Add(blockId))
            {
                continue;
            }

            switch (blocks[blockId].Terminator)
            {
                case WarpBranchTerminator branch:
                    pending.Push(branch.Target.Block);
                    break;

                case WarpConditionalBranchTerminator conditional:
                    pending.Push(conditional.WhenNonZero.Block);
                    pending.Push(conditional.WhenZero.Block);
                    break;
            }
        }

        if (reachable.Count != blocks.Count)
        {
            int unreachable = Enumerable.Range(0, blocks.Count).First(id => !reachable.Contains(id));
            throw new ArgumentException($"Control-flow block {unreachable} is unreachable.", nameof(blocks));
        }
    }

    private static void RequireNoOperands(WarpIrInstruction instruction)
    {
        if (instruction.Left != -1 || instruction.Right != -1 || instruction.Third != -1)
        {
            throw new ArgumentException("A load or constant instruction has unexpected operands.");
        }

        RequireNoCallMetadata(instruction);
    }

    private static void RequireNoCallMetadata(WarpIrInstruction instruction)
    {
        if (instruction.Callee != -1 || instruction.Arguments.Count != 0)
        {
            throw new ArgumentException("A non-call instruction has unexpected call metadata.");
        }
    }

    private static void RequireEntrySource(WarpIrInstruction instruction, bool isEntry)
    {
        if (!isEntry)
        {
            throw new ArgumentException(
                $"Function bodies cannot contain '{instruction.OpCode}' instructions.");
        }
    }

    private static void ValidateAcyclicCallGraph(
        IReadOnlyList<WarpBasicBlock> entryBlocks,
        IReadOnlyList<WarpControlFlowFunction> functions)
    {
        var states = new byte[functions.Count];

        foreach (int callee in GetCallees(entryBlocks))
        {
            Visit(callee);
        }

        if (states.Any(state => state == 0))
        {
            throw new ArgumentException(
                "Every control-flow function must be reachable from the kernel entry point.");
        }

        void Visit(int function)
        {
            if (states[function] == 2)
            {
                return;
            }

            if (states[function] == 1)
            {
                throw new ArgumentException(
                    "Recursive call graphs require the portable logical stack and are not in this profile.");
            }

            states[function] = 1;
            foreach (int callee in GetCallees(functions[function].Blocks))
            {
                Visit(callee);
            }

            states[function] = 2;
        }
    }

    private static IEnumerable<int> GetCallees(IEnumerable<WarpBasicBlock> blocks) =>
        blocks.SelectMany(block => block.Instructions)
            .Where(instruction => instruction.OpCode == WarpIrOpCode.Call)
            .Select(instruction => instruction.Callee);

    private static void RequireAvailable(int value, IReadOnlySet<int> available)
    {
        if (value < 0 || !available.Contains(value))
        {
            throw new ArgumentException(
                "An SSA operand must be a block parameter or a prior result in the same block.");
        }
    }
}
