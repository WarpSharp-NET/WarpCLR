using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpBinary64ArithmeticTests
{
    private const ulong CanonicalNaN = 0x7FF8000000000000;
    private static readonly ulong[] Edges =
    [
        0, 1, 2, 3, 0x000FFFFFFFFFFFFF, 0x0010000000000000, 0x0010000000000001,
        0x3FEFFFFFFFFFFFFF, 0x3FF0000000000000, 0x3FF0000000000001,
        0x3CA0000000000000, 0x3CB0000000000000, 0x7FEFFFFFFFFFFFFF,
        0x7FF0000000000000, 0x7FF8000000000000, 0x7FF0000000000001,
    ];
    private static readonly ulong[] SignMasks = [0, 0x8000000000000000];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void VerifiedBinary64ArithmeticPreservesExactBitsAcrossQuanta(bool minimumQuantum)
    {
        IReadOnlyList<WarpLogicalMachineLayout> layouts = WarpPortableNumericKernels.CreateBinary64Arithmetic();
        CoreCLRResumableKernel[] compiled = layouts.Select(CoreCLRResumableKernel.Compile).ToArray();
        uint[][] inputs = [new uint[1], new uint[1], new uint[1], new uint[1]];
        uint[][] states = layouts.Select(layout => layout.CreateInitialState(16, 100000)).ToArray();
        foreach (ulong left in Edges)
        {
            foreach (ulong right in Edges)
            {
                foreach (ulong leftSign in SignMasks)
                {
                    foreach (ulong rightSign in SignMasks)
                    {
                        Check(left ^ leftSign, right ^ rightSign, compiled, inputs, states, minimumQuantum);
                    }
                }
            }
        }

        ulong random = 0x352064FF0355046A;
        for (int index = 0; index < 4096; index++)
        {
            ulong left = NextBits(ref random);
            Check(left, NextBits(ref random), compiled, inputs, states, minimumQuantum);
        }
    }

    [TestMethod]
    public void Binary64HasBothStorageWordsAndNeverLowersToNativeFloatingPoint()
    {
        IReadOnlyList<WarpLogicalMachineLayout> layouts = WarpPortableNumericKernels.CreateBinary64Arithmetic();
        Assert.HasCount(8, layouts);
        foreach (WarpLogicalMachineLayout layout in layouts)
        {
            Assert.AreEqual(4, layout.Kernel.InputBufferCount);
            StringAssert.Contains(layout.Kernel.Name, WarpPortableBinary64.Semantics, StringComparison.Ordinal);
            foreach (WarpBackendKind backend in WarpBackendCatalog.Required.Where(value => value != WarpBackendKind.CoreCLR))
            {
                string source = WarpPortableMachineEmitter.Emit(layout, backend);
                Assert.IsFalse(source.Contains("fadd", StringComparison.Ordinal));
                Assert.IsFalse(source.Contains("fsub", StringComparison.Ordinal));
                Assert.IsFalse(source.Contains("fmul", StringComparison.Ordinal));
                Assert.IsFalse(source.Contains("fdiv", StringComparison.Ordinal));
                Assert.IsFalse(source.Contains("fast ", StringComparison.Ordinal));
                StringAssert.Contains(source, "ptr addrspace(1) %warp_input_3", StringComparison.Ordinal);
            }
        }
    }

    private static void Check(ulong left, ulong right, CoreCLRResumableKernel[] compiled, uint[][] inputs, uint[][] states, bool minimumQuantum)
    {
        inputs[0][0] = unchecked((uint)left);
        inputs[1][0] = (uint)(left >> 32);
        inputs[2][0] = unchecked((uint)right);
        inputs[3][0] = (uint)(right >> 32);
        uint[] results = new uint[compiled.Length];
        for (int word = 0; word < compiled.Length; word++)
        {
            CoreCLRResumableKernel executable = compiled[word];
            uint[] state = states[word];
            executable.Layout.ResetState(state, 100000);
            int quantum = minimumQuantum ? executable.Layout.MaximumBlockCost : 100000;
            for (int iteration = 0; state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; iteration++)
            {
                Assert.IsLessThan(10000, iteration);
                executable.ExecuteQuantum(inputs, [], 0, state, 16, quantum);
            }

            Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
            results[word] = state[WarpLogicalMachineLayout.ResultOffset];
        }

        double leftValue = BitConverter.UInt64BitsToDouble(left);
        double rightValue = BitConverter.UInt64BitsToDouble(right);
        ulong sum = (ulong)results[1] << 32 | results[0];
        ulong difference = (ulong)results[3] << 32 | results[2];
        ulong product = (ulong)results[5] << 32 | results[4];
        ulong quotient = (ulong)results[7] << 32 | results[6];
        Assert.AreEqual(OracleBits(leftValue + rightValue), sum, $"Add {left:X16} {right:X16}.");
        Assert.AreEqual(OracleBits(leftValue - rightValue), difference, $"Subtract {left:X16} {right:X16}.");
        Assert.AreEqual(OracleBits(leftValue * rightValue), product, $"Multiply {left:X16} {right:X16}.");
        Assert.AreEqual(OracleBits(leftValue / rightValue), quotient, $"Divide {left:X16} {right:X16}.");
    }

    private static ulong OracleBits(double value) => double.IsNaN(value) ? CanonicalNaN : BitConverter.DoubleToUInt64Bits(value);

    private static ulong NextBits(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }
}
