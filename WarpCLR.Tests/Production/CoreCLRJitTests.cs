using System.Reflection;
using System.Runtime.CompilerServices;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
public sealed class CoreCLRJitTests
{
    [TestMethod]
    [DataRow(nameof(TestKernels.Grayscale), 1)]
    [DataRow(nameof(TestKernels.Combine), 2)]
    [DataRow(nameof(TestKernels.Scramble), 1)]
    [DataRow(nameof(TestKernels.Branch), 1)]
    [DataRow(nameof(TestKernels.CompareAndSelect), 1)]
    [DataRow(nameof(TestKernels.Loop), 1)]
    [DataRow(nameof(TestKernels.Call), 1)]
    public void Jitted_source_corpus_matches_original_CLR_and_semantic_oracle(
        string methodName,
        int inputCount)
    {
        MethodInfo source = typeof(TestKernels).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;
        WarpControlFlowKernel kernel = new WarpIntegerMapVerifier()
            .Verify(new WarpIntegerMapRequest(source, inputCount))
            .ControlFlow;
        CoreCLRJitKernel executable = CoreCLRJitKernel.Compile(kernel);
        Assert.IsTrue(executable.IsCollectible);
        Assert.IsTrue(executable.CompiledEntryPoint.Module.Assembly.IsDynamic);
        Assert.AreNotEqual(typeof(WarpIntegerMapSemanticEmulator).Assembly,
            executable.CompiledEntryPoint.Module.Assembly);
        Assert.IsTrue(executable.CompiledEntryPoint.GetMethodBody()!.GetILAsByteArray()!.Length > 0);
        Assert.AreNotEqual(IntPtr.Zero, executable.CompiledEntryPoint.MethodHandle.GetFunctionPointer());

        uint[] values = methodName == nameof(TestKernels.Loop)
            ? [0, 1, 2, 5, 100]
            : [0, 1, 7, 8, 0x80000000, uint.MaxValue, 0xDEADBEEF, 0x12345678];
        uint[][] inputs = inputCount == 1
            ? [values]
            : [values, values.Reverse().ToArray()];
        uint[] scalars = methodName switch
        {
            nameof(TestKernels.Combine) => [0xA5A5A5A5, 37],
            nameof(TestKernels.Scramble) => [63],
            nameof(TestKernels.CompareAndSelect) => [7],
            _ => [],
        };
        uint[] oracle = new WarpIntegerMapSemanticEmulator().Execute(
            new CoreCLRBackendCompiler().Compile(kernel), kernel, inputs, scalars);
        for (int index = 0; index < values.Length; index++)
        {
            object[] originalArguments = inputs.Select(input => (object)input[index])
                .Concat(scalars.Select(scalar => (object)scalar))
                .ToArray();
            uint original = (uint)source.Invoke(null, originalArguments)!;
            uint actual = executable.Invoke(inputs, scalars, index, NewBudget());
            Assert.AreEqual(original, actual);
            Assert.AreEqual(oracle[index], actual);
        }

        Assert.AreEqual(kernel.Functions.Count, executable.CompiledFunctions.Count);
        foreach (MethodInfo function in executable.CompiledFunctions)
        {
            Assert.AreNotEqual(IntPtr.Zero, function.MethodHandle.GetFunctionPointer());
        }
    }

