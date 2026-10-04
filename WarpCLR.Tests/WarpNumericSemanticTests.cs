using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpNumericSemanticTests
{
    private static readonly ulong[] Edges =
    [
        0, 1, 2, 3, 0x007FFFFF, 0x00800000, 0x00800001, 0x3F7FFFFF,
        0x3F800000, 0x3F800001, 0x7F7FFFFF, 0x7F800000, 0x7FC00000, 0x7F800001,
        0x7FFFFFFF, 0x80000000, 0xFFFFFFFF, 0x100000000,
        0x000FFFFFFFFFFFFF, 0x0010000000000000, 0x0010000000000001,
        0x3FEFFFFFFFFFFFFF, 0x3FF0000000000000, 0x3FF0000000000001,
        0x3FF0000010000000, 0x3FF0000030000000, 0x3690000000000000,
        0x36A0000000000000, 0x380FFFFFC0000000, 0x3810000000000000,
        0x41DFFFFFFFC00000, 0x41E0000000000000, 0x41EFFFFFFFE00000, 0x41F0000000000000,
        0x43DFFFFFFFFFFFFF, 0x43E0000000000000, 0x43EFFFFFFFFFFFFF, 0x43F0000000000000,
        0x7FEFFFFFFFFFFFFF, 0x7FF0000000000000, 0x7FF8000000000000,
        0x7FF0000000000001, 0x7FFFFFFFFFFFFFFF, 0x8000000000000000, 0xFFFFFFFFFFFFFFFF,
    ];
    private static readonly ulong[] SignMasks = [0, 0x8000000000000000, 0x80000000];

    [TestMethod]
    public void ComparisonsAndMinMaxPreserveOrderedNaNSignedZeroSemantics()
    {
        foreach (ulong left in Edges)
        {
            foreach (ulong right in Edges)
            {
                foreach (ulong sign in SignMasks)
                {
                    CheckComparisons(left, right ^ sign);
                }
            }
        }

        ulong random = 0xEC40AAAFE568BCD1;
        for (int index = 0; index < 8192; index++)
        {
            ulong left = NextBits(ref random);
            CheckComparisons(left, NextBits(ref random));
        }
    }

    [TestMethod]
    public void PrecisionAndIntegerConversionsMatchExactClrBitsAndCheckedFaults()
    {
        foreach (ulong edge in Edges)
        {
            foreach (ulong sign in SignMasks)
            {
                CheckConversions(edge ^ sign);
            }
        }

        ulong random = 0x31C233D816A78ABA;
        for (int index = 0; index < 8192; index++)
        {
            CheckConversions(NextBits(ref random));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EveryNumericPrimitiveRunsAsVerifiedIntegerCilAcrossQuanta(bool minimumQuantum)
    {
        Type[] implementations = [typeof(WarpPortableNumericComparisons), typeof(WarpPortableNumericConversions)];
        foreach (Type implementation in implementations)
        {
            string semantics = implementation == typeof(WarpPortableNumericComparisons)
                ? WarpPortableNumericComparisons.Semantics : WarpPortableNumericConversions.Semantics;
            foreach (MethodInfo method in implementation.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                int inputCount = method.GetParameters().Length;
                WarpIntegerMapKernel verified = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(method, inputCount));
                WarpControlFlowKernel body = verified.ControlFlow;
                var identified = new WarpControlFlowKernel(body.Name + "/" + semantics, body.InputBufferCount,
                    body.ScalarArgumentCount, body.Blocks, body.Reduction, body.Functions);
                var layout = new WarpLogicalMachineLayout(identified);
                CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
                uint[] state = layout.CreateInitialState(16, 100000);
                uint[][] inputs = Enumerable.Range(0, inputCount).Select(_ => new uint[1]).ToArray();
                object[] arguments = new object[inputCount];
                for (int index = 0; index < Edges.Length; index++)
                {
                    for (int argument = 0; argument < inputCount; argument++)
                    {
                        ulong edge = Edges[(index + argument / 2) % Edges.Length];
                        uint value = argument % 2 == 0 ? unchecked((uint)edge) : (uint)(edge >> 32);
                        inputs[argument][0] = value;
                        arguments[argument] = value;
                    }

                    layout.ResetState(state, 100000);
                    int quantum = minimumQuantum ? layout.MaximumBlockCost : 100000;
                    for (int iteration = 0; state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; iteration++)
                    {
                        Assert.IsLessThan(10000, iteration);
                        executable.ExecuteQuantum(inputs, [], 0, state, 16, quantum);
                    }

                    Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset], method.Name);
                    Assert.AreEqual((uint)method.Invoke(null, arguments)!, state[WarpLogicalMachineLayout.ResultOffset], method.Name);
                }

                foreach (WarpBackendKind backend in WarpBackendCatalog.Required.Where(value => value != WarpBackendKind.CoreCLR))
                {
                    string source = WarpPortableMachineEmitter.Emit(layout, backend);
                    Assert.IsFalse(source.Contains("fptosi", StringComparison.Ordinal));
                    Assert.IsFalse(source.Contains("fptoui", StringComparison.Ordinal));
                    Assert.IsFalse(source.Contains("sitofp", StringComparison.Ordinal));
                    Assert.IsFalse(source.Contains("uitofp", StringComparison.Ordinal));
                    Assert.IsFalse(source.Contains("fcmp", StringComparison.Ordinal));
                    Assert.IsFalse(source.Contains("fast ", StringComparison.Ordinal));
                }
            }
        }
    }

    private static void CheckComparisons(ulong leftBits, ulong rightBits)
    {
        uint leftLow = unchecked((uint)leftBits);
        uint leftHigh = (uint)(leftBits >> 32);
        uint rightLow = unchecked((uint)rightBits);
        uint rightHigh = (uint)(rightBits >> 32);
        float leftSingle = BitConverter.UInt32BitsToSingle(leftLow);
        float rightSingle = BitConverter.UInt32BitsToSingle(rightLow);
        bool unorderedSingle = float.IsNaN(leftSingle) || float.IsNaN(rightSingle);
        double leftDouble = BitConverter.UInt64BitsToDouble(leftBits);
        double rightDouble = BitConverter.UInt64BitsToDouble(rightBits);
        bool unorderedDouble = double.IsNaN(leftDouble) || double.IsNaN(rightDouble);
        Assert.AreEqual(Bit(unorderedSingle), WarpPortableNumericComparisons.Binary32Unordered(leftLow, rightLow));
        Assert.AreEqual(Bit(leftSingle == rightSingle), WarpPortableNumericComparisons.Binary32Equal(leftLow, rightLow));
        Assert.AreEqual(Bit(leftSingle != rightSingle), WarpPortableNumericComparisons.Binary32NotEqual(leftLow, rightLow));
        Assert.AreEqual(Bit(leftSingle < rightSingle), WarpPortableNumericComparisons.Binary32Less(leftLow, rightLow));
        Assert.AreEqual(Bit(leftSingle > rightSingle), WarpPortableNumericComparisons.Binary32Greater(leftLow, rightLow));
        Assert.AreEqual(Bit(leftSingle <= rightSingle), WarpPortableNumericComparisons.Binary32LessOrEqual(leftLow, rightLow));
        Assert.AreEqual(Bit(leftSingle >= rightSingle), WarpPortableNumericComparisons.Binary32GreaterOrEqual(leftLow, rightLow));
        Assert.AreEqual(Bit(leftSingle < rightSingle || unorderedSingle), WarpPortableNumericComparisons.Binary32LessOrUnordered(leftLow, rightLow));
        Assert.AreEqual(Bit(leftSingle > rightSingle || unorderedSingle), WarpPortableNumericComparisons.Binary32GreaterOrUnordered(leftLow, rightLow));
        Assert.AreEqual(SingleBits(-leftSingle), WarpPortableNumericComparisons.Binary32Negate(leftLow));
        Assert.AreEqual(SingleBits(MathF.Abs(leftSingle)), WarpPortableNumericComparisons.Binary32Abs(leftLow));
        Assert.AreEqual(SingleBits(MathF.Min(leftSingle, rightSingle)), WarpPortableNumericComparisons.Binary32Min(leftLow, rightLow));
        Assert.AreEqual(SingleBits(MathF.Max(leftSingle, rightSingle)), WarpPortableNumericComparisons.Binary32Max(leftLow, rightLow));
        Assert.AreEqual(Bit(unorderedDouble), WarpPortableNumericComparisons.Binary64Unordered(leftLow, leftHigh, rightLow, rightHigh));
        Assert.AreEqual(Bit(leftDouble == rightDouble), WarpPortableNumericComparisons.Binary64Equal(leftLow, leftHigh, rightLow, rightHigh));
        Assert.AreEqual(Bit(leftDouble != rightDouble), WarpPortableNumericComparisons.Binary64NotEqual(leftLow, leftHigh, rightLow, rightHigh));
        Assert.AreEqual(Bit(leftDouble < rightDouble), WarpPortableNumericComparisons.Binary64Less(leftLow, leftHigh, rightLow, rightHigh));
        Assert.AreEqual(Bit(leftDouble > rightDouble), WarpPortableNumericComparisons.Binary64Greater(leftLow, leftHigh, rightLow, rightHigh));
        Assert.AreEqual(Bit(leftDouble <= rightDouble), WarpPortableNumericComparisons.Binary64LessOrEqual(leftLow, leftHigh, rightLow, rightHigh));
        Assert.AreEqual(Bit(leftDouble >= rightDouble), WarpPortableNumericComparisons.Binary64GreaterOrEqual(leftLow, leftHigh, rightLow, rightHigh));
        Assert.AreEqual(Bit(leftDouble < rightDouble || unorderedDouble), WarpPortableNumericComparisons.Binary64LessOrUnordered(leftLow, leftHigh, rightLow, rightHigh));
        Assert.AreEqual(Bit(leftDouble > rightDouble || unorderedDouble), WarpPortableNumericComparisons.Binary64GreaterOrUnordered(leftLow, leftHigh, rightLow, rightHigh));
        Assert.AreEqual(DoubleBits(-leftDouble), Join(WarpPortableNumericComparisons.Binary64NegateLow(leftLow, leftHigh), WarpPortableNumericComparisons.Binary64NegateHigh(leftLow, leftHigh)));
        Assert.AreEqual(DoubleBits(Math.Abs(leftDouble)), Join(WarpPortableNumericComparisons.Binary64AbsLow(leftLow, leftHigh), WarpPortableNumericComparisons.Binary64AbsHigh(leftLow, leftHigh)));
        Assert.AreEqual(DoubleBits(Math.Min(leftDouble, rightDouble)), Join(WarpPortableNumericComparisons.Binary64MinLow(leftLow, leftHigh, rightLow, rightHigh), WarpPortableNumericComparisons.Binary64MinHigh(leftLow, leftHigh, rightLow, rightHigh)));
        Assert.AreEqual(DoubleBits(Math.Max(leftDouble, rightDouble)), Join(WarpPortableNumericComparisons.Binary64MaxLow(leftLow, leftHigh, rightLow, rightHigh), WarpPortableNumericComparisons.Binary64MaxHigh(leftLow, leftHigh, rightLow, rightHigh)));
    }

    private static void CheckConversions(ulong bits)
    {
        uint low = unchecked((uint)bits);
        uint high = (uint)(bits >> 32);
        float single = BitConverter.UInt32BitsToSingle(low);
        double value = BitConverter.UInt64BitsToDouble(bits);
        Assert.AreEqual(SingleBits((float)value), WarpPortableNumericConversions.DoubleToSingle(low, high));
        Assert.AreEqual(DoubleBits(single), Join(WarpPortableNumericConversions.SingleToDoubleLow(low), WarpPortableNumericConversions.SingleToDoubleHigh(low)));
        Assert.AreEqual(SingleBits(low), WarpPortableNumericConversions.UInt32ToSingle(low));
        Assert.AreEqual(SingleBits(unchecked((int)low)), WarpPortableNumericConversions.Int32ToSingle(low));
        Assert.AreEqual(SingleBits(bits), WarpPortableNumericConversions.UInt64ToSingle(low, high));
        Assert.AreEqual(SingleBits(unchecked((long)bits)), WarpPortableNumericConversions.Int64ToSingle(low, high));
        Assert.AreEqual(DoubleBits(low), Join(WarpPortableNumericConversions.UInt32ToDoubleLow(low), WarpPortableNumericConversions.UInt32ToDoubleHigh(low)));
        Assert.AreEqual(DoubleBits(unchecked((int)low)), Join(WarpPortableNumericConversions.Int32ToDoubleLow(low), WarpPortableNumericConversions.Int32ToDoubleHigh(low)));
        Assert.AreEqual(DoubleBits(bits), Join(WarpPortableNumericConversions.UInt64ToDoubleLow(low, high), WarpPortableNumericConversions.UInt64ToDoubleHigh(low, high)));
        Assert.AreEqual(DoubleBits(unchecked((long)bits)), Join(WarpPortableNumericConversions.Int64ToDoubleLow(low, high), WarpPortableNumericConversions.Int64ToDoubleHigh(low, high)));
        Assert.AreEqual(unchecked((uint)(int)value), WarpPortableNumericConversions.DoubleToInt32(low, high));
        Assert.AreEqual(unchecked((uint)value), WarpPortableNumericConversions.DoubleToUInt32(low, high));
        Assert.AreEqual(unchecked((ulong)(long)value), Join(WarpPortableNumericConversions.DoubleToInt64Low(low, high), WarpPortableNumericConversions.DoubleToInt64High(low, high)));
        Assert.AreEqual(unchecked((ulong)value), Join(WarpPortableNumericConversions.DoubleToUInt64Low(low, high), WarpPortableNumericConversions.DoubleToUInt64High(low, high)));
        Assert.AreEqual(unchecked((uint)(int)single), WarpPortableNumericConversions.SingleToInt32(low));
        Assert.AreEqual(unchecked((uint)single), WarpPortableNumericConversions.SingleToUInt32(low));
        Assert.AreEqual(unchecked((ulong)(long)single), Join(WarpPortableNumericConversions.SingleToInt64Low(low), WarpPortableNumericConversions.SingleToInt64High(low)));
        Assert.AreEqual(unchecked((ulong)single), Join(WarpPortableNumericConversions.SingleToUInt64Low(low), WarpPortableNumericConversions.SingleToUInt64High(low)));
        Assert.AreEqual(CheckedFault(() => { _ = checked((int)value); }), WarpPortableNumericConversions.DoubleToInt32Fault(low, high));
        Assert.AreEqual(CheckedFault(() => { _ = checked((uint)value); }), WarpPortableNumericConversions.DoubleToUInt32Fault(low, high));
        Assert.AreEqual(CheckedFault(() => { _ = checked((long)value); }), WarpPortableNumericConversions.DoubleToInt64Fault(low, high));
        Assert.AreEqual(CheckedFault(() => { _ = checked((ulong)value); }), WarpPortableNumericConversions.DoubleToUInt64Fault(low, high));
        Assert.AreEqual(CheckedFault(() => { _ = checked((int)single); }), WarpPortableNumericConversions.SingleToInt32Fault(low));
        Assert.AreEqual(CheckedFault(() => { _ = checked((uint)single); }), WarpPortableNumericConversions.SingleToUInt32Fault(low));
        Assert.AreEqual(CheckedFault(() => { _ = checked((long)single); }), WarpPortableNumericConversions.SingleToInt64Fault(low));
        Assert.AreEqual(CheckedFault(() => { _ = checked((ulong)single); }), WarpPortableNumericConversions.SingleToUInt64Fault(low));
    }

    private static uint CheckedFault(Action conversion)
    {
        try
        {
            conversion();
            return WarpPortableNumericConversions.NoFault;
        }
        catch (OverflowException)
        {
            return WarpPortableNumericConversions.OverflowFault;
        }
    }

    private static uint Bit(bool value) => value ? 1u : 0u;

    private static uint SingleBits(float value) => float.IsNaN(value) ? 0x7FC00000u : BitConverter.SingleToUInt32Bits(value);

    private static ulong DoubleBits(double value) => double.IsNaN(value) ? 0x7FF8000000000000 : BitConverter.DoubleToUInt64Bits(value);

    private static ulong Join(uint low, uint high) => (ulong)high << 32 | low;

    private static ulong NextBits(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }
}
