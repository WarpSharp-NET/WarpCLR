using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed class CoreCLRJitKernel
{
    public const int MaximumAdmittedFrameBytes = 32 * 1024;

    private static long nextAssemblyId;
    private static readonly MethodInfo ChargeMethod = typeof(CoreCLRExecutionBudget)
        .GetMethod(nameof(CoreCLRExecutionBudget.Charge))!;
    private static readonly MethodInfo EnterCallMethod = typeof(CoreCLRExecutionBudget)
        .GetMethod(nameof(CoreCLRExecutionBudget.EnterCall))!;
    private static readonly MethodInfo ExitCallMethod = typeof(CoreCLRExecutionBudget)
        .GetMethod(nameof(CoreCLRExecutionBudget.ExitCall))!;
    private static readonly MethodInfo CheckNativeStackMethod = typeof(CoreCLRExecutionBudget)
        .GetMethod(nameof(CoreCLRExecutionBudget.CheckNativeStack))!;

    private readonly WorkerEntryPoint entryPoint;

    private CoreCLRJitKernel(
        WarpControlFlowKernel kernel,
        MethodInfo compiledEntryPoint,
        IEnumerable<MethodInfo> compiledFunctions)
    {
        Kernel = kernel;
        CompiledEntryPoint = compiledEntryPoint;
        CompiledFunctions = Array.AsReadOnly(compiledFunctions.ToArray());
        entryPoint = compiledEntryPoint.CreateDelegate<WorkerEntryPoint>();
    }

    public WarpControlFlowKernel Kernel { get; }

    public MethodInfo CompiledEntryPoint { get; }

    public ReadOnlyCollection<MethodInfo> CompiledFunctions { get; }

    public bool IsCollectible => CompiledEntryPoint.Module.Assembly.IsCollectible;

    public static CoreCLRJitKernel Compile(WarpControlFlowKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        WarpCompilationAdmission.Validate(kernel);
        if (!RuntimeFeature.IsDynamicCodeSupported || !RuntimeFeature.IsDynamicCodeCompiled)
        {
            throw new PlatformNotSupportedException("The CoreCLR backend requires an available native .NET JIT.");
        }

        ValidateKernelNativeFrameAdmission(kernel);

        var assemblyName = new AssemblyName(
            $"WarpCLR.CoreCLR.Jit.{Interlocked.Increment(ref nextAssemblyId)}");
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            assemblyName,
            AssemblyBuilderAccess.RunAndCollect);
        ModuleBuilder module = assembly.DefineDynamicModule(assemblyName.Name!);
        TypeBuilder type = module.DefineType(
            "CompiledKernel",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        const MethodAttributes attributes = MethodAttributes.Public | MethodAttributes.Static;
        var entry = type.DefineMethod(
            "InvokeWorker",
            attributes,
            typeof(uint),
            [typeof(uint[][]), typeof(uint[]), typeof(int), typeof(CoreCLRExecutionBudget)]);
        var functions = new MethodBuilder[kernel.Functions.Count];
        foreach (WarpControlFlowFunction function in kernel.Functions)
        {
            Type[] parameterTypes = Enumerable.Repeat(typeof(uint), function.ParameterCount + 1).ToArray();
            parameterTypes[0] = typeof(CoreCLRExecutionBudget);
            functions[function.Id] = type.DefineMethod(
                $"Function_{function.Id}",
                attributes,
                typeof(uint),
                parameterTypes);
        }

        EmitBody(entry.GetILGenerator(), kernel.Blocks, kernel.ValueCount, functions, budgetArgument: 3);
        foreach (WarpControlFlowFunction function in kernel.Functions)
        {
            EmitBody(
                functions[function.Id].GetILGenerator(),
                function.Blocks,
                function.ValueCount,
                functions,
                budgetArgument: 0);
        }

        Type compiledType = type.CreateType()!;
        MethodInfo compiledEntry = compiledType.GetMethod(entry.Name)!;
        MethodInfo[] compiledFunctions = functions
            .Select(function => compiledType.GetMethod(function.Name)!)
            .ToArray();
        foreach (MethodInfo method in compiledFunctions.Append(compiledEntry))
        {
            RuntimeHelpers.PrepareMethod(method.MethodHandle);
        }

        return new CoreCLRJitKernel(kernel, compiledEntry, compiledFunctions);
    }

    public uint Invoke(
        uint[][] inputs,
        uint[] scalarArguments,
        int workerIndex,
        CoreCLRExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(scalarArguments);
        ArgumentNullException.ThrowIfNull(budget);
        using WarpOrdinaryArrayAdmission admission = WarpOrdinaryArrayAdmission.Acquire(inputs, scalarArguments, [], []);
        uint[][] capturedInputs = CoreCLROrdinaryArrayEmission.CaptureInputReferences(admission, inputs.Length);
        if (inputs.Length != Kernel.InputBufferCount)
        {
            throw new ArgumentException("The input buffer count does not match the verified kernel.", nameof(inputs));
        }

        if (scalarArguments.Length != Kernel.ScalarArgumentCount)
        {
            throw new ArgumentException("The scalar argument count does not match the verified kernel.", nameof(scalarArguments));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(workerIndex);
        foreach (uint[] input in capturedInputs)
        {
            if (input is null)
            {
                throw new ArgumentException("An input buffer cannot be null.", nameof(inputs));
            }

            if ((uint)workerIndex >= (uint)input.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            }
        }

        budget.BeginInvocation();
        try
        {
            budget.CheckNativeStack();
            return entryPoint(capturedInputs, scalarArguments, workerIndex, budget);
        }
        finally
        {
            budget.EndInvocation();
        }
    }

    private static void ValidateKernelNativeFrameAdmission(WarpControlFlowKernel kernel)
    {
        ValidateNativeFrameAdmission(kernel.Name, kernel.Blocks, kernel.ValueCount, parameterCount: 4);
        foreach (WarpControlFlowFunction function in kernel.Functions)
        {
            ValidateNativeFrameAdmission(function.Name, function.Blocks, function.ValueCount, function.ParameterCount + 1);
        }
    }

    private static void ValidateNativeFrameAdmission(
        string bodyName,
        IReadOnlyList<WarpBasicBlock> blocks,
        int valueCount,
        int parameterCount)
    {
        int parallelCopies = blocks.Max(block => block.Parameters.Count);
        int maximumOutgoingArguments = blocks.SelectMany(block => block.Instructions)
            .Where(instruction => instruction.OpCode == WarpIrOpCode.Call)
            .Select(instruction => instruction.Arguments.Count + 1)
            .DefaultIfEmpty(0)
            .Max();
        // UInt32 locals/argument slots need at most a machine word each. Sixteen
        // bytes per slot plus 4 KiB accounts conservatively for spills, alignment,
        // EH state, and compiler temporaries. This is an admission policy, not an
        // assumed thread-stack size; each actual invocation also probes CoreCLR.
        long estimate = 4096 + (16L * (valueCount + (long)parallelCopies + parameterCount + maximumOutgoingArguments + 1));
        if (estimate > MaximumAdmittedFrameBytes)
        {
            throw new CoreCLRCompilationResourceException(bodyName, estimate, MaximumAdmittedFrameBytes);
        }
    }

    private static void EmitBody(
        ILGenerator il,
        IReadOnlyList<WarpBasicBlock> blocks,
        int valueCount,
        IReadOnlyList<MethodBuilder> functions,
        int budgetArgument)
    {
        LocalBuilder[] values = Enumerable.Range(0, valueCount)
            .Select(_ => il.DeclareLocal(typeof(uint)))
            .ToArray();
        LocalBuilder[] edgeArguments = Enumerable.Range(0, blocks.Max(block => block.Parameters.Count))
            .Select(_ => il.DeclareLocal(typeof(uint)))
            .ToArray();
        LocalBuilder returnValue = il.DeclareLocal(typeof(uint));
        Label[] labels = blocks.Select(_ => il.DefineLabel()).ToArray();

        // Pure compiled functions have only budget/UInt32 arguments and cannot load array banks.
        CoreCLROrdinaryArrayEmission? ordinary = budgetArgument == 3 ? new(il) : null;
        ordinary?.Begin(scalarArgument: 1);

        EmitArgument(il, budgetArgument);
        il.Emit(OpCodes.Callvirt, EnterCallMethod);
        Label exit = il.BeginExceptionBlock();
        foreach (WarpBasicBlock block in blocks)
        {
            il.MarkLabel(labels[block.Id]);
            EmitArgument(il, budgetArgument);
            EmitConstant(il, checked(block.Instructions.Count + 1));
            il.Emit(OpCodes.Callvirt, ChargeMethod);
            foreach (WarpIrInstruction instruction in block.Instructions)
            {
                EmitInstruction(il, instruction, values, functions, budgetArgument, ordinary);
                il.Emit(OpCodes.Stloc, values[instruction.Result]);
            }

            EmitBodyTerminator(il, block.Terminator, blocks, values, edgeArguments, labels, returnValue, exit);
        }

        il.BeginFinallyBlock();
        EmitArgument(il, budgetArgument);
        il.Emit(OpCodes.Callvirt, ExitCallMethod);
        il.EndExceptionBlock();
        ordinary?.End();
        il.Emit(OpCodes.Ldloc, returnValue);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitBodyTerminator(ILGenerator il, WarpBlockTerminator terminator,
        IReadOnlyList<WarpBasicBlock> blocks, LocalBuilder[] values, LocalBuilder[] edgeArguments,
        Label[] labels, LocalBuilder returnValue, Label exit)
    {
        switch (terminator)
        {
            case WarpBranchTerminator branch:
                EmitEdge(il, branch.Target, blocks, values, edgeArguments, labels);
                break;

            case WarpConditionalBranchTerminator conditional:
                Label whenZero = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, values[conditional.Condition]);
                il.Emit(OpCodes.Brfalse, whenZero);
                EmitEdge(il, conditional.WhenNonZero, blocks, values, edgeArguments, labels);
                il.MarkLabel(whenZero);
                EmitEdge(il, conditional.WhenZero, blocks, values, edgeArguments, labels);
                break;

            case WarpReturnTerminator @return:
                il.Emit(OpCodes.Ldloc, values[@return.Value]);
                il.Emit(OpCodes.Stloc, returnValue);
                il.Emit(OpCodes.Leave, exit);
                break;

            default:
                throw new InvalidOperationException("The CoreCLR JIT received an unregistered terminator.");
        }
    }

    private static void EmitInstruction(
        ILGenerator il,
        WarpIrInstruction instruction,
        LocalBuilder[] values,
        IReadOnlyList<MethodBuilder> functions,
        int budgetArgument,
        CoreCLROrdinaryArrayEmission? ordinary)
    {
        switch (instruction.OpCode)
        {
            case WarpIrOpCode.LoadInput:
                if (ordinary is null) { throw new InvalidOperationException("A value-only function cannot load an input bank."); }
                ordinary.LoadInput(checked((int)instruction.Immediate));
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldelem_U4);
                return;

            case WarpIrOpCode.LoadScalar:
                il.Emit(OpCodes.Ldarg_1);
                EmitConstant(il, checked((int)instruction.Immediate));
                il.Emit(OpCodes.Ldelem_U4);
                return;

            case WarpIrOpCode.LoadArgument:
                EmitArgument(il, checked((int)instruction.Immediate + 1));
                return;

            case WarpIrOpCode.Constant:
                EmitConstant(il, unchecked((int)instruction.Immediate));
                return;

            case WarpIrOpCode.Call:
                EmitArgument(il, budgetArgument);
                il.Emit(OpCodes.Callvirt, CheckNativeStackMethod);
                EmitArgument(il, budgetArgument);
                foreach (int argument in instruction.Arguments)
                {
                    il.Emit(OpCodes.Ldloc, values[argument]);
                }

                il.Emit(OpCodes.Call, functions[instruction.Callee]);
                return;

            case WarpIrOpCode.Select:
                Label whenZero = il.DefineLabel();
                Label selected = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, values[instruction.Left]);
                il.Emit(OpCodes.Brfalse, whenZero);
                il.Emit(OpCodes.Ldloc, values[instruction.Right]);
                il.Emit(OpCodes.Br, selected);
                il.MarkLabel(whenZero);
                il.Emit(OpCodes.Ldloc, values[instruction.Third]);
                il.MarkLabel(selected);
                return;

            case WarpIrOpCode.BitwiseNot:
                il.Emit(OpCodes.Ldloc, values[instruction.Left]);
                il.Emit(OpCodes.Not);
                return;
        }

        EmitBinaryInstruction(il, instruction, values);
    }

    private static void EmitBinaryInstruction(ILGenerator il, WarpIrInstruction instruction, LocalBuilder[] values)
    {
        il.Emit(OpCodes.Ldloc, values[instruction.Left]);
        il.Emit(OpCodes.Ldloc, values[instruction.Right]);
        if (instruction.OpCode is WarpIrOpCode.ShiftLeft or WarpIrOpCode.ShiftRightLogical)
        {
            EmitConstant(il, 31);
            il.Emit(OpCodes.And);
        }

        OpCode operation = instruction.OpCode switch
        {
            WarpIrOpCode.Add => OpCodes.Add,
            WarpIrOpCode.Subtract => OpCodes.Sub,
            WarpIrOpCode.Multiply => OpCodes.Mul,
            WarpIrOpCode.BitwiseAnd => OpCodes.And,
            WarpIrOpCode.BitwiseOr => OpCodes.Or,
            WarpIrOpCode.ExclusiveOr => OpCodes.Xor,
            WarpIrOpCode.ShiftLeft => OpCodes.Shl,
            WarpIrOpCode.ShiftRightLogical => OpCodes.Shr_Un,
            WarpIrOpCode.Equal or WarpIrOpCode.NotEqual => OpCodes.Ceq,
            WarpIrOpCode.LessThanUnsigned or WarpIrOpCode.GreaterThanOrEqualUnsigned => OpCodes.Clt_Un,
            WarpIrOpCode.GreaterThanUnsigned or WarpIrOpCode.LessThanOrEqualUnsigned => OpCodes.Cgt_Un,
            _ => throw new InvalidOperationException("The CoreCLR JIT received an unregistered opcode."),
        };
        il.Emit(operation);
        if (instruction.OpCode is WarpIrOpCode.NotEqual or
            WarpIrOpCode.LessThanOrEqualUnsigned or WarpIrOpCode.GreaterThanOrEqualUnsigned)
        {
            EmitBooleanNot(il);
        }
    }

    private static void EmitEdge(
        ILGenerator il,
        WarpBranchTarget target,
        IReadOnlyList<WarpBasicBlock> blocks,
        LocalBuilder[] values,
        LocalBuilder[] edgeArguments,
        Label[] labels)
    {
        for (int index = 0; index < target.Arguments.Count; index++)
        {
            il.Emit(OpCodes.Ldloc, values[target.Arguments[index]]);
            il.Emit(OpCodes.Stloc, edgeArguments[index]);
        }

        for (int index = 0; index < target.Arguments.Count; index++)
        {
            il.Emit(OpCodes.Ldloc, edgeArguments[index]);
            il.Emit(OpCodes.Stloc, values[blocks[target.Block].Parameters[index].Value]);
        }

        il.Emit(OpCodes.Br, labels[target.Block]);
    }

    private static void EmitBooleanNot(ILGenerator il)
    {
        EmitConstant(il, 0);
        il.Emit(OpCodes.Ceq);
    }

    private static void EmitConstant(ILGenerator il, int value) => il.Emit(OpCodes.Ldc_I4, value);

    private static void EmitArgument(ILGenerator il, int argument) =>
        il.Emit(OpCodes.Ldarg, unchecked((short)checked((ushort)argument)));

    private delegate uint WorkerEntryPoint(
        uint[][] inputs,
        uint[] scalarArguments,
        int workerIndex,
        CoreCLRExecutionBudget budget);
}