    [TestMethod]
    public void Every_binary_operation_has_unsigned_wrapping_semantics()
    {
        WarpIrOpCode[] operations =
        [
            WarpIrOpCode.Add, WarpIrOpCode.Subtract, WarpIrOpCode.Multiply,
            WarpIrOpCode.BitwiseAnd, WarpIrOpCode.BitwiseOr, WarpIrOpCode.ExclusiveOr,
            WarpIrOpCode.ShiftLeft, WarpIrOpCode.ShiftRightLogical,
            WarpIrOpCode.Equal, WarpIrOpCode.NotEqual,
            WarpIrOpCode.LessThanUnsigned, WarpIrOpCode.LessThanOrEqualUnsigned,
            WarpIrOpCode.GreaterThanUnsigned, WarpIrOpCode.GreaterThanOrEqualUnsigned,
        ];
        uint[][] inputs =
        [
            [0, 1, uint.MaxValue, 0x80000000, 17, uint.MaxValue],
            [uint.MaxValue, 1, 1, 0x7FFFFFFF, 63, uint.MaxValue],
        ];
        foreach (WarpIrOpCode operation in operations)
        {
            var kernel = new WarpControlFlowKernel(operation.ToString(), 2, 0,
            [
                new WarpBasicBlock(0, [],
                [
                    new WarpIrInstruction(0, WarpIrOpCode.LoadInput),
                    new WarpIrInstruction(1, WarpIrOpCode.LoadInput, immediate: 1),
                    new WarpIrInstruction(2, operation, 0, 1),
                ], new WarpReturnTerminator(2)),
            ]);
            CoreCLRJitKernel executable = CoreCLRJitKernel.Compile(kernel);
            uint[] expected = new WarpIntegerMapSemanticEmulator().Execute(
                new CoreCLRBackendCompiler().Compile(kernel), kernel, inputs);
            for (int index = 0; index < inputs[0].Length; index++)
            {
                Assert.AreEqual(expected[index], executable.Invoke(inputs, [], index, NewBudget()), operation.ToString());
            }
        }
    }

    [TestMethod]
    public void Select_treats_every_nonzero_condition_as_true()
    {
        var kernel = new WarpControlFlowKernel("Select", 1, 0,
        [
            new WarpBasicBlock(0, [],
            [
                new WarpIrInstruction(0, WarpIrOpCode.LoadInput),
                new WarpIrInstruction(1, WarpIrOpCode.Constant, immediate: 0xDEADBEEF),
                new WarpIrInstruction(2, WarpIrOpCode.Constant, immediate: 0x12345678),
                new WarpIrInstruction(3, WarpIrOpCode.Select, 0, 1, third: 2),
            ], new WarpReturnTerminator(3)),
        ]);
        CoreCLRJitKernel executable = CoreCLRJitKernel.Compile(kernel);
        uint[][] inputs = [[0, 1, 0x80000000, uint.MaxValue]];
        for (int index = 0; index < inputs[0].Length; index++)
        {
            Assert.AreEqual(index == 0 ? 0x12345678u : 0xDEADBEEFu,
                executable.Invoke(inputs, [], index, NewBudget()));
        }
    }

    [TestMethod]
    public void Backedge_block_arguments_are_parallel_assignments()
    {
        var kernel = new WarpControlFlowKernel("Swap", 1, 0,
        [
            new WarpBasicBlock(0, [],
            [
                new WarpIrInstruction(0, WarpIrOpCode.Constant, immediate: 1),
                new WarpIrInstruction(1, WarpIrOpCode.Constant, immediate: 2),
                new WarpIrInstruction(2, WarpIrOpCode.Constant, immediate: 2),
            ], new WarpBranchTerminator(new WarpBranchTarget(1, [0, 1, 2]))),
            new WarpBasicBlock(1,
                [new WarpBlockParameter(3), new WarpBlockParameter(4), new WarpBlockParameter(5)],
            [
                new WarpIrInstruction(6, WarpIrOpCode.Constant, immediate: 1),
                new WarpIrInstruction(7, WarpIrOpCode.Subtract, 5, 6),
                new WarpIrInstruction(8, WarpIrOpCode.GreaterThanUnsigned, 5, 6),
            ], new WarpConditionalBranchTerminator(8,
                new WarpBranchTarget(1, [4, 3, 7]), new WarpBranchTarget(2, [3, 4]))),
            new WarpBasicBlock(2, [new WarpBlockParameter(9), new WarpBlockParameter(10)],
            [
                new WarpIrInstruction(11, WarpIrOpCode.Constant, immediate: 100),
                new WarpIrInstruction(12, WarpIrOpCode.Multiply, 9, 11),
                new WarpIrInstruction(13, WarpIrOpCode.Add, 12, 10),
            ], new WarpReturnTerminator(13)),
        ]);

        var budget = new CoreCLRExecutionBudget(16, 1);
        Assert.AreEqual(201u, CoreCLRJitKernel.Compile(kernel).Invoke([[0]], [], 0, budget));
        Assert.AreEqual(16L, budget.StepsConsumed);
        Assert.AreEqual(0, budget.CurrentCallDepth);
    }

