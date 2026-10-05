using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

public sealed class WarpControlFlowKernel
{
    public WarpControlFlowKernel(
        string name,
        int inputBufferCount,
        int scalarArgumentCount,
        IEnumerable<WarpBasicBlock> blocks,
        WarpReductionOperation? reduction = null,
        IEnumerable<WarpControlFlowFunction>? functions = null)
        : this(name, inputBufferCount, scalarArgumentCount, blocks, reduction, functions, execution: null)
    {
    }

    internal WarpControlFlowKernel(string name, int inputBufferCount, int scalarArgumentCount,
        IEnumerable<WarpBasicBlock> blocks, WarpReductionOperation? reduction,
        IEnumerable<WarpControlFlowFunction>? functions, WarpLogicalExecutionMetadata? execution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        WarpCompilationAdmission.Require("<IR-entry>", WarpCompilationResourceKind.IdentityCharacters, name.Length, WarpCompilationAdmission.MaximumIdentityCharacters);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputBufferCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(scalarArgumentCount);
        ArgumentNullException.ThrowIfNull(blocks);
        if (reduction.HasValue && !Enum.IsDefined(reduction.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(reduction));
        }

        WarpBasicBlock[] blockArray = WarpCompilationAdmission.Materialize(blocks, name, WarpCompilationResourceKind.Blocks, WarpCompilationAdmission.MaximumBlocksPerEntry);
        ValidateBodyShape(blockArray, nameof(blocks));

        WarpControlFlowFunction[] functionArray = functions is null ? [] : WarpCompilationAdmission.Materialize(functions, name,
            WarpCompilationResourceKind.Functions, WarpCompilationAdmission.MaximumFunctionsPerEntry);
        ValidateFunctionTable(functionArray);
        WarpCompilationAdmission.Require(name, WarpCompilationResourceKind.Parameters,
            inputBufferCount + (long)scalarArgumentCount, WarpCompilationAdmission.MaximumParametersPerBody);
        WarpCompilationAdmission.ValidateDefinitions(name, Array.AsReadOnly(blockArray), functionArray);

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
        ValidateStateDestinations(blockArray, functionArray, execution);
        ValidateReachability(blockArray, GetStateEntryBlocks(blockArray, functionArray, 0));

        if (reduction.HasValue && blockArray.Any(block => block.Terminator is WarpTupleReturnTerminator))
        {
            throw new ArgumentException("A wide result requires a reduction contract with a matching accumulator width.", nameof(reduction));
        }

        ValidateFunctionBodies(blockArray, functionArray);
        HelperExpansionFactor = execution?.Validate(blockArray, functionArray) ?? 1;
        ValidateRuntimeCapabilities(blockArray, functionArray, execution);
        ValidateAcyclicCallGraph(blockArray, functionArray, execution?.RecursiveCalls == true);
        Execution = execution;

        Name = name;
        InputBufferCount = inputBufferCount;
        ScalarArgumentCount = scalarArgumentCount;
        Blocks = Array.AsReadOnly(blockArray);
        Instructions = Array.AsReadOnly(blockArray.SelectMany(block => block.Instructions).ToArray());
        ValueCount = valueTypes.Count;
        Reduction = reduction;
        Functions = Array.AsReadOnly(functionArray);
    }

    internal WarpLogicalExecutionMetadata? Execution { get; }

    internal int HelperExpansionFactor { get; }

    private static void ValidateRuntimeCapabilities(WarpBasicBlock[] blocks, WarpControlFlowFunction[] functions, WarpLogicalExecutionMetadata? execution)
    {
        WarpBasicBlock[] bodies = blocks.Concat(functions.SelectMany(function => function.Blocks)).ToArray();
        if (execution?.ManagedExceptionTermination != true && bodies.Any(block => block.Terminator is WarpManagedExceptionTerminator))
        {
            throw new ArgumentException("Managed exception termination requires its immutable execution admission.", nameof(execution));
        }
        if (execution is null && bodies.SelectMany(block => block.Instructions).Any(instruction => WarpManagedFrameOpCode.IsPrivate(instruction.OpCode) ||
            WarpManagedFrameOpCode.IsOwner(instruction.OpCode) || WarpManagedStateOpCode.IsState(instruction.OpCode)))
        {
            throw new ArgumentException("Private storage requires its admitted logical-frame metadata.", nameof(execution));
        }
    }

