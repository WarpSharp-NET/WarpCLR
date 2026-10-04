using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpBinary32ArithmeticTests
{
    private static readonly uint[] Edges =
    [
        0, 1, 2, 3, 0x007FFFFF, 0x00800000, 0x00800001, 0x3F7FFFFF,
        0x3F800000, 0x3F800001, 0x33800000, 0x34000000, 0x7F7FFFFF,
        0x7F800000, 0x7FC00000, 0x7F800001,
    ];
    private static readonly uint[] SignMasks = [0, 0x80000000];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void VerifiedBinary32ArithmeticPreservesExactBitsAcrossQuanta(bool minimumQuantum)
    {
        IReadOnlyList<WarpLogicalMachineLayout> layouts = WarpPortableNumericKernels.CreateBinary32Arithmetic();
        CoreCLRResumableKernel[] compiled = layouts.Select(CoreCLRResumableKernel.Compile).ToArray();
        uint[][] inputs = [new uint[1], new uint[1]];
        uint[][] states = layouts.Select(layout => layout.CreateInitialState(16, 100000)).ToArray();
        foreach (uint left in Edges)
        {
            foreach (uint right in Edges)
            {
                foreach (uint leftSign in SignMasks)
                {
                    foreach (uint rightSign in SignMasks)
                    {
                        Check(left ^ leftSign, right ^ rightSign, compiled, inputs, states, minimumQuantum);
                    }
                }
            }
        }

        uint random = 0x352064FF;
        for (int index = 0; index < 4096; index++)
        {
            uint left = NextBits(ref random);
            Check(left, NextBits(ref random), compiled, inputs, states, minimumQuantum);
        }
    }

    [TestMethod]
    public void Binary32PreservesStorageAndNeverLowersToNativeFloatingPoint()
    {
        IReadOnlyList<WarpLogicalMachineLayout> layouts = WarpPortableNumericKernels.CreateBinary32Arithmetic();
        Assert.HasCount(4, layouts);
        foreach (WarpLogicalMachineLayout layout in layouts)
        {
            Assert.AreEqual(2, layout.Kernel.InputBufferCount);
            StringAssert.Contains(layout.Kernel.Name, WarpPortableBinary32.Semantics, StringComparison.Ordinal);
            foreach (WarpBackendKind backend in WarpBackendCatalog.Required.Where(value => value != WarpBackendKind.CoreCLR))
            {
                string source = WarpPortableMachineEmitter.Emit(layout, backend);
                Assert.IsFalse(source.Contains("fadd", StringComparison.Ordinal));
                Assert.IsFalse(source.Contains("fsub", StringComparison.Ordinal));
                Assert.IsFalse(source.Contains("fmul", StringComparison.Ordinal));
                Assert.IsFalse(source.Contains("fdiv", StringComparison.Ordinal));
                Assert.IsFalse(source.Contains("fast ", StringComparison.Ordinal));
                StringAssert.Contains(source, "ptr addrspace(1) %warp_input_1", StringComparison.Ordinal);
            }
        }
    }

    private static void Check(uint left, uint right, CoreCLRResumableKernel[] compiled, uint[][] inputs, uint[][] states, bool minimumQuantum)
    {
        inputs[0][0] = left;
        inputs[1][0] = right;
        uint[] results = new uint[compiled.Length];
        for (int operation = 0; operation < compiled.Length; operation++)
        {
            CoreCLRResumableKernel executable = compiled[operation];
            uint[] state = states[operation];
            executable.Layout.ResetState(state, 100000);
            int quantum = minimumQuantum ? executable.Layout.MaximumBlockCost : 100000;
            for (int iteration = 0; state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; iteration++)
            {
                Assert.IsLessThan(10000, iteration);
                executable.ExecuteQuantum(inputs, [], 0, state, 16, quantum);
            }

            Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
            results[operation] = state[WarpLogicalMachineLayout.ResultOffset];
        }

        float leftValue = BitConverter.UInt32BitsToSingle(left);
        float rightValue = BitConverter.UInt32BitsToSingle(right);
        Assert.AreEqual(OracleBits(leftValue + rightValue), results[0], $"Add {left:X8} {right:X8}.");
        Assert.AreEqual(OracleBits(leftValue - rightValue), results[1], $"Subtract {left:X8} {right:X8}.");
        Assert.AreEqual(OracleBits(leftValue * rightValue), results[2], $"Multiply {left:X8} {right:X8}.");
        Assert.AreEqual(OracleBits(leftValue / rightValue), results[3], $"Divide {left:X8} {right:X8}.");
    }

    private static uint OracleBits(float value) => float.IsNaN(value) ? 0x7FC00000u : BitConverter.SingleToUInt32Bits(value);

    private static uint NextBits(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
}