    [TestMethod]
    public void Nonterminating_worker_has_a_finite_deterministic_resource_fault()
    {
        CoreCLRJitKernel executable = CoreCLRJitKernel.Compile(NonterminatingKernel());
        var budget = new CoreCLRExecutionBudget(10, 1);
        CoreCLRResourceLimitException fault = Assert.ThrowsExactly<CoreCLRResourceLimitException>(
            () => executable.Invoke([[1]], [], 0, budget));
        Assert.AreEqual(CoreCLRResourceLimitKind.StepLimit, fault.Kind);
        Assert.AreEqual(10L, fault.Limit);
        Assert.AreEqual(8L, fault.StepsConsumed);
        Assert.AreEqual(1, fault.Depth);
        Assert.AreEqual(0, budget.CurrentCallDepth);
    }

    [TestMethod]
    public void Budget_limit_is_enforced_before_a_partial_block_executes()
    {
        CoreCLRJitKernel executable = CoreCLRJitKernel.Compile(NonterminatingKernel());
        var budget = new CoreCLRExecutionBudget(1, 1);
        CoreCLRResourceLimitException fault = Assert.ThrowsExactly<CoreCLRResourceLimitException>(
            () => executable.Invoke([[0]], [], 0, budget));
        Assert.AreEqual(CoreCLRResourceLimitKind.StepLimit, fault.Kind);
        Assert.AreEqual(0L, budget.StepsConsumed);
    }

    [TestMethod]
    public void Helper_frame_budget_is_not_host_stack_overflow()
    {
        MethodInfo source = typeof(TestKernels).GetMethod(nameof(TestKernels.Call))!;
        WarpControlFlowKernel kernel = new WarpIntegerMapVerifier()
            .Verify(new WarpIntegerMapRequest(source, 1)).ControlFlow;
        CoreCLRJitKernel executable = CoreCLRJitKernel.Compile(kernel);
        var budget = new CoreCLRExecutionBudget(10000, 1);
        CoreCLRResourceLimitException fault = Assert.ThrowsExactly<CoreCLRResourceLimitException>(
            () => executable.Invoke([[3]], [], 0, budget));
        Assert.AreEqual(CoreCLRResourceLimitKind.CallDepth, fault.Kind);
        Assert.AreEqual(1L, fault.Limit);
        Assert.AreEqual(1, fault.Depth);
        Assert.AreEqual(0, budget.CurrentCallDepth);
        Assert.AreEqual(TestKernels.Call(3), executable.Invoke([[3]], [], 0, NewBudget()));
    }

