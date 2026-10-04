using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpFloatingPointSourceTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void TypedCilPipelinePreservesDeclaredPrecisionAndOperationRounding(bool wide, bool minimumQuantum)
    {
        string name = wide ? nameof(WarpFloatingPointSourceKernels.Pipeline64) : nameof(WarpFloatingPointSourceKernels.Pipeline32);
        WarpFloatingPointMapPlan plan = WarpFloatingPointMapLowerer.Lower(Source(name));
        Assert.AreEqual(wide ? typeof(double) : typeof(float), plan.SourceType);
        Assert.AreEqual(4, plan.InputValueCount);
        Assert.AreEqual(wide ? 2 : 1, plan.StorageWordsPerValue);
        Assert.HasCount(plan.StorageWordsPerValue, plan.Results);
        CoreCLRResumableKernel[] compiled = plan.Results.Select(CoreCLRResumableKernel.Compile).ToArray();
        uint[][] inputs = Enumerable.Range(0, 4 * plan.StorageWordsPerValue).Select(_ => new uint[1]).ToArray();
        uint[][] states = plan.Results.Select(layout => layout.CreateInitialState(16, 100000)).ToArray();
        ulong random = 0x352064FF0355046A;
        for (int index = 0; index < 2048; index++)
        {
            ulong[] values = Enumerable.Range(0, 4).Select(_ => wide ? NextBits(ref random) : (uint)NextBits(ref random)).ToArray();
            ulong actual = Execute(compiled, inputs, states, values, wide, minimumQuantum);
            ulong expected = wide ? Oracle64(values) : Oracle32(values);
            Assert.AreEqual(expected, actual, $"Typed {name}: {string.Join(",", values.Select(value => value.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)))}.");
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RawTransportPreservesEveryBitIncludingNaNPayloadsAndSignedZero(bool wide)
    {
        string name = wide ? nameof(WarpFloatingPointSourceKernels.Transport64) : nameof(WarpFloatingPointSourceKernels.Transport32);
        WarpFloatingPointMapPlan plan = WarpFloatingPointMapLowerer.Lower(Source(name));
        CoreCLRResumableKernel[] compiled = plan.Results.Select(CoreCLRResumableKernel.Compile).ToArray();
        uint[][] inputs = Enumerable.Range(0, plan.StorageWordsPerValue).Select(_ => new uint[1]).ToArray();
        uint[][] states = plan.Results.Select(layout => layout.CreateInitialState(16, 100000)).ToArray();
        ulong[] bits = wide ?
            [0, 0x8000000000000000, 1, 0x7FF0000000000001, 0x7FF8123456789ABC, 0xFFF0000000000001, 0xFFFABCDEF0123456, ulong.MaxValue] :
            [0, 0x80000000, 1, 0x7F800001, 0x7FC12345, 0xFF800001, 0xFFC54321, uint.MaxValue];
        foreach (ulong value in bits)
        {
            Assert.AreEqual(value, Execute(compiled, inputs, states, [value], wide, minimumQuantum: true));
        }
    }

    [TestMethod]
    public void ExplicitSingleNarrowingIsRejectedInsteadOfSilentlyAdmittedAsDouble()
    {
        MethodInfo method = typeof(WarpFloatingPointTestKernels).GetMethod(nameof(WarpFloatingPointTestKernels.Narrow))!;
        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(() => WarpFloatingPointMapLowerer.Lower(method));
        Assert.AreEqual("WRPNUM1000", exception.Code, StringComparer.Ordinal);
        StringAssert.Contains(exception.Message, "no alternate precision", StringComparison.Ordinal);
    }

    [TestMethod]
    public void PublicIntegerProfileStillRejectsFloatingPointSourceSignatures()
    {
        foreach (string name in new[] { nameof(WarpFloatingPointSourceKernels.Pipeline32), nameof(WarpFloatingPointSourceKernels.Pipeline64) })
        {
            WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(() =>
                new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(Source(name), 4)));
            Assert.AreEqual("WRPCIL1000", exception.Code, StringComparer.Ordinal);
        }
    }

    [TestMethod]
    public void DoubleConstantAndIntermediateKeepBitsThatSingleCannotRepresent()
    {
        MethodInfo method = typeof(WarpFloatingPointTestKernels).GetMethod(nameof(WarpFloatingPointTestKernels.Constant))!;
        WarpFloatingPointMapPlan plan = WarpFloatingPointMapLowerer.Lower(method);
        CoreCLRResumableKernel[] compiled = plan.Results.Select(CoreCLRResumableKernel.Compile).ToArray();
        uint[][] inputs = [new uint[1], new uint[1]];
        uint[][] states = plan.Results.Select(layout => layout.CreateInitialState(16, 100000)).ToArray();
        ulong value = BitConverter.DoubleToUInt64Bits(1.0);
        ulong actual = Execute(compiled, inputs, states, [value], wide: true, minimumQuantum: true);
        Assert.AreEqual(BitConverter.DoubleToUInt64Bits(WarpFloatingPointTestKernels.Constant(1.0)), actual);
        Assert.AreNotEqual(BitConverter.DoubleToUInt64Bits((double)(float)0.1), actual);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MultiplyThenAddHasTwoRoundingPointsAndNeverContracts(bool wide)
    {
        string name = wide ? nameof(WarpFloatingPointTestKernels.Round64) : nameof(WarpFloatingPointTestKernels.Round32);
        MethodInfo method = typeof(WarpFloatingPointTestKernels).GetMethod(name)!;
        WarpFloatingPointMapPlan plan = WarpFloatingPointMapLowerer.Lower(method);
        CoreCLRResumableKernel[] compiled = plan.Results.Select(CoreCLRResumableKernel.Compile).ToArray();
        uint[][] inputs = Enumerable.Range(0, 3 * plan.StorageWordsPerValue).Select(_ => new uint[1]).ToArray();
        uint[][] states = plan.Results.Select(layout => layout.CreateInitialState(16, 100000)).ToArray();
        double delta = wide ? Math.ScaleB(1.0, -27) : Math.ScaleB(1.0, -13);
        ulong[] values = wide ?
            [BitConverter.DoubleToUInt64Bits(1 + delta), BitConverter.DoubleToUInt64Bits(1 - delta), BitConverter.DoubleToUInt64Bits(-1)] :
            [BitConverter.SingleToUInt32Bits((float)(1 + delta)), BitConverter.SingleToUInt32Bits((float)(1 - delta)), BitConverter.SingleToUInt32Bits(-1)];
        ulong actual = Execute(compiled, inputs, states, values, wide, minimumQuantum: true);
        Assert.AreEqual(0UL, actual);
        ulong fused = wide ? BitConverter.DoubleToUInt64Bits(Math.FusedMultiplyAdd(1 + delta, 1 - delta, -1)) :
            BitConverter.SingleToUInt32Bits(MathF.FusedMultiplyAdd((float)(1 + delta), (float)(1 - delta), -1));
        Assert.AreNotEqual(fused, actual);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TypedLocalsAndArgumentAssignmentsRetainTheDeclaredPrecision(bool wide)
    {
        string name = wide ? nameof(WarpFloatingPointTestKernels.Local64) : nameof(WarpFloatingPointTestKernels.Local32);
        MethodInfo method = typeof(WarpFloatingPointTestKernels).GetMethod(name)!;
        WarpFloatingPointMapPlan plan = WarpFloatingPointMapLowerer.Lower(method);
        CoreCLRResumableKernel[] compiled = plan.Results.Select(CoreCLRResumableKernel.Compile).ToArray();
        uint[][] inputs = Enumerable.Range(0, 2 * plan.StorageWordsPerValue).Select(_ => new uint[1]).ToArray();
        uint[][] states = plan.Results.Select(layout => layout.CreateInitialState(16, 100000)).ToArray();
        ulong[] values = wide ? [BitConverter.DoubleToUInt64Bits(1), BitConverter.DoubleToUInt64Bits(Math.ScaleB(1, -30))] :
            [BitConverter.SingleToUInt32Bits(1), BitConverter.SingleToUInt32Bits(MathF.ScaleB(1, -15))];
        ulong actual = Execute(compiled, inputs, states, values, wide, minimumQuantum: true);
        ulong expected = wide ? BitConverter.DoubleToUInt64Bits(WarpFloatingPointTestKernels.Local64(1, Math.ScaleB(1, -30))) :
            BitConverter.SingleToUInt32Bits(WarpFloatingPointTestKernels.Local32(1, MathF.ScaleB(1, -15)));
        Assert.AreEqual(expected, actual);
    }

    private static MethodInfo Source(string name) => typeof(WarpFloatingPointSourceKernels).GetMethod(name)!;

    private static ulong Execute(CoreCLRResumableKernel[] compiled, uint[][] inputs, uint[][] states, ulong[] values, bool wide, bool minimumQuantum)
    {
        for (int index = 0; index < values.Length; index++)
        {
            inputs[index * (wide ? 2 : 1)][0] = unchecked((uint)values[index]);
            if (wide)
            {
                inputs[index * 2 + 1][0] = (uint)(values[index] >> 32);
            }
        }

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

        return wide ? (ulong)results[1] << 32 | results[0] : results[0];
    }

    private static ulong Oracle64(ulong[] values)
    {
        double value = WarpFloatingPointSourceKernels.Pipeline64(BitConverter.UInt64BitsToDouble(values[0]),
            BitConverter.UInt64BitsToDouble(values[1]), BitConverter.UInt64BitsToDouble(values[2]), BitConverter.UInt64BitsToDouble(values[3]));
        return double.IsNaN(value) ? 0x7FF8000000000000 : BitConverter.DoubleToUInt64Bits(value);
    }

    private static ulong Oracle32(ulong[] values)
    {
        float value = WarpFloatingPointSourceKernels.Pipeline32(BitConverter.UInt32BitsToSingle((uint)values[0]),
            BitConverter.UInt32BitsToSingle((uint)values[1]), BitConverter.UInt32BitsToSingle((uint)values[2]), BitConverter.UInt32BitsToSingle((uint)values[3]));
        return float.IsNaN(value) ? 0x7FC00000u : BitConverter.SingleToUInt32Bits(value);
    }

    private static ulong NextBits(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }
}
