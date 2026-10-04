using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpPortableIntrinsicTests
{
    private static readonly ulong[] DoubleEdges =
    [
        0, 1, 2, 3, 0x000FFFFFFFFFFFFF, 0x0010000000000000, 0x0010000000000001,
        0x3FDFFFFFFFFFFFFF, 0x3FE0000000000000, 0x3FE0000000000001, 0x3FEFFFFFFFFFFFFF,
        0x3FF0000000000000, 0x3FF8000000000000, 0x4004000000000000,
        0x433FFFFFFFFFFFFF, 0x4340000000000000, 0x4341C37937E08000,
        0x7FEFFFFFFFFFFFFF, 0x7FF0000000000000, 0x7FF8000000000000, 0x7FF0000000000001,
        0x8000000000000000, 0xBFE0000000000000, 0xBFF8000000000000, 0xFFF0000000000000,
    ];
    private static readonly uint[] SingleEdges =
    [
        0, 1, 2, 3, 0x007FFFFF, 0x00800000, 0x00800001, 0x3EFFFFFF, 0x3F000000,
        0x3F000001, 0x3F7FFFFF, 0x3F800000, 0x3FC00000, 0x40200000, 0x4B7FFFFF,
        0x4B800000, 0x4CBEBC20, 0x7F7FFFFF, 0x7F800000, 0x7FC00000, 0x7F800001,
        0x80000000, 0xBF000000, 0xBFC00000, 0xFF800000,
    ];
    private static readonly int[] Scales =
    [
        int.MinValue, -4097, -4096, -2100, -1075, -1074, -1022, -150, -149, -126,
        -64, -53, -52, -32, -24, -23, -1, 0, 1, 23, 24, 32, 52, 53, 64, 126,
        127, 128, 1023, 1024, 2100, 4096, 4097, int.MaxValue,
    ];

    [TestMethod]
    [DataRow(32)]
    [DataRow(64)]
    public void IntrinsicsMatchIndependentIntegerAndRationalOracles(int width)
    {
        ulong[] edges = width == 32 ? SingleEdges.Select(value => (ulong)value).ToArray() : DoubleEdges;
        foreach (ulong value in edges)
        {
            foreach (int scale in Scales)
            {
                CheckExact(value, width, unchecked((uint)scale));
            }
        }

        ulong random = 0x1402F719668B4CDD;
        for (int index = 0; index < 2048; index++)
        {
            ulong value = Next(ref random);
            uint scale = unchecked((uint)((int)(Next(ref random) % 4400) - 2200));
            CheckExact(width == 32 ? unchecked((uint)value) : value, width, scale);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EveryIntrinsicRunsAsVerifiedCilWithExplicitFaultsAndPrecision(bool minimumQuantum)
    {
        Type[] implementations = [typeof(WarpPortableBinary32Intrinsics), typeof(WarpPortableBinary64Intrinsics)];
        foreach (Type implementation in implementations)
        {
            string semantics = implementation == typeof(WarpPortableBinary32Intrinsics)
                ? WarpPortableBinary32Intrinsics.Semantics : WarpPortableBinary64Intrinsics.Semantics;
            foreach (MethodInfo method in implementation.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                ParameterInfo[] parameters = method.GetParameters();
                WarpIntegerMapKernel verified = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(method, parameters.Length));
                WarpControlFlowKernel body = verified.ControlFlow;
                var identified = new WarpControlFlowKernel(body.Name + "/" + semantics, body.InputBufferCount,
                    body.ScalarArgumentCount, body.Blocks, body.Reduction, body.Functions);
                var layout = new WarpLogicalMachineLayout(identified);
                CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
                uint[] state = layout.CreateInitialState(16, 10000000);
                uint[][] inputs = Enumerable.Range(0, parameters.Length).Select(_ => new uint[1]).ToArray();
                object[] arguments = new object[parameters.Length];
                for (int index = 0; index < 96; index++)
                {
                    for (int argument = 0; argument < parameters.Length; argument++)
                    {
                        uint value = Input(method, parameters[argument], index);
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
                    Assert.AreEqual(Expected(method, arguments), state[WarpLogicalMachineLayout.ResultOffset], method.Name);
                }
            }
        }
    }

    [TestMethod]
    public void IntrinsicCatalogMakesSemanticVersionPartOfArtifactIdentity()
    {
        IReadOnlyList<WarpLogicalMachineLayout> single = WarpPortableIntrinsicKernels.CreateBinary32Intrinsics();
        IReadOnlyList<WarpLogicalMachineLayout> wide = WarpPortableIntrinsicKernels.CreateBinary64Intrinsics();
        Assert.HasCount(15, single);
        Assert.HasCount(25, wide);
        foreach (WarpLogicalMachineLayout layout in single.Concat(wide))
        {
            WarpControlFlowKernel kernel = layout.Kernel;
            string semantics = single.Contains(layout) ? WarpPortableBinary32Intrinsics.Semantics : WarpPortableBinary64Intrinsics.Semantics;
            StringAssert.Contains(kernel.Name, semantics, StringComparison.Ordinal);
            var renamed = new WarpControlFlowKernel(kernel.Name + "/another-version", kernel.InputBufferCount,
                kernel.ScalarArgumentCount, kernel.Blocks, kernel.Reduction, kernel.Functions);
            Assert.AreNotEqual(WarpIrHash.Compute(kernel), WarpIrHash.Compute(renamed), StringComparer.Ordinal);
        }
    }

    [TestMethod]
    public void FaultDescriptorsMatchManagedExceptionTypesAndValidationOrder()
    {
        uint[] modes = [0, 1, 2, 3, 4, 5, 0xFFFFFFFF];
        uint[] digits = [0, 6, 7, 15, 16, 0xFFFFFFFF];
        foreach (uint mode in modes)
        {
            Assert.AreEqual(ExceptionKind(() => { _ = Math.Round(0.5, unchecked((MidpointRounding)mode)); }), WarpPortableBinary64Intrinsics.RoundFault(mode));
            Assert.AreEqual(ExceptionKind(() => { _ = MathF.Round(0.5f, unchecked((MidpointRounding)mode)); }), WarpPortableBinary32Intrinsics.RoundFault(mode));
            foreach (uint digit in digits)
            {
                Assert.AreEqual(ExceptionKind(() => { _ = Math.Round(0.5, unchecked((int)digit), unchecked((MidpointRounding)mode)); }), WarpPortableBinary64Intrinsics.RoundDigitsFault(digit, mode));
                Assert.AreEqual(ExceptionKind(() => { _ = MathF.Round(0.5f, unchecked((int)digit), unchecked((MidpointRounding)mode)); }), WarpPortableBinary32Intrinsics.RoundDigitsFault(digit, mode));
            }
        }

        Assert.AreEqual(ExceptionKind(() => { _ = Math.Sign(double.NaN); }), WarpPortableBinary64Intrinsics.SignFault(0, 0x7FF80000));
        Assert.AreEqual(ExceptionKind(() => { _ = MathF.Sign(float.NaN); }), WarpPortableBinary32Intrinsics.SignFault(0x7FC00000));
    }

    private static uint ExceptionKind(Action operation)
    {
        try
        {
            operation();
            return 0;
        }
        catch (ArgumentOutOfRangeException) { return 2; }
        catch (ArgumentException) { return 3; }
        catch (ArithmeticException) { return 1; }
    }

    private static void CheckExact(ulong bits, int width, uint scale)
    {
        uint low = unchecked((uint)bits);
        uint high = (uint)(bits >> 32);
        for (uint mode = 0; mode < 5; mode++)
        {
            ulong actual = width == 32 ? WarpPortableBinary32Intrinsics.Round(low, mode) :
                Join(WarpPortableBinary64Intrinsics.RoundLow(low, high, mode), WarpPortableBinary64Intrinsics.RoundHigh(low, high, mode));
            Assert.AreEqual(WarpPortableIntrinsicOracle.Integral(bits, width, mode), actual);
            uint maximumDigits = width == 32 ? 6u : 15u;
            for (uint digits = 0; digits <= maximumDigits; digits++)
            {
                actual = width == 32 ? WarpPortableBinary32Intrinsics.RoundDigits(low, digits, mode) :
                    Join(WarpPortableBinary64Intrinsics.RoundDigitsLow(low, high, digits, mode), WarpPortableBinary64Intrinsics.RoundDigitsHigh(low, high, digits, mode));
                Assert.AreEqual(WarpPortableIntrinsicOracle.Digits(bits, width, digits, mode), actual);
            }
        }

        ulong scaled = width == 32 ? WarpPortableBinary32Intrinsics.ScaleB(low, scale) :
            Join(WarpPortableBinary64Intrinsics.ScaleBLow(low, high, scale), WarpPortableBinary64Intrinsics.ScaleBHigh(low, high, scale));
        Assert.AreEqual(WarpPortableIntrinsicOracle.ScaleB(bits, width, scale), scaled);
        Assert.AreEqual(WarpPortableIntrinsicOracle.ILogB(bits, width), width == 32 ? WarpPortableBinary32Intrinsics.ILogB(low) : WarpPortableBinary64Intrinsics.ILogB(low, high));
    }

    private static uint Input(MethodInfo method, ParameterInfo parameter, int index)
    {
        bool single = method.DeclaringType == typeof(WarpPortableBinary32Intrinsics);
        if (parameter.Name is "mode") { return index % 7 == 6 ? 0xFFFFFFFFu : (uint)(index % 7); }
        if (parameter.Name is "digits") { return (uint)(index % (single ? 9 : 17)); }
        if (parameter.Name is "scale") { return unchecked((uint)Scales[index % Scales.Length]); }
        ulong bits = CaseBits(single, index);
        if (parameter.Name is "signLow" or "signHigh" or "sign") { bits ^= single ? 0x80000000 : 0x8000000000000000; }
        return parameter.Name is "high" or "signHigh" ? (uint)(bits >> 32) : unchecked((uint)bits);
    }

    private static ulong CaseBits(bool single, int index)
    {
        if (index < DoubleEdges.Length) { return single ? SingleEdges[index] : DoubleEdges[index]; }
        ulong random = unchecked((0x688ABD8748394180 ^ (ulong)(uint)index << 32) * 0x9E3779B185EBCA87);
        _ = Next(ref random);
        ulong bits = Next(ref random);
        return single ? unchecked((uint)bits) : bits;
    }

    private static uint Expected(MethodInfo method, object[] arguments)
    {
        uint[] values = arguments.Cast<uint>().ToArray();
        bool single = method.DeclaringType == typeof(WarpPortableBinary32Intrinsics);
        int width = single ? 32 : 64;
        string name = method.Name;
        if (name is "RoundFault") { return values[0] > 4 ? 3u : 0u; }
        if (name is "RoundDigitsFault") { return values[0] > (single ? 6u : 15u) ? 2u : values[1] > 4 ? 3u : 0u; }
        int extra = single ? 1 : 2;
        ulong bits = single ? values[0] : Join(values[0], values[1]);
        ulong expected = ExpectedValue(name, bits, width, values, extra);
        return name.EndsWith("High", StringComparison.Ordinal) ? (uint)(expected >> 32) : unchecked((uint)expected);
    }

    private static ulong ExpectedValue(string name, ulong bits, int width, uint[] values, int extra)
    {
        if (name.StartsWith("Floor", StringComparison.Ordinal)) { return WarpPortableIntrinsicOracle.Integral(bits, width, 3); }
        if (name.StartsWith("Ceiling", StringComparison.Ordinal)) { return WarpPortableIntrinsicOracle.Integral(bits, width, 4); }
        if (name.StartsWith("Truncate", StringComparison.Ordinal)) { return WarpPortableIntrinsicOracle.Integral(bits, width, 2); }
        if (name.StartsWith("RoundToEven", StringComparison.Ordinal)) { return WarpPortableIntrinsicOracle.Integral(bits, width, 0); }
        if (name.StartsWith("RoundDigits", StringComparison.Ordinal)) { return WarpPortableIntrinsicOracle.Digits(bits, width, values[extra], values[extra + 1]); }
        if (name.StartsWith("Round", StringComparison.Ordinal)) { return WarpPortableIntrinsicOracle.Integral(bits, width, values[extra]); }
        if (name.StartsWith("ScaleB", StringComparison.Ordinal)) { return WarpPortableIntrinsicOracle.ScaleB(bits, width, values[extra]); }
        if (name is "ILogB") { return WarpPortableIntrinsicOracle.ILogB(bits, width); }
        return NativeRepresentation(name, bits, width, values, extra);
    }

    private static ulong NativeRepresentation(string name, ulong bits, int width, uint[] values, int extra)
    {
        if (width == 32)
        {
            float value = BitConverter.UInt32BitsToSingle(checked((uint)bits));
            if (name is "SignFault") { return float.IsNaN(value) ? 1u : 0u; }
            if (name is "Sign") { return float.IsNaN(value) ? 0 : unchecked((uint)MathF.Sign(value)); }
            if (name.StartsWith("CopySign", StringComparison.Ordinal)) { return BitConverter.SingleToUInt32Bits(MathF.CopySign(value, BitConverter.UInt32BitsToSingle(values[extra]))); }
            return BitConverter.SingleToUInt32Bits(name.StartsWith("BitIncrement", StringComparison.Ordinal) ? MathF.BitIncrement(value) : MathF.BitDecrement(value));
        }

        double wide = BitConverter.UInt64BitsToDouble(bits);
        if (name is "SignFault") { return double.IsNaN(wide) ? 1u : 0u; }
        if (name is "Sign") { return double.IsNaN(wide) ? 0 : unchecked((uint)Math.Sign(wide)); }
        if (name.StartsWith("CopySign", StringComparison.Ordinal)) { return BitConverter.DoubleToUInt64Bits(Math.CopySign(wide, BitConverter.UInt64BitsToDouble(Join(values[extra], values[extra + 1])))); }
        return BitConverter.DoubleToUInt64Bits(name.StartsWith("BitIncrement", StringComparison.Ordinal) ? Math.BitIncrement(wide) : Math.BitDecrement(wide));
    }

    private static ulong Join(uint low, uint high) => (ulong)high << 32 | low;

    private static ulong Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }
}
