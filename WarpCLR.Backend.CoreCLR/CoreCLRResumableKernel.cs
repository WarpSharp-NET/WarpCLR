using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed class CoreCLRResumableKernel
{
    private static long nextAssemblyId;
    private readonly QuantumEntryPoint entryPoint;

    private CoreCLRResumableKernel(WarpLogicalMachineLayout layout, MethodInfo compiledEntryPoint)
    {
        Layout = layout;
        CompiledEntryPoint = compiledEntryPoint;
        entryPoint = compiledEntryPoint.CreateDelegate<QuantumEntryPoint>();
    }

    public WarpLogicalMachineLayout Layout { get; }

    public MethodInfo CompiledEntryPoint { get; }

    public bool IsCollectible => CompiledEntryPoint.Module.Assembly.IsCollectible;

    public static CoreCLRResumableKernel Compile(WarpLogicalMachineLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!RuntimeFeature.IsDynamicCodeSupported || !RuntimeFeature.IsDynamicCodeCompiled)
        {
            throw new PlatformNotSupportedException("The CoreCLR backend requires an available native .NET JIT.");
        }

        int maximumParallelCopies = layout.Kernel.Blocks
            .Concat(layout.Kernel.Functions.SelectMany(function => function.Blocks))
            .Max(block => block.Parameters.Count);
        long frameEstimate = 4096 + (16L * (maximumParallelCopies + 20));
        if (frameEstimate > CoreCLRJitKernel.MaximumAdmittedFrameBytes)
        {
            throw new CoreCLRCompilationResourceException(
                layout.Kernel.Name, frameEstimate, CoreCLRJitKernel.MaximumAdmittedFrameBytes);
        }

        var assemblyName = new AssemblyName($"WarpCLR.CoreCLR.Resumable.{Interlocked.Increment(ref nextAssemblyId)}");
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder type = assembly.DefineDynamicModule(assemblyName.Name!).DefineType(
            "CompiledQuantum", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder method = type.DefineMethod("ExecuteQuantum", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(uint[][]), typeof(uint[]), typeof(int), typeof(uint[]), typeof(int), typeof(int), typeof(CancellationToken)]);
        new QuantumEmitter(layout, method.GetILGenerator(), maximumParallelCopies).Emit();
        MethodInfo compiledMethod = type.CreateType()!.GetMethod(method.Name)!;
        RuntimeHelpers.PrepareMethod(compiledMethod.MethodHandle);
        return new CoreCLRResumableKernel(layout, compiledMethod);
    }

    public void ExecuteQuantum(
        uint[][] inputs,
        uint[] scalarArguments,
        int workerIndex,
        uint[] state,
        int maximumCallDepth,
        int quantum,
        CancellationToken cancellationToken = default)
    {
        ValidateInvocation(inputs, scalarArguments, workerIndex, state, maximumCallDepth, quantum);
        if (state[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
        }
        catch (InsufficientExecutionStackException)
        {
            throw new CoreCLRResourceLimitException(
                CoreCLRResourceLimitKind.StackExhausted, 0, depth: checked((int)state[WarpLogicalMachineLayout.DepthOffset]));
        }

        entryPoint(inputs, scalarArguments, workerIndex, state, maximumCallDepth, quantum, cancellationToken);
    }

    private void ValidateInvocation(uint[][] inputs, uint[] scalars, int workerIndex, uint[] state, int maximumCallDepth, int quantum)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(scalars);
        ArgumentNullException.ThrowIfNull(state);
        if (inputs.Length != Layout.Kernel.InputBufferCount || scalars.Length != Layout.Kernel.ScalarArgumentCount)
        {
            throw new ArgumentException("Input and scalar argument counts must match the verified kernel.", nameof(inputs));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(workerIndex);
        foreach (uint[] input in inputs)
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

        ArgumentOutOfRangeException.ThrowIfLessThan(quantum, Layout.MaximumBlockCost);
        if (state.Length != Layout.GetStateWords(maximumCallDepth))
        {
            throw new ArgumentException("Logical machine state must match the admitted frame capacity.", nameof(state));
        }

        if (state[WarpLogicalMachineLayout.StatusOffset] > WarpLogicalMachineLayout.Faulted ||
            state[WarpLogicalMachineLayout.DepthOffset] == 0 ||
            state[WarpLogicalMachineLayout.DepthOffset] > (uint)maximumCallDepth ||
            state[WarpLogicalMachineLayout.RemainingStepsHighOffset] > int.MaxValue)
        {
            throw new ArgumentException("Logical machine header is invalid.", nameof(state));
        }

        ValidateFrames(state);
    }

    private void ValidateFrames(uint[] state)
    {
        for (int depth = 0; depth < state[WarpLogicalMachineLayout.DepthOffset]; depth++)
        {
            int frame = checked(WarpLogicalMachineLayout.HeaderWords + (depth * Layout.FrameWords));
            uint pc = state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset];
            uint function = state[frame + WarpLogicalMachineLayout.FrameFunctionOffset];
            if ((depth == 0 && function != 0) || pc >= (uint)Layout.Nodes.Count || function > (uint)Layout.Kernel.Functions.Count ||
                function != (uint)Layout.Nodes[checked((int)pc)].Function)
            {
                throw new ArgumentException("A logical machine frame has an invalid program counter or function.", nameof(state));
            }

            if (depth != 0)
            {
                int callerFrame = frame - Layout.FrameWords;
                uint callerFunction = state[callerFrame + WarpLogicalMachineLayout.FrameFunctionOffset];
                int callerValues = callerFunction == 0
                    ? Layout.Kernel.ValueCount
                    : Layout.Kernel.Functions[checked((int)callerFunction - 1)].ValueCount;
                if (state[frame + WarpLogicalMachineLayout.FrameReturnValueOffset] >= (uint)callerValues)
                {
                    throw new ArgumentException("A logical machine frame has an invalid return destination.", nameof(state));
                }
            }
        }
    }

    private delegate void QuantumEntryPoint(
        uint[][] inputs,
        uint[] scalarArguments,
        int workerIndex,
        uint[] state,
        int maximumCallDepth,
        int quantum,
        CancellationToken cancellationToken);

    private sealed class QuantumEmitter
    {
        private static readonly MethodInfo CancellationCheck = typeof(CancellationToken)
            .GetMethod(nameof(CancellationToken.ThrowIfCancellationRequested))!;
        private readonly WarpLogicalMachineLayout layout;
        private readonly ILGenerator il;
        private readonly LocalBuilder frame;
        private readonly LocalBuilder depth;
        private readonly LocalBuilder nextFrame;
        private readonly LocalBuilder remainingSteps;
        private readonly LocalBuilder remainingQuantum;
        private readonly LocalBuilder result;
        private readonly LocalBuilder[] edgeCopies;
        private readonly Label loop;
        private readonly Label[] nodes;

        public QuantumEmitter(WarpLogicalMachineLayout layout, ILGenerator il, int maximumParallelCopies)
        {
            this.layout = layout;
            this.il = il;
            frame = il.DeclareLocal(typeof(int));
            depth = il.DeclareLocal(typeof(int));
            nextFrame = il.DeclareLocal(typeof(int));
            remainingSteps = il.DeclareLocal(typeof(ulong));
            remainingQuantum = il.DeclareLocal(typeof(int));
            result = il.DeclareLocal(typeof(uint));
            edgeCopies = Enumerable.Range(0, maximumParallelCopies).Select(_ => il.DeclareLocal(typeof(uint))).ToArray();
            loop = il.DefineLabel();
            nodes = layout.Nodes.Select(_ => il.DefineLabel()).ToArray();
        }

        public void Emit()
        {
            LoadState(WarpLogicalMachineLayout.RemainingStepsLowOffset);
            il.Emit(OpCodes.Conv_U8);
            LoadState(WarpLogicalMachineLayout.RemainingStepsHighOffset);
            il.Emit(OpCodes.Conv_U8);
            Constant(32);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Stloc, remainingSteps);
            il.Emit(OpCodes.Ldarg_S, (byte)5);
            il.Emit(OpCodes.Stloc, remainingQuantum);
            il.MarkLabel(loop);
            il.Emit(OpCodes.Ldarga_S, (byte)6);
            il.Emit(OpCodes.Call, CancellationCheck);
            LoadState(WarpLogicalMachineLayout.DepthOffset);
            il.Emit(OpCodes.Stloc, depth);
            il.Emit(OpCodes.Ldloc, depth);
            Constant(1);
            il.Emit(OpCodes.Sub);
            Constant(layout.FrameWords);
            il.Emit(OpCodes.Mul);
            Constant(WarpLogicalMachineLayout.HeaderWords);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, frame);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameProgramCounterOffset);
            il.Emit(OpCodes.Switch, nodes);
            il.Emit(OpCodes.Ldstr, "A logical machine program counter is invalid.");
            il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Throw);
            foreach (WarpLogicalMachineNode node in layout.Nodes)
            {
                il.MarkLabel(nodes[node.ProgramCounter]);
                EmitNode(node);
            }
        }

        private void EmitNode(WarpLogicalMachineNode node)
        {
            if (node.StartsBlock)
            {
                EmitBlockCharge(node);
            }

            foreach (WarpIrInstruction instruction in node.Instructions)
            {
                StoreFrame(frame, WarpLogicalMachineLayout.FrameHeaderWords + instruction.Result,
                    () => EmitInstruction(instruction));
            }

            if (node.Call is WarpIrInstruction call)
            {
                EmitCall(node, call);
            }
            else
            {
                EmitTerminator(node);
            }
        }

        private void EmitBlockCharge(WarpLogicalMachineNode node)
        {
            Label totalAdmitted = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, remainingSteps);
            Constant(node.BlockCost);
            il.Emit(OpCodes.Conv_U8);
            il.Emit(OpCodes.Bge_Un, totalAdmitted);
            EmitFault(node, WarpLogicalMachineLayout.StepLimitFault);
            il.MarkLabel(totalAdmitted);
            Label quantumAdmitted = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, remainingQuantum);
            Constant(node.BlockCost);
            il.Emit(OpCodes.Bge, quantumAdmitted);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(quantumAdmitted);
            il.Emit(OpCodes.Ldloc, remainingSteps);
            Constant(node.BlockCost);
            il.Emit(OpCodes.Conv_U8);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Stloc, remainingSteps);
            StoreState(WarpLogicalMachineLayout.RemainingStepsLowOffset, () =>
            {
                il.Emit(OpCodes.Ldloc, remainingSteps);
                il.Emit(OpCodes.Conv_U4);
            });
            StoreState(WarpLogicalMachineLayout.RemainingStepsHighOffset, () =>
            {
                il.Emit(OpCodes.Ldloc, remainingSteps);
                Constant(32);
                il.Emit(OpCodes.Shr_Un);
                il.Emit(OpCodes.Conv_U4);
            });
            il.Emit(OpCodes.Ldloc, remainingQuantum);
            Constant(node.BlockCost);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Stloc, remainingQuantum);
        }

        private void EmitCall(WarpLogicalMachineNode node, WarpIrInstruction call)
        {
            Label admitted = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, depth);
            il.Emit(OpCodes.Ldarg_S, (byte)4);
            il.Emit(OpCodes.Blt, admitted);
            EmitFault(node, WarpLogicalMachineLayout.CallDepthFault);
            il.MarkLabel(admitted);
            il.Emit(OpCodes.Ldloc, frame);
            Constant(layout.FrameWords);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, nextFrame);
            StoreFrame(nextFrame, WarpLogicalMachineLayout.FrameFunctionOffset, () => Constant(call.Callee + 1));
            StoreFrame(nextFrame, WarpLogicalMachineLayout.FrameProgramCounterOffset,
                () => Constant(layout.GetBlockEntry(call.Callee + 1, 0)));
            StoreFrame(nextFrame, WarpLogicalMachineLayout.FrameReturnValueOffset, () => Constant(call.Result));
            for (int index = 0; index < call.Arguments.Count; index++)
            {
                int argument = call.Arguments[index];
                StoreFrame(nextFrame, layout.ArgumentOffset + index, () => LoadValue(argument));
            }

            StoreFrame(frame, WarpLogicalMachineLayout.FrameProgramCounterOffset, () => Constant(node.Continuation));
            StoreState(WarpLogicalMachineLayout.DepthOffset, () =>
            {
                il.Emit(OpCodes.Ldloc, depth);
                Constant(1);
                il.Emit(OpCodes.Add);
            });
            il.Emit(OpCodes.Br, loop);
        }

        private void EmitTerminator(WarpLogicalMachineNode node)
        {
            switch (node.Terminator)
            {
                case WarpBranchTerminator branch:
                    EmitEdge(node.Function, branch.Target);
                    break;
                case WarpConditionalBranchTerminator conditional:
                    Label whenZero = il.DefineLabel();
                    LoadValue(conditional.Condition);
                    il.Emit(OpCodes.Brfalse, whenZero);
                    EmitEdge(node.Function, conditional.WhenNonZero);
                    il.MarkLabel(whenZero);
                    EmitEdge(node.Function, conditional.WhenZero);
                    break;
                case WarpReturnTerminator @return:
                    EmitReturn(@return);
                    break;
                default:
                    throw new InvalidOperationException("The CoreCLR JIT received an unregistered terminator.");
            }
        }

        private void EmitEdge(int function, WarpBranchTarget target)
        {
            ReadOnlyCollection<WarpBasicBlock> blocks = function == 0
                ? layout.Kernel.Blocks
                : layout.Kernel.Functions[function - 1].Blocks;
            for (int index = 0; index < target.Arguments.Count; index++)
            {
                LoadValue(target.Arguments[index]);
                il.Emit(OpCodes.Stloc, edgeCopies[index]);
            }

            for (int index = 0; index < target.Arguments.Count; index++)
            {
                LocalBuilder copy = edgeCopies[index];
                StoreFrame(frame, WarpLogicalMachineLayout.FrameHeaderWords + blocks[target.Block].Parameters[index].Value,
                    () => il.Emit(OpCodes.Ldloc, copy));
            }

            StoreFrame(frame, WarpLogicalMachineLayout.FrameProgramCounterOffset,
                () => Constant(layout.GetBlockEntry(function, target.Block)));
            il.Emit(OpCodes.Br, loop);
        }

        private void EmitReturn(WarpReturnTerminator terminator)
        {
            LoadValue(terminator.Value);
            il.Emit(OpCodes.Stloc, result);
            Label helperReturn = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, depth);
            Constant(1);
            il.Emit(OpCodes.Bne_Un, helperReturn);
            StoreState(WarpLogicalMachineLayout.ResultOffset, () => il.Emit(OpCodes.Ldloc, result));
            StoreState(WarpLogicalMachineLayout.StatusOffset, () => Constant((int)WarpLogicalMachineLayout.Completed));
            il.Emit(OpCodes.Ret);
            il.MarkLabel(helperReturn);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldloc, frame);
            Constant(layout.FrameWords);
            il.Emit(OpCodes.Sub);
            Constant(WarpLogicalMachineLayout.FrameHeaderWords);
            il.Emit(OpCodes.Add);
            LoadFrame(frame, WarpLogicalMachineLayout.FrameReturnValueOffset);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Stelem_I4);
            StoreState(WarpLogicalMachineLayout.DepthOffset, () =>
            {
                il.Emit(OpCodes.Ldloc, depth);
                Constant(1);
                il.Emit(OpCodes.Sub);
            });
            il.Emit(OpCodes.Br, loop);
        }

        private void EmitFault(WarpLogicalMachineNode node, uint kind)
        {
            StoreState(WarpLogicalMachineLayout.StatusOffset, () => Constant((int)WarpLogicalMachineLayout.Faulted));
            StoreState(WarpLogicalMachineLayout.FaultKindOffset, () => Constant((int)kind));
            StoreState(WarpLogicalMachineLayout.FaultFunctionOffset, () => Constant(node.Function));
            StoreState(WarpLogicalMachineLayout.FaultBlockOffset, () => Constant(node.Block));
            il.Emit(OpCodes.Ret);
        }

        private void EmitInstruction(WarpIrInstruction instruction)
        {
            switch (instruction.OpCode)
            {
                case WarpIrOpCode.LoadInput:
                    il.Emit(OpCodes.Ldarg_0);
                    Constant(checked((int)instruction.Immediate));
                    il.Emit(OpCodes.Ldelem_Ref);
                    il.Emit(OpCodes.Ldarg_2);
                    il.Emit(OpCodes.Ldelem_U4);
                    return;
                case WarpIrOpCode.LoadScalar:
                    il.Emit(OpCodes.Ldarg_1);
                    Constant(checked((int)instruction.Immediate));
                    il.Emit(OpCodes.Ldelem_U4);
                    return;
                case WarpIrOpCode.LoadArgument:
                    LoadFrame(frame, layout.ArgumentOffset + checked((int)instruction.Immediate));
                    return;
                case WarpIrOpCode.Constant:
                    Constant(unchecked((int)instruction.Immediate));
                    return;
                case WarpIrOpCode.BitwiseNot:
                    LoadValue(instruction.Left);
                    il.Emit(OpCodes.Not);
                    return;
                case WarpIrOpCode.Select:
                    Label whenZero = il.DefineLabel();
                    Label selected = il.DefineLabel();
                    LoadValue(instruction.Left);
                    il.Emit(OpCodes.Brfalse, whenZero);
                    LoadValue(instruction.Right);
                    il.Emit(OpCodes.Br, selected);
                    il.MarkLabel(whenZero);
                    LoadValue(instruction.Third);
                    il.MarkLabel(selected);
                    return;
                case WarpIrOpCode.Call:
                    throw new InvalidOperationException("Calls must be split into logical continuation nodes.");
            }

            EmitBinaryInstruction(instruction);
        }

        private void EmitBinaryInstruction(WarpIrInstruction instruction)
        {
            LoadValue(instruction.Left);
            LoadValue(instruction.Right);
            if (instruction.OpCode is WarpIrOpCode.ShiftLeft or WarpIrOpCode.ShiftRightLogical)
            {
                Constant(31);
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
            if (instruction.OpCode is WarpIrOpCode.NotEqual or WarpIrOpCode.LessThanOrEqualUnsigned or WarpIrOpCode.GreaterThanOrEqualUnsigned)
            {
                Constant(0);
                il.Emit(OpCodes.Ceq);
            }
        }

        private void LoadValue(int value) => LoadFrame(frame, WarpLogicalMachineLayout.FrameHeaderWords + value);

        private void LoadState(int offset)
        {
            il.Emit(OpCodes.Ldarg_3);
            Constant(offset);
            il.Emit(OpCodes.Ldelem_U4);
        }

        private void StoreState(int offset, Action emitValue)
        {
            il.Emit(OpCodes.Ldarg_3);
            Constant(offset);
            emitValue();
            il.Emit(OpCodes.Stelem_I4);
        }

        private void LoadFrame(LocalBuilder selectedFrame, int offset)
        {
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldloc, selectedFrame);
            Constant(offset);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldelem_U4);
        }

        private void StoreFrame(LocalBuilder selectedFrame, int offset, Action emitValue)
        {
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldloc, selectedFrame);
            Constant(offset);
            il.Emit(OpCodes.Add);
            emitValue();
            il.Emit(OpCodes.Stelem_I4);
        }

        private void Constant(int value) => il.Emit(OpCodes.Ldc_I4, value);
    }
}
