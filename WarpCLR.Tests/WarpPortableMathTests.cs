using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpPortableMathTests
{
    private static readonly ulong[] DoubleEdges =
    [
        0, 1, 2, 3, 0x000FFFFFFFFFFFFF, 0x0010000000000000, 0x0010000000000001,
        0x1C8D2737B06EC2AD, 0x3FE0000000000000, 0x3FEFFFFFFFFFFFFF,
        0x3FF0000000000000, 0x3FF0000000000001, 0x3FF0000002000000, 0x3FEFFFFFFC000000,
        0x4000000000000000, 0x4008000000000000, 0x4014000000000000, 0x7FEFFFFFFFFFFFFF,
        0x7FF0000000000000, 0x7FF8000000000000, 0x7FF0000000000001,
        0x8000000000000000, 0xBFF0000000000000, 0xFFF0000000000000,
    ];
    private static readonly uint[] SingleEdges =
    [
        0, 1, 2, 3, 0x007FFFFF, 0x00800000, 0x00800001, 0x3F000000, 0x3F7FFFFF, 0x3F800000,
        0x3F800001, 0x3F800400, 0x3F7FF800, 0x40000000, 0x40400000, 0x40A00000,
        0x7F7FFFFF, 0x7F800000, 0x7FC00000, 0x7F800001, 0x80000000, 0xBF800000, 0xFF800000,
    ];

    [TestMethod]
    public void MathCatalogBindsDeclaredPrecisionAndSemanticVersionToKernelIdentity()
    {
        IReadOnlyList<WarpLogicalMachineLayout> single = WarpPortableMathKernels.CreateBinary32Math();
        IReadOnlyList<WarpLogicalMachineLayout> wide = WarpPortableMathKernels.CreateBinary64Math();
        Assert.HasCount(4, single);
        Assert.HasCount(8, wide);
        foreach (WarpLogicalMachineLayout layout in single.Concat(wide))
        {
            string semantics = single.Contains(layout) ? WarpPortableBinary32Math.Semantics : WarpPortableBinary64Math.Semantics;
            StringAssert.Contains(layout.Kernel.Name, semantics, StringComparison.Ordinal);
            WarpControlFlowKernel kernel = layout.Kernel;
            var renamed = new WarpControlFlowKernel(kernel.Name + "/another-version", kernel.InputBufferCount,
                kernel.ScalarArgumentCount, kernel.Blocks, kernel.Reduction, kernel.Functions);
            Assert.AreNotEqual(WarpIrHash.Compute(kernel), WarpIrHash.Compute(renamed), StringComparer.Ordinal);
        }
    }

    [TestMethod]
    [DataRow(32)]
    [DataRow(64)]
    public void PortableMathMatchesClrBitsAndIndependentExactBigIntegerOracle(int width)
    {
        ulong[] edges = width == 32 ? SingleEdges.Select(value => (ulong)value).ToArray() : DoubleEdges;
        foreach (ulong left in edges)
        {
            foreach (ulong right in edges)
            {
                foreach (ulong addend in edges)
                {
                    Check(left, right, addend, width);
                }
            }
        }

        ulong random = 0x32386799805118F6;
        for (int index = 0; index < 4096; index++)
        {
            ulong left = Next(ref random);
            ulong right = Next(ref random);
            ulong addend = Next(ref random);
            if (width == 32)
            {
                Check(unchecked((uint)left), unchecked((uint)right), unchecked((uint)addend), width);
            }
            else
            {
                Check(left, right, addend, width);
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EveryMathPrimitiveExecutesVerifiedCilAcrossQuanta(bool minimumQuantum)
    {
        Type[] implementations = [typeof(WarpPortableBinary32Math), typeof(WarpPortableBinary64Math)];
        foreach (Type implementation in implementations)
        {
            string semantics = implementation == typeof(WarpPortableBinary32Math)
                ? WarpPortableBinary32Math.Semantics : WarpPortableBinary64Math.Semantics;
            foreach (MethodInfo method in implementation.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                int inputCount = method.GetParameters().Length;
                WarpIntegerMapKernel verified = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(method, inputCount));
                WarpControlFlowKernel body = verified.ControlFlow;
                var identified = new WarpControlFlowKernel(body.Name + "/" + semantics, body.InputBufferCount,
                    body.ScalarArgumentCount, body.Blocks, body.Reduction, body.Functions);
                var layout = new WarpLogicalMachineLayout(identified);
                CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
                uint[] state = layout.CreateInitialState(16, 10000000);
                uint[][] inputs = Enumerable.Range(0, inputCount).Select(_ => new uint[1]).ToArray();
                object[] arguments = new object[inputCount];
                for (int index = 0; index < DoubleEdges.Length + 8 + 128; index++)
                {
                    for (int argument = 0; argument < inputCount; argument++)
                    {
                        uint value = InputWord(implementation == typeof(WarpPortableBinary32Math), index, argument);
                        inputs[argument][0] = value;
                        arguments[argument] = value;
                    }

                    layout.ResetState(state, 10000000);
                    int quantum = minimumQuantum ? layout.MaximumBlockCost : 10000000;
                    for (int iteration = 0; state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; iteration++)
                    {
                        Assert.IsLessThan(100000, iteration);
                        executable.ExecuteQuantum(inputs, [], 0, state, 16, quantum);
                    }

                    Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset], method.Name);
                    Assert.AreEqual((uint)method.Invoke(null, arguments)!, state[WarpLogicalMachineLayout.ResultOffset], method.Name);
                    Assert.AreEqual(ExpectedWord(method, inputs), state[WarpLogicalMachineLayout.ResultOffset], method.Name);
                }

                CheckBackendSource(layout);
            }
        }
    }

    [TestMethod]
    public void FmaPreservesCancellationOverflowRescueAndSubnormalProductBits()
    {
        Check(0x3FF0000002000000, 0x3FEFFFFFFC000000, 0xBFF0000000000000, 64);
        Check(0x3F800400, 0x3F7FF800, 0xBF800000, 32);
        Check(0x7FEFFFFFFFFFFFFF, 0x4000000000000000, 0xFFEFFFFFFFFFFFFF, 64);
        Check(0x7F7FFFFF, 0x40000000, 0xFF7FFFFF, 32);
        Check(1, 0x3FE0000000000000, 1, 64);
        Check(1, 0x3F000000, 1, 32);
        Check(0x0010000000000001, 0x3FEFFFFFFFFFFFFF, 0x8010000000000000, 64);
        Check(0x00800001, 0x3F7FFFFF, 0x80800000, 32);
    }

    private static uint InputWord(bool single, int index, int argument)
    {
        int operand = single ? argument : argument / 2;
        ulong bits = CaseBits(single, index, operand);
        return single || argument % 2 == 0 ? unchecked((uint)bits) : (uint)(bits >> 32);
    }

    private static ulong CaseBits(bool single, int index, int operand)
    {
        if (index < DoubleEdges.Length)
        {
            return single ? SingleEdges[(index + operand) % SingleEdges.Length] : DoubleEdges[(index + operand) % DoubleEdges.Length];
        }

        if (index < DoubleEdges.Length + 8)
        {
            (ulong left, ulong right, ulong addend) = Witness(single, index - DoubleEdges.Length);
            return operand == 0 ? left : operand == 1 ? right : addend;
        }

        ulong raw = RandomOperand(index, operand);
        return operand == 2 && index % 4 == 0 ? CancellationAddend(single, index, raw) : single ? unchecked((uint)raw) : raw;
    }

    private static ulong CancellationAddend(bool single, int index, ulong fallback)
    {
        if (single)
        {
            float product = BitConverter.UInt32BitsToSingle(unchecked((uint)RandomOperand(index, 0))) *
                BitConverter.UInt32BitsToSingle(unchecked((uint)RandomOperand(index, 1)));
            return float.IsFinite(product) ? BitConverter.SingleToUInt32Bits(-product) : unchecked((uint)fallback);
        }

        double wideProduct = BitConverter.UInt64BitsToDouble(RandomOperand(index, 0)) * BitConverter.UInt64BitsToDouble(RandomOperand(index, 1));
        return double.IsFinite(wideProduct) ? BitConverter.DoubleToUInt64Bits(-wideProduct) : fallback;
    }

    private static ulong RandomOperand(int index, int operand)
    {
        ulong random = unchecked((0x688ABD8748394180 ^ (ulong)(uint)index << 32 ^ (uint)operand) * 0x9E3779B185EBCA87);
        _ = Next(ref random);
        return Next(ref random);
    }

    private static (ulong Left, ulong Right, ulong Addend) Witness(bool single, int index)
    {
        if (single)
        {
            return index switch
            {
                0 => (0x3F800400UL, 0x3F7FF800UL, 0xBF800000UL),
                1 => (0x7F7FFFFFUL, 0x40000000UL, 0xFF7FFFFFUL),
                2 => (1UL, 0x3F000000UL, 1UL),
                3 => (0x00800001UL, 0x3F7FFFFFUL, 0x80800000UL),
                4 => (0x80000000UL, 0x3F800000UL, 0x80000000UL),
                5 => (0x40400000UL, 0x40000000UL, 0UL),
                6 => (0x40A00000UL, 0x40000000UL, 0UL),
                _ => (0x00800000UL, 1UL, 0UL),
            };
        }

        return index switch
        {
            0 => (0x3FF0000002000000UL, 0x3FEFFFFFFC000000UL, 0xBFF0000000000000UL),
            1 => (0x7FEFFFFFFFFFFFFFUL, 0x4000000000000000UL, 0xFFEFFFFFFFFFFFFFUL),
            2 => (1UL, 0x3FE0000000000000UL, 1UL),
            3 => (0x0010000000000001UL, 0x3FEFFFFFFFFFFFFFUL, 0x8010000000000000UL),
            4 => (0x8000000000000000UL, 0x3FF0000000000000UL, 0x8000000000000000UL),
            5 => (0x4008000000000000UL, 0x4000000000000000UL, 0UL),
            6 => (0x4014000000000000UL, 0x4000000000000000UL, 0UL),
            _ => (0x0010000000000000UL, 1UL, 0UL),
        };
    }

    private static uint ExpectedWord(MethodInfo method, uint[][] inputs)
    {
        bool single = method.DeclaringType == typeof(WarpPortableBinary32Math);
        int width = single ? 32 : 64;
        ulong left = single ? inputs[0][0] : Join(inputs[0][0], inputs[1][0]);
        ulong right = inputs.Length <= (single ? 1 : 2) ? 0 : single ? inputs[1][0] : Join(inputs[2][0], inputs[3][0]);
        ulong addend = inputs.Length <= (single ? 2 : 4) ? 0 : single ? inputs[2][0] : Join(inputs[4][0], inputs[5][0]);
        ulong expected = method.Name.StartsWith("Sqrt", StringComparison.Ordinal) ? WarpPortableMathOracle.Sqrt(left, width) :
            method.Name.StartsWith("IeeeRemainder", StringComparison.Ordinal) ? WarpPortableMathOracle.Remainder(left, right, width, nearest: true) :
            method.Name.StartsWith("Remainder", StringComparison.Ordinal) ? WarpPortableMathOracle.Remainder(left, right, width, nearest: false) :
            WarpPortableMathOracle.FusedMultiplyAdd(left, right, addend, width);
        return method.Name.EndsWith("High", StringComparison.Ordinal) ? (uint)(expected >> 32) : unchecked((uint)expected);
    }

    private static void CheckBackendSource(WarpLogicalMachineLayout layout)
    {
        foreach (WarpBackendKind backend in WarpBackendCatalog.Required.Where(value => value != WarpBackendKind.CoreCLR))
        {
            string source = WarpPortableMachineEmitter.Emit(layout, backend);
            Assert.IsFalse(source.Contains("llvm.sqrt", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("llvm.fma", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("frem", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("fast ", StringComparison.Ordinal));
        }
    }

    private static void Check(ulong left, ulong right, ulong addend, int width)
    {
        if (width == 32)
        {
            CheckSingle(checked((uint)left), checked((uint)right), checked((uint)addend));
        }
        else
        {
            CheckDouble(left, right, addend);
        }
    }

    private static void CheckSingle(uint left, uint right, uint addend)
    {
        float leftValue = BitConverter.UInt32BitsToSingle(left);
        float rightValue = BitConverter.UInt32BitsToSingle(right);
        float addendValue = BitConverter.UInt32BitsToSingle(addend);
        uint root = WarpPortableBinary32Math.Sqrt(left);
        uint remainder = WarpPortableBinary32Math.Remainder(left, right);
        uint ieee = WarpPortableBinary32Math.IeeeRemainder(left, right);
        uint fused = WarpPortableBinary32Math.FusedMultiplyAdd(left, right, addend);
        Assert.AreEqual(SingleBits(MathF.Sqrt(leftValue)), root);
        Assert.AreEqual(SingleBits(leftValue % rightValue), remainder);
        Assert.AreEqual(SingleBits(MathF.IEEERemainder(leftValue, rightValue)), ieee);
        Assert.AreEqual(SingleBits(MathF.FusedMultiplyAdd(leftValue, rightValue, addendValue)), fused);
        Assert.AreEqual(WarpPortableMathOracle.Sqrt(left, 32), (ulong)root);
        Assert.AreEqual(WarpPortableMathOracle.Remainder(left, right, 32, nearest: false), (ulong)remainder);
        Assert.AreEqual(WarpPortableMathOracle.Remainder(left, right, 32, nearest: true), (ulong)ieee);
        Assert.AreEqual(WarpPortableMathOracle.FusedMultiplyAdd(left, right, addend, 32), (ulong)fused);
    }

    private static void CheckDouble(ulong left, ulong right, ulong addend)
    {
        uint leftLow = unchecked((uint)left);
        uint leftHigh = (uint)(left >> 32);
        uint rightLow = unchecked((uint)right);
        uint rightHigh = (uint)(right >> 32);
        uint addendLow = unchecked((uint)addend);
        uint addendHigh = (uint)(addend >> 32);
        double leftValue = BitConverter.UInt64BitsToDouble(left);
        double rightValue = BitConverter.UInt64BitsToDouble(right);
        double addendValue = BitConverter.UInt64BitsToDouble(addend);
        ulong root = Join(WarpPortableBinary64Math.SqrtLow(leftLow, leftHigh), WarpPortableBinary64Math.SqrtHigh(leftLow, leftHigh));
        ulong remainder = Join(WarpPortableBinary64Math.RemainderLow(leftLow, leftHigh, rightLow, rightHigh), WarpPortableBinary64Math.RemainderHigh(leftLow, leftHigh, rightLow, rightHigh));
        ulong ieee = Join(WarpPortableBinary64Math.IeeeRemainderLow(leftLow, leftHigh, rightLow, rightHigh), WarpPortableBinary64Math.IeeeRemainderHigh(leftLow, leftHigh, rightLow, rightHigh));
        ulong fused = Join(WarpPortableBinary64Math.FusedMultiplyAddLow(leftLow, leftHigh, rightLow, rightHigh, addendLow, addendHigh), WarpPortableBinary64Math.FusedMultiplyAddHigh(leftLow, leftHigh, rightLow, rightHigh, addendLow, addendHigh));
        Assert.AreEqual(DoubleBits(Math.Sqrt(leftValue)), root);
        Assert.AreEqual(DoubleBits(leftValue % rightValue), remainder);
        Assert.AreEqual(DoubleBits(Math.IEEERemainder(leftValue, rightValue)), ieee);
        Assert.AreEqual(DoubleBits(Math.FusedMultiplyAdd(leftValue, rightValue, addendValue)), fused);
        Assert.AreEqual(WarpPortableMathOracle.Sqrt(left, 64), root);
        Assert.AreEqual(WarpPortableMathOracle.Remainder(left, right, 64, nearest: false), remainder);
        Assert.AreEqual(WarpPortableMathOracle.Remainder(left, right, 64, nearest: true), ieee);
        Assert.AreEqual(WarpPortableMathOracle.FusedMultiplyAdd(left, right, addend, 64), fused);
    }

    private static uint SingleBits(float value) => float.IsNaN(value) ? 0x7FC00000u : BitConverter.SingleToUInt32Bits(value);

    private static ulong DoubleBits(double value) => double.IsNaN(value) ? 0x7FF8000000000000 : BitConverter.DoubleToUInt64Bits(value);

    private static ulong Join(uint low, uint high) => (ulong)high << 32 | low;

    private static ulong Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }
}