    [TestMethod]
    public void Precancelled_invocation_does_not_enter_a_frame_or_consume_steps()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var budget = new CoreCLRExecutionBudget(1000, 16, cancellation.Token);
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            CoreCLRJitKernel.Compile(NonterminatingKernel()).Invoke([[0]], [], 0, budget));
        Assert.AreEqual(0L, budget.StepsConsumed);
        Assert.AreEqual(0, budget.CurrentCallDepth);
    }

    [TestMethod]
    public async Task Backedge_safepoint_observes_cancellation_during_native_execution()
    {
        CoreCLRJitKernel executable = CoreCLRJitKernel.Compile(NonterminatingKernel());
        using var cancellation = new CancellationTokenSource();
        var budget = new CoreCLRExecutionBudget(long.MaxValue, 16, cancellation.Token);
        Task<uint> execution = Task.Run(() => executable.Invoke([[1]], [], 0, budget));
        try
        {
            Assert.IsTrue(SpinWait.SpinUntil(() => budget.StepsConsumed > 100, TimeSpan.FromSeconds(5)));
        }
        finally
        {
            cancellation.Cancel();
        }
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await execution.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, budget.CurrentCallDepth);
    }

    [TestMethod]
    public void Partition_budget_can_reset_between_workers_but_not_during_execution()
    {
        CoreCLRJitKernel executable = CoreCLRJitKernel.Compile(NonterminatingKernel());
        var budget = new CoreCLRExecutionBudget(6, 1);
        Assert.AreEqual(0u, executable.Invoke([[0]], [], 0, budget));
        Assert.AreEqual(6L, budget.StepsConsumed);
        Assert.ThrowsExactly<CoreCLRResourceLimitException>(() => executable.Invoke([[0]], [], 0, budget));
        budget.Reset();
        Assert.AreEqual(0L, budget.StepsConsumed);
        Assert.AreEqual(0u, executable.Invoke([[0]], [], 0, budget));
        Assert.AreEqual(6L, budget.StepsConsumed);
    }

    [TestMethod]
    public async Task Active_budget_rejects_shared_worker_and_concurrent_reset()
    {
        CoreCLRJitKernel executable = CoreCLRJitKernel.Compile(NonterminatingKernel());
        using var cancellation = new CancellationTokenSource();
        var budget = new CoreCLRExecutionBudget(long.MaxValue, 16, cancellation.Token);
        Task<uint> execution = Task.Run(() => executable.Invoke([[1]], [], 0, budget));
        try
        {
            Assert.IsTrue(SpinWait.SpinUntil(() => budget.StepsConsumed > 100, TimeSpan.FromSeconds(5)));
            Assert.ThrowsExactly<InvalidOperationException>(() => executable.Invoke([[0]], [], 0, budget));
            Assert.ThrowsExactly<InvalidOperationException>(budget.Reset);
        }
        finally
        {
            cancellation.Cancel();
        }

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await execution.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, budget.CurrentCallDepth);
    }

    [TestMethod]
    public void One_collectible_executable_runs_concurrent_workers_without_shared_state()
    {
        MethodInfo source = typeof(TestKernels).GetMethod(nameof(TestKernels.Call))!;
        CoreCLRJitKernel executable = CoreCLRJitKernel.Compile(new WarpIntegerMapVerifier()
            .Verify(new WarpIntegerMapRequest(source, 1)).ControlFlow);
        uint[][] inputs = [Enumerable.Range(0, 4096).Select(index => unchecked((uint)(index * 786433))).ToArray()];
        var actual = new uint[inputs[0].Length];
        Parallel.For(0, actual.Length, index => actual[index] = executable.Invoke(inputs, [], index, NewBudget()));
        CollectionAssert.AreEqual(inputs[0].Select(TestKernels.Call).ToArray(), actual);
    }

    [TestMethod]
    public void Malformed_invocation_is_rejected_without_consuming_the_budget()
    {
        CoreCLRJitKernel executable = CoreCLRJitKernel.Compile(NonterminatingKernel());
        var budget = NewBudget();
        Assert.ThrowsExactly<ArgumentException>(() => executable.Invoke([], [], 0, budget));
        Assert.ThrowsExactly<ArgumentException>(() => executable.Invoke([[0]], [1], 0, budget));
        Assert.ThrowsExactly<ArgumentException>(() => executable.Invoke([null!], [], 0, budget));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => executable.Invoke([[0]], [], -1, budget));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => executable.Invoke([[0]], [], 1, budget));
        Assert.AreEqual(0L, budget.StepsConsumed);
    }

    [TestMethod]
    public void Oversized_entry_native_frame_is_rejected_before_compilation_or_execution()
    {
        WarpIrInstruction[] instructions = Enumerable.Range(0, 2000)
            .Select(index => new WarpIrInstruction(index, WarpIrOpCode.Constant, immediate: (uint)index))
            .ToArray();
        var kernel = new WarpControlFlowKernel("OversizedEntry", 1, 0,
            [new WarpBasicBlock(0, [], instructions, new WarpReturnTerminator(1999))]);
        CoreCLRCompilationResourceException fault = Assert.ThrowsExactly<CoreCLRCompilationResourceException>(
            () => CoreCLRJitKernel.Compile(kernel));
        Assert.AreEqual("OversizedEntry", fault.BodyName);
        Assert.AreEqual(CoreCLRJitKernel.MaximumAdmittedFrameBytes, fault.FrameLimitBytes);
        Assert.IsGreaterThan(fault.FrameLimitBytes, fault.EstimatedFrameBytes);
    }

    [TestMethod]
    public void Oversized_helper_native_frame_is_rejected_before_compilation_or_execution()
    {
        var helper = new WarpControlFlowFunction(0, "OversizedHelper", 1,
        [
            new WarpBasicBlock(0, [], Enumerable.Range(0, 2000)
                .Select(index => new WarpIrInstruction(index, WarpIrOpCode.Constant, immediate: (uint)index)),
                new WarpReturnTerminator(1999)),
        ]);
        var kernel = new WarpControlFlowKernel("SmallEntry", 1, 0,
        [
            new WarpBasicBlock(0, [],
            [
                new WarpIrInstruction(0, WarpIrOpCode.LoadInput),
                new WarpIrInstruction(1, WarpIrOpCode.Call, callee: 0, arguments: [0]),
            ], new WarpReturnTerminator(1)),
        ], functions: [helper]);
        CoreCLRCompilationResourceException fault = Assert.ThrowsExactly<CoreCLRCompilationResourceException>(
            () => CoreCLRJitKernel.Compile(kernel));
        Assert.AreEqual("OversizedHelper", fault.BodyName);
    }

    [TestMethod]
    public void Native_stack_probe_fault_is_recoverable_without_host_stack_overflow()
    {
        CoreCLRJitKernel executable = CoreCLRJitKernel.Compile(NonterminatingKernel());
        CoreCLRExecutionBudget budget = NewBudget();
        CoreCLRResourceLimitException? fault = null;
        Exception? unexpected = null;
        var worker = new Thread(() =>
        {
            try
            {
                fault = ReachNativeStackBoundary(executable, budget);
            }
            catch (Exception exception)
            {
                unexpected = exception;
            }
        }, maxStackSize: 512 * 1024);
        worker.Start();
        Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(10)));
        Assert.IsNull(unexpected);
        Assert.IsNotNull(fault);
        Assert.AreEqual(CoreCLRResourceLimitKind.StackExhausted, fault.Kind);
        Assert.AreEqual(0L, budget.StepsConsumed);
        Assert.AreEqual(0, budget.CurrentCallDepth);
        Assert.AreEqual(0u, executable.Invoke([[0]], [], 0, budget));
    }

    [TestMethod]
    public void Collectible_generated_assembly_is_not_rooted_by_a_static_jit_cache()
    {
        WeakReference assembly = CompileAndRelease();
        for (int attempt = 0; attempt < 20 && assembly.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.IsFalse(assembly.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CompileAndRelease()
    {
        CoreCLRJitKernel executable = CoreCLRJitKernel.Compile(NonterminatingKernel());
        Assert.AreEqual(0u, executable.Invoke([[0]], [], 0, NewBudget()));
        return new WeakReference(executable.CompiledEntryPoint.Module.Assembly);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static CoreCLRResourceLimitException ReachNativeStackBoundary(
        CoreCLRJitKernel executable,
        CoreCLRExecutionBudget budget)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            try
            {
                executable.Invoke([[0]], [], 0, budget);
                throw new InvalidOperationException("The worker failed to probe its native stack.");
            }
            catch (CoreCLRResourceLimitException fault)
            {
                return fault;
            }
        }

        CoreCLRResourceLimitException result = ReachNativeStackBoundary(executable, budget);
        GC.KeepAlive(budget);
        return result;
    }

    private static CoreCLRExecutionBudget NewBudget() => new(100000, 64);

    private static WarpControlFlowKernel NonterminatingKernel() => new("Nonterminating", 1, 0,
    [
        new WarpBasicBlock(0, [], [new WarpIrInstruction(0, WarpIrOpCode.LoadInput)],
            new WarpBranchTerminator(new WarpBranchTarget(1, [0]))),
        new WarpBasicBlock(1, [new WarpBlockParameter(1)],
        [
            new WarpIrInstruction(2, WarpIrOpCode.Constant),
            new WarpIrInstruction(3, WarpIrOpCode.Equal, 1, 2),
        ], new WarpConditionalBranchTerminator(3,
            new WarpBranchTarget(2, [1]), new WarpBranchTarget(1, [1]))),
        new WarpBasicBlock(2, [new WarpBlockParameter(4)], [], new WarpReturnTerminator(4)),
    ]);
}
