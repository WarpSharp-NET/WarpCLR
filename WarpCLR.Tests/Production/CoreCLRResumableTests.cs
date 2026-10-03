using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class CoreCLRResumableTests
{
    [TestMethod]
    [DataRow(nameof(TestKernels.Grayscale), 1)]
    [DataRow(nameof(TestKernels.Combine), 2)]
    [DataRow(nameof(TestKernels.Scramble), 1)]
    [DataRow(nameof(TestKernels.Branch), 1)]
    [DataRow(nameof(TestKernels.CompareAndSelect), 1)]
    [DataRow(nameof(TestKernels.Loop), 1)]
    [DataRow(nameof(TestKernels.Call), 1)]
    public void Resumable_native_CLR_matches_source_across_quantum_sizes(string methodName, int inputCount)
    {
        MethodInfo source = typeof(TestKernels).GetMethod(methodName)!;
        WarpControlFlowKernel kernel = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(source, inputCount)).ControlFlow;
        var layout = new WarpLogicalMachineLayout(kernel);
        CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
        Assert.IsTrue(executable.IsCollectible);
        Assert.IsTrue(executable.CompiledEntryPoint.Module.Assembly.IsDynamic);
        Assert.AreNotEqual(IntPtr.Zero, executable.CompiledEntryPoint.MethodHandle.GetFunctionPointer());
        uint[][] inputs = inputCount == 1 ? [[0, 1, 7, 15, 31]] : [[0, 1, uint.MaxValue], [uint.MaxValue, 17, 31]];
        uint[] scalars = methodName switch
        {
            nameof(TestKernels.Combine) => [0xA5A5A5A5, 37],
            nameof(TestKernels.Scramble) => [63],
            nameof(TestKernels.CompareAndSelect) => [7],
            _ => [],
        };
        foreach (int worker in Enumerable.Range(0, inputs[0].Length))
        {
            uint[] baseline = Run(executable, inputs, scalars, worker, 64, 100000, layout.MaximumBlockCost);
            object[] originalArguments = inputs.Select(input => (object)input[worker])
                .Concat(scalars.Select(scalar => (object)scalar)).ToArray();
            Assert.AreEqual((uint)source.Invoke(null, originalArguments)!, baseline[WarpLogicalMachineLayout.ResultOffset]);
            foreach (int quantum in new[] { layout.MaximumBlockCost + 1, layout.MaximumBlockCost * 3, 65536 })
            {
                uint[] alternate = Run(executable, inputs, scalars, worker, 64, 100000, quantum);
                CollectionAssert.AreEqual(baseline, alternate);
            }
        }
    }

    [TestMethod]
    public void Nested_call_yield_preserves_explicit_frames_and_resumes_exactly()
    {
        var layout = new WarpLogicalMachineLayout(NestedCalls());
        CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(3, 9);
        executable.ExecuteQuantum([[11]], [], 0, state, 3, 3);
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(2u, state[WarpLogicalMachineLayout.DepthOffset]);
        int helperFrame = WarpLogicalMachineLayout.HeaderWords + layout.FrameWords;
        Assert.AreEqual(1u, state[helperFrame + WarpLogicalMachineLayout.FrameFunctionOffset]);
        Assert.AreEqual(11u, state[helperFrame + layout.ArgumentOffset]);
        executable.ExecuteQuantum([[11]], [], 0, state, 3, 3);
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(3u, state[WarpLogicalMachineLayout.DepthOffset]);
        int nestedFrame = helperFrame + layout.FrameWords;
        Assert.AreEqual(2u, state[nestedFrame + WarpLogicalMachineLayout.FrameFunctionOffset]);
        Assert.AreEqual(11u, state[nestedFrame + layout.ArgumentOffset]);
        executable.ExecuteQuantum([[11]], [], 0, state, 3, 3);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(11u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(1u, state[WarpLogicalMachineLayout.DepthOffset]);
        Assert.AreEqual(1UL, Remaining(state));
    }

    [TestMethod]
    public void Resumable_binary_operations_match_the_development_semantic_oracle()
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
        uint[][] inputs = [[0, 1, uint.MaxValue, 0x80000000, 17], [uint.MaxValue, 1, 1, 0x7FFFFFFF, 63]];
        foreach (WarpIrOpCode operation in operations)
        {
            var kernel = new WarpControlFlowKernel(operation.ToString(), 2, 0,
            [new WarpBasicBlock(0, [],
            [
                new WarpIrInstruction(0, WarpIrOpCode.LoadInput),
                new WarpIrInstruction(1, WarpIrOpCode.LoadInput, immediate: 1),
                new WarpIrInstruction(2, operation, 0, 1),
            ], new WarpReturnTerminator(2))]);
            CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(new WarpLogicalMachineLayout(kernel));
            uint[] expected = new WarpIntegerMapSemanticEmulator().Execute(new CoreCLRBackendCompiler().Compile(kernel), kernel, inputs);
            for (int worker = 0; worker < inputs[0].Length; worker++)
            {
                Assert.AreEqual(expected[worker], Run(executable, inputs, [], worker, 1, 4, 4)[WarpLogicalMachineLayout.ResultOffset]);
            }
        }
    }

    [TestMethod]
    public void Explicit_frame_backedge_phi_values_are_assigned_in_parallel()
    {
        var kernel = new WarpControlFlowKernel("Swap", 1, 0,
        [
            new WarpBasicBlock(0, [],
            [
                new WarpIrInstruction(0, WarpIrOpCode.Constant, immediate: 1),
                new WarpIrInstruction(1, WarpIrOpCode.Constant, immediate: 2),
                new WarpIrInstruction(2, WarpIrOpCode.Constant, immediate: 2),
            ], new WarpBranchTerminator(new WarpBranchTarget(1, [0, 1, 2]))),
            new WarpBasicBlock(1, [new WarpBlockParameter(3), new WarpBlockParameter(4), new WarpBlockParameter(5)],
            [
                new WarpIrInstruction(6, WarpIrOpCode.Constant, immediate: 1),
                new WarpIrInstruction(7, WarpIrOpCode.Subtract, 5, 6),
                new WarpIrInstruction(8, WarpIrOpCode.GreaterThanUnsigned, 5, 6),
            ], new WarpConditionalBranchTerminator(8, new WarpBranchTarget(1, [4, 3, 7]), new WarpBranchTarget(2, [3, 4]))),
            new WarpBasicBlock(2, [new WarpBlockParameter(9), new WarpBlockParameter(10)],
            [
                new WarpIrInstruction(11, WarpIrOpCode.Constant, immediate: 100),
                new WarpIrInstruction(12, WarpIrOpCode.Multiply, 9, 11),
                new WarpIrInstruction(13, WarpIrOpCode.Add, 12, 10),
            ], new WarpReturnTerminator(13)),
        ]);
        CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(new WarpLogicalMachineLayout(kernel));
        uint[] state = Run(executable, [[0]], [], 0, 1, 16, 4);
        Assert.AreEqual(201u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(0UL, Remaining(state));
        CollectionAssert.AreEqual(state, Run(executable, [[0]], [], 0, 1, 16, 16));
    }

    [TestMethod]
    public void Resumable_select_treats_every_nonzero_condition_as_true()
    {
        var kernel = new WarpControlFlowKernel("Select", 1, 0,
        [new WarpBasicBlock(0, [],
        [
            new WarpIrInstruction(0, WarpIrOpCode.LoadInput),
            new WarpIrInstruction(1, WarpIrOpCode.Constant, immediate: 0xDEADBEEF),
            new WarpIrInstruction(2, WarpIrOpCode.Constant, immediate: 0x12345678),
            new WarpIrInstruction(3, WarpIrOpCode.Select, 0, 1, third: 2),
        ], new WarpReturnTerminator(3))]);
        CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(new WarpLogicalMachineLayout(kernel));
        uint[][] inputs = [[0, 1, 0x80000000, uint.MaxValue]];
        for (int worker = 0; worker < inputs[0].Length; worker++)
        {
            Assert.AreEqual(worker == 0 ? 0x12345678u : 0xDEADBEEFu,
                Run(executable, inputs, [], worker, 1, 5, 5)[WarpLogicalMachineLayout.ResultOffset]);
        }
    }

    [TestMethod]
    public void Logical_call_depth_fault_is_quantum_independent_and_has_exact_location()
    {
        var layout = new WarpLogicalMachineLayout(NestedCalls());
        CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
        uint[] smallQuantum = Run(executable, [[17]], [], 0, 2, 100, 3);
        uint[] largeQuantum = Run(executable, [[17]], [], 0, 2, 100, 100);
        CollectionAssert.AreEqual(smallQuantum, largeQuantum);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, smallQuantum[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(WarpLogicalMachineLayout.CallDepthFault, smallQuantum[WarpLogicalMachineLayout.FaultKindOffset]);
        Assert.AreEqual(1u, smallQuantum[WarpLogicalMachineLayout.FaultFunctionOffset]);
        Assert.AreEqual(0u, smallQuantum[WarpLogicalMachineLayout.FaultBlockOffset]);
        Assert.AreEqual(2u, smallQuantum[WarpLogicalMachineLayout.DepthOffset]);
        Assert.AreEqual(94UL, Remaining(smallQuantum));
    }

    [TestMethod]
    public void Infinite_loop_yields_and_then_reports_deterministic_step_fault()
    {
        var layout = new WarpLogicalMachineLayout(InfiniteLoop());
        CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(1, 10);
        executable.ExecuteQuantum([[1]], [], 0, state, 1, 3);
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(8UL, Remaining(state));
        executable.ExecuteQuantum([[1]], [], 0, state, 1, 3);
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(5UL, Remaining(state));
        executable.ExecuteQuantum([[1]], [], 0, state, 1, 3);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(2UL, Remaining(state));
        Assert.AreEqual(WarpLogicalMachineLayout.StepLimitFault, state[WarpLogicalMachineLayout.FaultKindOffset]);
        Assert.AreEqual(1u, state[WarpLogicalMachineLayout.FaultBlockOffset]);
        CollectionAssert.AreEqual(state, Run(executable, [[1]], [], 0, 1, 10, 100));
    }

    [TestMethod]
    public void Total_step_fault_takes_precedence_over_exhausted_quantum()
    {
        var layout = new WarpLogicalMachineLayout(InfiniteLoop());
        uint[] state = layout.CreateInitialState(1, 2);
        CoreCLRResumableKernel.Compile(layout).ExecuteQuantum([[1]], [], 0, state, 1, 3);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(0UL, Remaining(state));
    }

    [TestMethod]
    public void Remaining_step_high_word_is_preserved_across_quanta()
    {
        var layout = new WarpLogicalMachineLayout(InfiniteLoop());
        uint[] state = layout.CreateInitialState(1, (1L << 32) + 1);
        CoreCLRResumableKernel.Compile(layout).ExecuteQuantum([[1]], [], 0, state, 1, 3);
        Assert.AreEqual((1UL << 32) - 1, Remaining(state));
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.RemainingStepsHighOffset]);
        Assert.AreEqual(uint.MaxValue, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
    }

    [TestMethod]
    public void Precancelled_quantum_preserves_all_state_words()
    {
        var layout = new WarpLogicalMachineLayout(InfiniteLoop());
        uint[] state = layout.CreateInitialState(1, 100);
        uint[] before = state.ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => CoreCLRResumableKernel.Compile(layout)
            .ExecuteQuantum([[1]], [], 0, state, 1, 3, cancellation.Token));
        CollectionAssert.AreEqual(before, state);
    }

    [TestMethod]
    public void Cancellation_between_quanta_preserves_previous_continuation()
    {
        var layout = new WarpLogicalMachineLayout(InfiniteLoop());
        CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(1, 100);
        using var cancellation = new CancellationTokenSource();
        executable.ExecuteQuantum([[1]], [], 0, state, 1, 3, cancellation.Token);
        uint[] before = state.ToArray();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => executable.ExecuteQuantum([[1]], [], 0, state, 1, 3, cancellation.Token));
        CollectionAssert.AreEqual(before, state);
    }

    [TestMethod]
    public void Invalid_header_frame_or_subminimum_quantum_is_rejected_without_mutation()
    {
        var layout = new WarpLogicalMachineLayout(InfiniteLoop());
        CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
        uint[] original = layout.CreateInitialState(1, 100);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => executable.ExecuteQuantum([[0]], [], 0, original, 1, 2));
        foreach (int offset in new[] { WarpLogicalMachineLayout.StatusOffset, WarpLogicalMachineLayout.DepthOffset,
            WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameProgramCounterOffset,
            WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameFunctionOffset })
        {
            uint[] state = original.ToArray();
            state[offset] = uint.MaxValue;
            uint[] before = state.ToArray();
            Assert.ThrowsExactly<ArgumentException>(() => executable.ExecuteQuantum([[0]], [], 0, state, 1, 3));
            CollectionAssert.AreEqual(before, state);
        }
    }

    [TestMethod]
    public void Completed_and_faulted_states_are_terminal()
    {
        var layout = new WarpLogicalMachineLayout(InfiniteLoop());
        CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
        foreach (uint[] state in new[] { Run(executable, [[0]], [], 0, 1, 100, 3), Run(executable, [[1]], [], 0, 1, 10, 3) })
        {
            uint[] before = state.ToArray();
            executable.ExecuteQuantum([[1]], [], 0, state, 1, 3);
            CollectionAssert.AreEqual(before, state);
        }
    }

    [TestMethod]
    public void Large_SSA_body_uses_explicit_words_instead_of_an_unbounded_native_frame()
    {
        var kernel = new WarpControlFlowKernel("LargeSSA", 1, 0,
        [new WarpBasicBlock(0, [], Enumerable.Range(0, 2000)
            .Select(index => new WarpIrInstruction(index, WarpIrOpCode.Constant, immediate: (uint)index)),
            new WarpReturnTerminator(1999))]);
        var layout = new WarpLogicalMachineLayout(kernel);
        CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
        uint[] state = Run(executable, [[0]], [], 0, 1, 2001, 2001);
        Assert.AreEqual(1999u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(0UL, Remaining(state));
    }

    [TestMethod]
    public void Compiled_quantum_supports_concurrent_independent_logical_workers()
    {
        MethodInfo source = typeof(TestKernels).GetMethod(nameof(TestKernels.Call))!;
        var layout = new WarpLogicalMachineLayout(new WarpIntegerMapVerifier()
            .Verify(new WarpIntegerMapRequest(source, 1)).ControlFlow);
        CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
        uint[][] inputs = [Enumerable.Range(0, 4096).Select(index => unchecked((uint)(index * 786433))).ToArray()];
        var actual = new uint[inputs[0].Length];
        Parallel.For(0, actual.Length, worker => actual[worker] =
            Run(executable, inputs, [], worker, 64, 100000, layout.MaximumBlockCost)[WarpLogicalMachineLayout.ResultOffset]);
        CollectionAssert.AreEqual(inputs[0].Select(TestKernels.Call).ToArray(), actual);
    }

    private static uint[] Run(CoreCLRResumableKernel executable, uint[][] inputs, uint[] scalars,
        int worker, int depth, long steps, int quantum)
    {
        uint[] state = executable.Layout.CreateInitialState(depth, steps);
        int invocationCount = 0;
        do
        {
            Assert.IsLessThan(100000, invocationCount++);
            executable.ExecuteQuantum(inputs, scalars, worker, state, depth, quantum);
        }
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable);

        return state;
    }

    private static ulong Remaining(uint[] state) => state[WarpLogicalMachineLayout.RemainingStepsLowOffset] |
        ((ulong)state[WarpLogicalMachineLayout.RemainingStepsHighOffset] << 32);

    private static WarpControlFlowKernel NestedCalls() => new("Nested", 1, 0,
    [new WarpBasicBlock(0, [],
        [new WarpIrInstruction(0, WarpIrOpCode.LoadInput), new WarpIrInstruction(1, WarpIrOpCode.Call, callee: 0, arguments: [0])],
        new WarpReturnTerminator(1))], functions:
    [
        new WarpControlFlowFunction(0, "Outer", 1,
        [new WarpBasicBlock(0, [],
            [new WarpIrInstruction(0, WarpIrOpCode.LoadArgument), new WarpIrInstruction(1, WarpIrOpCode.Call, callee: 1, arguments: [0])],
            new WarpReturnTerminator(1))]),
        new WarpControlFlowFunction(1, "Inner", 1,
        [new WarpBasicBlock(0, [], [new WarpIrInstruction(0, WarpIrOpCode.LoadArgument)], new WarpReturnTerminator(0))]),
    ]);

    private static WarpControlFlowKernel InfiniteLoop() => new("Nonterminating", 1, 0,
    [
        new WarpBasicBlock(0, [], [new WarpIrInstruction(0, WarpIrOpCode.LoadInput)], new WarpBranchTerminator(new WarpBranchTarget(1, [0]))),
        new WarpBasicBlock(1, [new WarpBlockParameter(1)],
            [new WarpIrInstruction(2, WarpIrOpCode.Constant), new WarpIrInstruction(3, WarpIrOpCode.Equal, 1, 2)],
            new WarpConditionalBranchTerminator(3, new WarpBranchTarget(2, [1]), new WarpBranchTarget(1, [1]))),
        new WarpBasicBlock(2, [new WarpBlockParameter(4)], [], new WarpReturnTerminator(4)),
    ]);
}