    public string Name { get; }

    public int InputBufferCount { get; }

    public int ScalarArgumentCount { get; }

    public ReadOnlyCollection<WarpBasicBlock> Blocks { get; }

    public ReadOnlyCollection<WarpIrInstruction> Instructions { get; }

    public int ValueCount { get; }

    public WarpReductionOperation? Reduction { get; }

    public ReadOnlyCollection<WarpControlFlowFunction> Functions { get; }

    private static void ValidateFunctionTable(WarpControlFlowFunction[] functions)
    {
        for (int index = 0; index < functions.Length; index++)
        {
            if (functions[index].Id != index)
            {
                throw new ArgumentException(
                    "Control-flow function identifiers must be sequential and start at zero.",
                    nameof(functions));
            }
        }

        if (functions.Select(function => function.Name).Distinct(StringComparer.Ordinal).Count() !=
            functions.Length)
        {
            throw new ArgumentException("Control-flow function names must be unique.", nameof(functions));
        }
    }

    private static void ValidateFunctionBodies(IReadOnlyList<WarpBasicBlock> entry, WarpControlFlowFunction[] functions)
    {
        foreach (WarpControlFlowFunction function in functions)
        {
            Dictionary<int, WarpIrValueType> valueTypes = CollectBodyDefinitions(function.Blocks);
            ValidateBlocks(
                function.Blocks,
                valueTypes,
                inputBufferCount: 0,
                scalarArgumentCount: 0,
                function.ParameterCount,
                functions,
                isEntry: false);
            ValidateReachability(function.Blocks, GetStateEntryBlocks(entry, functions, function.Id + 1));
        }
    }

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
                for (int word = 0; word < instruction.ResultWordCount; word++)
                {
                    AddDefinition(result, checked(instruction.Result + word), instruction.ResultType);
                }
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
            throw new ArgumentException($"SSA value {value} has more than one definition.", nameof(value));
        }
    }

    internal static void ValidateBodyValues(
        IReadOnlyDictionary<int, WarpIrValueType> valueTypes)
    {
        for (int value = 0; value < valueTypes.Count; value++)
        {
            if (!valueTypes.ContainsKey(value))
            {
                throw new ArgumentException("SSA value identifiers must be contiguous.", nameof(valueTypes));
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
        int returnWords = -1;
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
                for (int word = 0; word < instruction.ResultWordCount; word++)
                {
                    available.Add(checked(instruction.Result + word));
                }
            }

            RequireReturnWidth(ref returnWords, ValidateTerminator(block.Terminator, blocks, valueTypes, available, isEntry));
        }

        if (returnWords < 0)
        {
            throw new ArgumentException("A control-flow kernel requires a return terminator.", nameof(blocks));
        }
    }

    private static int ValidateTerminator(WarpBlockTerminator terminator, IReadOnlyList<WarpBasicBlock> blocks,
        IReadOnlyDictionary<int, WarpIrValueType> valueTypes, HashSet<int> available, bool isEntry)
    {
        switch (terminator)
        {
            case WarpBranchTerminator branch:
                ValidateTarget(branch.Target, blocks, valueTypes, available);
                return -1;
            case WarpConditionalBranchTerminator conditional:
                RequireAvailable(conditional.Condition, available);
                if (conditional.WhenNonZero.Block == conditional.WhenZero.Block)
                {
                    throw new ArgumentException("A conditional branch must have distinct successor blocks.", nameof(blocks));
                }

                ValidateTarget(conditional.WhenNonZero, blocks, valueTypes, available);
                ValidateTarget(conditional.WhenZero, blocks, valueTypes, available);
                return -1;
            case WarpStateDispatchTerminator dispatch:
                return dispatch.ResultWordCount;
            case WarpManagedExceptionTerminator managed:
                RequireAvailable(managed.Context, available);
                RequireAvailable(managed.ObjectId, available);
                RequireAvailable(managed.Generation, available);
                return managed.ResultWordCount;
            case WarpReturnTerminator single:
                RequireAvailable(single.Value, available);
                return 1;
            case WarpTupleReturnTerminator tuple:
                foreach (int value in tuple.Values)
                {
                    RequireAvailable(value, available);
                }

                return tuple.Values.Count;
            default:
                throw new ArgumentOutOfRangeException(nameof(terminator), terminator, "The block terminator is not registered.");
        }
    }

    private static void RequireReturnWidth(ref int actual, int expected)
    {
        if (expected < 0)
        {
            return;
        }

        if (actual >= 0 && actual != expected)
        {
            throw new ArgumentException("Every return in a body must preserve the same result width.", nameof(expected));
        }

        actual = expected;
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
            throw new ArgumentException("The current profile only defines UInt32 SSA values.", nameof(instruction));
        }

        if (instruction.OpCode != WarpIrOpCode.Call)
        {
            RequireNoCallMetadata(instruction);
            if (instruction.ResultWordCount != 1)
            {
                throw new ArgumentException("Only a call can define a result tuple.", nameof(instruction));
            }
        }

        if (instruction.OpCode is WarpIrOpCode.LoadInput or WarpIrOpCode.LoadScalar or
            WarpIrOpCode.LoadArgument or WarpIrOpCode.Constant)
        {
            ValidateSourceInstruction(instruction, inputBufferCount, scalarArgumentCount, argumentCount, isEntry);
        }
        else if (WarpManagedStateOpCode.IsState(instruction.OpCode) || WarpManagedFrameOpCode.IsOwner(instruction.OpCode))
        {
            ValidateRuntimeWordInstruction(instruction, available);
        }
        else
        {
            ValidateOperationInstruction(instruction, available, functions);
        }
    }

    private static void ValidateSourceInstruction(
        WarpIrInstruction instruction,
        int inputBufferCount,
        int scalarArgumentCount,
        int argumentCount,
        bool isEntry)
    {
        RequireNoOperands(instruction);
        switch (instruction.OpCode)
        {
            case WarpIrOpCode.LoadInput:
                RequireEntrySource(instruction, isEntry);
                if (instruction.Immediate >= (uint)inputBufferCount)
                {
                    throw new ArgumentException("An instruction references an invalid input buffer.", nameof(instruction));
                }

                break;

            case WarpIrOpCode.LoadScalar:
                RequireEntrySource(instruction, isEntry);
                if (instruction.Immediate >= (uint)scalarArgumentCount)
                {
                    throw new ArgumentException("An instruction references an invalid scalar argument.", nameof(instruction));
                }

                break;

            case WarpIrOpCode.LoadArgument:
                if (isEntry || instruction.Immediate >= (uint)argumentCount)
                {
                    throw new ArgumentException("An instruction references an invalid function argument.", nameof(instruction));
                }

                break;

            case WarpIrOpCode.Constant:
                break;
        }
    }

    private static void ValidateOperationInstruction(
        WarpIrInstruction instruction,
        IReadOnlySet<int> available,
        IReadOnlyList<WarpControlFlowFunction> functions)
    {
        if (WarpManagedFrameOpCode.IsPrivate(instruction.OpCode))
        {
            ValidatePrivateInstruction(instruction, available);
            return;
        }
        switch (instruction.OpCode)
        {
            case WarpManagedMemoryOpCode.WordCount:
            case WarpManagedAtomicOpCode.Fence:
                ValidateWordQueryInstruction(instruction);
                break;

            case WarpManagedMemoryOpCode.LoadWord:
            case WarpManagedMemoryOpCode.WordAddress:
            case WarpManagedAtomicOpCode.LoadSequential:
            case WarpManagedAtomicOpCode.LoadAcquire:
            case WarpIrOpCode.BitwiseNot:
                ValidateUnaryInstruction(instruction, available);
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
            case WarpManagedMemoryOpCode.StoreWord:
            case WarpManagedAtomicOpCode.StoreSequential:
            case WarpManagedAtomicOpCode.StoreRelease:
            case WarpManagedAtomicOpCode.Exchange:
            case WarpManagedAtomicOpCode.Add:
                ValidateBinaryInstruction(instruction, available);
                break;

            case WarpIrOpCode.Select:
            case WarpManagedAtomicOpCode.CompareExchange:
                ValidateSelectInstruction(instruction, available);
                break;

            case WarpIrOpCode.Call:
                ValidateCallInstruction(instruction, available, functions);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(instruction),
                    instruction.OpCode,
                    "The instruction opcode is not registered.");
        }
    }

    private static void ValidateWordQueryInstruction(WarpIrInstruction instruction)
    {
        RequireNoOperands(instruction);
        if (instruction.Immediate != 0)
        {
            throw new ArgumentException("A word count or fence cannot declare an immediate.", nameof(instruction));
        }
    }

    private static void ValidateRuntimeWordInstruction(WarpIrInstruction instruction, IReadOnlySet<int> available)
    {
        if (WarpManagedFrameOpCode.IsOwner(instruction.OpCode) || instruction.OpCode == WarpManagedStateOpCode.WordCount)
        {
            ValidateWordQueryInstruction(instruction);
        }
        else if (instruction.OpCode == WarpManagedStateOpCode.StoreWord) { ValidateBinaryInstruction(instruction, available); }
        else { ValidateUnaryInstruction(instruction, available); }
    }

    private static void ValidatePrivateInstruction(WarpIrInstruction instruction, IReadOnlySet<int> available)
    {
        if (instruction.OpCode == WarpManagedFrameOpCode.LoadPrivateWord)
        {
            RequireNoOperands(instruction);
            return;
        }
        RequireAvailable(instruction.Left, available);
        if (instruction.Right != -1 || instruction.Third != -1)
        {
            throw new ArgumentException("A private store requires one value and a fixed admitted offset.", nameof(instruction));
        }
    }

    private static void ValidateUnaryInstruction(WarpIrInstruction instruction, IReadOnlySet<int> available)
    {
        RequireAvailable(instruction.Left, available);
        if (instruction.Right != -1 || instruction.Third != -1 || instruction.Immediate != 0)
        {
            throw new ArgumentException("A unary instruction has an unexpected operand.", nameof(instruction));
        }
    }

    private static void ValidateBinaryInstruction(WarpIrInstruction instruction, IReadOnlySet<int> available)
    {
        RequireAvailable(instruction.Left, available);
        RequireAvailable(instruction.Right, available);
        if (instruction.Third != -1 || instruction.Immediate != 0)
        {
            throw new ArgumentException("A binary instruction has an unexpected third operand.", nameof(instruction));
        }
    }

    private static void ValidateSelectInstruction(WarpIrInstruction instruction, IReadOnlySet<int> available)
    {
        RequireAvailable(instruction.Left, available);
        RequireAvailable(instruction.Right, available);
        RequireAvailable(instruction.Third, available);
        if (instruction.Immediate != 0)
        {
            throw new ArgumentException("A select instruction has an unexpected immediate.", nameof(instruction));
        }
    }

    private static void ValidateCallInstruction(
        WarpIrInstruction instruction,
        IReadOnlySet<int> available,
        IReadOnlyList<WarpControlFlowFunction> functions)
    {
        if (instruction.Left != -1 || instruction.Right != -1 || instruction.Third != -1 || instruction.Immediate != 0)
        {
            throw new ArgumentException("A call instruction has an unexpected fixed operand.", nameof(instruction));
        }

        if ((uint)instruction.Callee >= (uint)functions.Count)
        {
            throw new ArgumentException("A call instruction references an invalid function.", nameof(instruction));
        }

        WarpControlFlowFunction callee = functions[instruction.Callee];
        if (instruction.ResultWordCount != callee.ResultWordCount)
        {
            throw new ArgumentException("A call result width does not match its function signature.", nameof(instruction));
        }
        if (instruction.Arguments.Count != callee.ParameterCount)
        {
            throw new ArgumentException("A call argument count does not match its function signature.", nameof(instruction));
        }

        foreach (int argument in instruction.Arguments)
        {
            RequireAvailable(argument, available);
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
            throw new ArgumentException("A branch references an invalid block.", nameof(target));
        }

        WarpBasicBlock destination = blocks[target.Block];
        if (target.Arguments.Count != destination.Parameters.Count)
        {
            throw new ArgumentException("A branch argument count does not match its target block.", nameof(target));
        }

        for (int index = 0; index < target.Arguments.Count; index++)
        {
            int argument = target.Arguments[index];
            RequireAvailable(argument, available);
            if (valueTypes[argument] != destination.Parameters[index].Type)
            {
                throw new ArgumentException("A branch argument type does not match its target parameter.", nameof(target));
            }
        }
    }

    private static void ValidateStateDestinations(WarpBasicBlock[] entry,
        WarpControlFlowFunction[] functions, WarpLogicalExecutionMetadata? execution)
    {
        foreach (WarpStateDispatchTerminator dispatch in entry.Concat(functions.SelectMany(function => function.Blocks))
            .Select(block => block.Terminator).OfType<WarpStateDispatchTerminator>())
        {
            if (execution?.NonlocalStateDispatch != true)
            {
                throw new ArgumentException("A nonlocal dispatch requires explicitly admitted runtime capabilities.", nameof(execution));
            }
            foreach (WarpStateDispatchTarget target in dispatch.Destinations)
            {
                if (target.Function > functions.Length || target.Block >= (target.Function == 0 ? entry.Length : functions[target.Function - 1].Blocks.Count))
                {
                    throw new ArgumentException("A nonlocal continuation names a body or block outside its immutable closure.", nameof(entry));
                }
            }
        }
    }

    private static IEnumerable<int> GetStateEntryBlocks(IReadOnlyList<WarpBasicBlock> entry,
        IReadOnlyList<WarpControlFlowFunction> functions, int function) => entry.Concat(functions.SelectMany(body => body.Blocks))
        .Select(block => block.Terminator).OfType<WarpStateDispatchTerminator>()
        .SelectMany(dispatch => dispatch.Destinations).Where(target => target.Function == function).Select(target => target.Block);

    private static void ValidateReachability(IReadOnlyList<WarpBasicBlock> blocks, IEnumerable<int> additionalEntries)
    {
        var reachable = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(0);
        foreach (int entry in additionalEntries) { pending.Push(entry); }

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
            throw new ArgumentException("A load or constant instruction has unexpected operands.", nameof(instruction));
        }

        RequireNoCallMetadata(instruction);
    }

    private static void RequireNoCallMetadata(WarpIrInstruction instruction)
    {
        if (instruction.Callee != -1 || instruction.Arguments.Count != 0)
        {
            throw new ArgumentException("A non-call instruction has unexpected call metadata.", nameof(instruction));
        }
    }

    private static void RequireEntrySource(WarpIrInstruction instruction, bool isEntry)
    {
        if (!isEntry)
        {
            throw new ArgumentException(
                $"Function bodies cannot contain '{instruction.OpCode}' instructions.", nameof(instruction));
        }
    }

    private static void ValidateAcyclicCallGraph(
        IReadOnlyList<WarpBasicBlock> entryBlocks,
        WarpControlFlowFunction[] functions, bool recursive)
    {
        var states = new byte[functions.Length];
        var reachable = new HashSet<int>();
        var pending = new Stack<int>(GetFunctionTargets(entryBlocks));
        while (pending.TryPop(out int function))
        {
            if (!reachable.Add(function)) { continue; }
            foreach (int target in GetFunctionTargets(functions[function].Blocks)) { pending.Push(target); }
        }
        if (reachable.Count != functions.Length)
        {
            throw new ArgumentException(
                "Every control-flow function must be reachable from the kernel entry point.", nameof(functions));
        }
        // Nonlocal transfers reach a body without pushing a new call frame.
        // Recursion policy applies only to the ordinary call edges of those bodies.
        foreach (int function in reachable) { Visit(function); }

        void Visit(int function)
        {
            if (states[function] == 2)
            {
                return;
            }

            if (states[function] == 1)
            {
                if (recursive) { return; }
                throw new ArgumentException(
                    "Recursive call graphs require the portable logical stack and are not in this profile.", nameof(functions));
            }

            states[function] = 1;
            foreach (int callee in GetCallees(functions[function].Blocks))
            {
                Visit(callee);
            }

            states[function] = 2;
        }
    }

    private static IEnumerable<int> GetFunctionTargets(IReadOnlyList<WarpBasicBlock> blocks) =>
        GetCallees(blocks).Concat(blocks.Select(block => block.Terminator).OfType<WarpStateDispatchTerminator>()
            .SelectMany(dispatch => dispatch.Destinations).Where(target => target.Function != 0).Select(target => target.Function - 1));

    private static IEnumerable<int> GetCallees(IEnumerable<WarpBasicBlock> blocks) =>
        blocks.SelectMany(block => block.Instructions)
            .Where(instruction => instruction.OpCode == WarpIrOpCode.Call)
            .Select(instruction => instruction.Callee);

    private static void RequireAvailable(int value, IReadOnlySet<int> available)
    {
        if (value < 0 || !available.Contains(value))
        {
            throw new ArgumentException(
                "An SSA operand must be a block parameter or a prior result in the same block.", nameof(value));
        }
    }
}
