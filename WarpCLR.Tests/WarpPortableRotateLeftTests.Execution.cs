using WarpCLR.Tests;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableRotateLeftTests
{
    [TestMethod]
    [DataRow(nameof(WarpPortableRotateLeftSourceKernels.Rotate32), false)]
    [DataRow(nameof(WarpPortableRotateLeftSourceKernels.Rotate64), true)]
    [DataRow(nameof(WarpPortableRotateLeftSourceKernels.Transport32), false)]
    [DataRow(nameof(WarpPortableRotateLeftSourceKernels.Transport64), true)]
    public void CapturedCilUsesExactRawWordResultsAndIdenticalWholeBanksAtBothQuanta(string name, bool wide)
    {
        WarpPortableWordLoweredProgram program = Capture(name);
        WarpPortableWordBody body = program.Bodies.RequireExactlyOne(item => string.Equals(item.MethodIdentity, WarpPortableMethodGraphIdentity.Method(Source(name)), StringComparison.Ordinal));
        Assert.AreEqual(wide ? 64 : 32, body.Arguments[0].Type.StorageBits);
        Assert.AreEqual(WarpPortableMethodGraphIdentity.Type(typeof(int)), body.Arguments[1].Type.Identity, StringComparer.Ordinal);
        Assert.AreEqual(32, body.Arguments[1].Type.StorageBits);
        Assert.IsTrue(body.Arguments[1].Type.IsSigned);
        var layout = new WarpLogicalMachineLayout(program.Kernel);
        Assert.AreEqual(wide ? 2 : 1, layout.ResultWordCount);
        Assert.AreEqual(wide ? 3 : 2, layout.Kernel.InputBufferCount);
        Assert.AreEqual(0, layout.Kernel.ScalarArgumentCount);
        Assert.IsNull(layout.Kernel.Execution!.PrivateControllerProjection);
        Assert.IsTrue(layout.Kernel.Execution.Bodies.Where(metadata => metadata.RuntimeHelper)
            .All(metadata => !metadata.CountsSourceDepth && metadata.SourceBlockCosts.All(cost => cost == 0)));
        int budget = body.SourceBlocks.Length;
        uint[] initial = layout.CreateInitialState(32, budget);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        int large = Math.Max(4096, checked(layout.MaximumBlockCost + 1));
        foreach (uint[] words in WarpPortableRotateLeftOracle.Words(wide))
        {
            foreach (int count in WarpPortableRotateLeftOracle.Counts(wide ? 64 : 32))
            {
                uint[] expected = WarpPortableRotateLeftOracle.Permute(words, count);
                uint[] arguments = [.. words, unchecked((uint)count)];
                uint[] small = Run(compiled, initial, arguments, layout.MaximumBlockCost);
                uint[] big = Run(compiled, initial, arguments, large);
                CollectionAssert.AreEqual(small, big);
                uint[] result = Enumerable.Range(0, layout.ResultWordCount).Select(word => small[layout.GetResultWordOffset(word, 32)]).ToArray();
                CollectionAssert.AreEqual(expected, result);
                Assert.AreEqual(0u, small[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
                Assert.AreEqual(0u, small[WarpLogicalMachineLayout.RemainingStepsHighOffset]);
            }
        }
    }

    [TestMethod]
    [DataRow(nameof(WarpPortableRotateLeftSourceKernels.Rotate32), false)]
    [DataRow(nameof(WarpPortableRotateLeftSourceKernels.Rotate64), true)]
    public void ExactSourceBudgetCompletesAndOneShortBudgetFaultsBeforeTheOriginalReturn(string name, bool wide)
    {
        WarpPortableWordLoweredProgram program = Capture(name);
        WarpPortableWordBody body = program.Bodies.RequireExactlyOne(item => string.Equals(item.MethodIdentity, WarpPortableMethodGraphIdentity.Method(Source(name)), StringComparison.Ordinal));
        var layout = new WarpLogicalMachineLayout(program.Kernel);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] arguments = wide ? [1, 0x80000000, uint.MaxValue] : [0x80000001, uint.MaxValue];
        foreach (int quantum in new[] { layout.MaximumBlockCost, Math.Max(4096, checked(layout.MaximumBlockCost + 1)) })
        {
            uint[] complete = Run(compiled, layout.CreateInitialState(32, body.SourceBlocks.Length), arguments, quantum);
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, complete[WarpLogicalMachineLayout.StatusOffset]);
            uint[] state = layout.CreateInitialState(32, body.SourceBlocks.Length - 1);
            ExecuteToStop(compiled, arguments, state, quantum);
            Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(WarpLogicalMachineLayout.StepLimitFault, state[WarpLogicalMachineLayout.FaultKindOffset]);
            Assert.AreEqual((uint)body.Function, state[WarpLogicalMachineLayout.FaultFunctionOffset]);
            WarpPortableWordSourceBlock last = body.SourceBlocks.RequireExactlyOne(block => block.Instruction.OpCode == System.Reflection.Emit.OpCodes.Ret.Value);
            Assert.AreEqual((uint)last.Block, state[WarpLogicalMachineLayout.FaultBlockOffset]);
        }
    }

    [TestMethod]
    public void IndependentWitnessesCoverZeroCountSignBitHalfSwapAndAllBits()
    {
        CollectionAssert.AreEqual(new uint[] { 1 }, WarpPortableRotateLeftOracle.Permute([0x80000000], 1));
        CollectionAssert.AreEqual(new uint[] { 0x40000000 }, WarpPortableRotateLeftOracle.Permute([0x80000000], -1));
        CollectionAssert.AreEqual(new uint[] { 0x7F800001 }, WarpPortableRotateLeftOracle.Permute([0x7F800001], 32));
        CollectionAssert.AreEqual(new uint[] { 1, 0x7FF00000 }, WarpPortableRotateLeftOracle.Permute([1, 0x7FF00000], int.MinValue));
        CollectionAssert.AreEqual(new uint[] { 0x80000000, 1 }, WarpPortableRotateLeftOracle.Permute([1, 0x80000000], 32));
        CollectionAssert.AreEqual(new uint[] { 0, 0xC0000000 }, WarpPortableRotateLeftOracle.Permute([1, 0x80000000], -1));
        CollectionAssert.AreEqual(new uint[] { uint.MaxValue, uint.MaxValue }, WarpPortableRotateLeftOracle.Permute([uint.MaxValue, uint.MaxValue], int.MaxValue));
    }

    private static uint[] Run(CoreCLRResumableKernel compiled, uint[] initial, uint[] arguments, int quantum)
    {
        uint[] state = (uint[])initial.Clone();
        ExecuteToStop(compiled, arguments, state, quantum);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return state;
    }

    private static void ExecuteToStop(CoreCLRResumableKernel compiled, uint[] arguments, uint[] state, int quantum)
    {
        uint[][] inputs = arguments.Select(word => new uint[] { word, 0xDEADBEEF, 0x7F800001 }).ToArray();
        uint[][] originalInputs = inputs.Select(words => (uint[])words.Clone()).ToArray();
        uint[] arena = [0xDEADBEEF, 0x80000000, 0x7F800001, 0xFFF00001, uint.MaxValue, 0];
        uint[] originalArena = (uint[])arena.Clone();
        for (int step = 0; step < 10000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; step++)
        {
            compiled.ExecuteManagedQuantum(inputs, [], 0, state, 32, quantum, arena, CancellationToken.None);
            for (int index = 0; index < inputs.Length; index++) { CollectionAssert.AreEqual(originalInputs[index], inputs[index]); }
            CollectionAssert.AreEqual(originalArena, arena);
        }
        Assert.AreNotEqual(WarpLogicalMachineLayout.Runnable, state[WarpLogicalMachineLayout.StatusOffset]);
    }
}
