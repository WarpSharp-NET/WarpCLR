using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpPortableTranscendentalTests
{
    [TestMethod]
    [DataRow(32)]
    [DataRow(64)]
    public void VersionedAlgorithmsMatchIndependentHighPrecisionReferences(int width)
    {
        var maximum = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var row in WarpPortableTranscendentalWitnesses.Oracle.Where(value => value.Width == width))
        {
            ulong actual = WarpPortableTranscendentalOracle.Actual(row.Name, row.Left, row.Right, width);
            CheckReference(actual, row.Reference, width, row.Name);
            ulong distance = WarpPortableTranscendentalOracle.Distance(actual, row.Reference, width);
            if (distance < 65536) { maximum[row.Name] = Math.Max(maximum.GetValueOrDefault(row.Name), distance); }
        }

        foreach ((string name, ulong distance) in maximum)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Independent {width} {name}: observed maximum {distance} ULP in fixed corpus."));
        }
    }

    [TestMethod]
    [DataRow(32)]
    [DataRow(64)]
    public void VersionedAlgorithmsMatchActualLocalMathAndMathFReferenceClasses(int width)
    {
        foreach (var row in WarpPortableTranscendentalWitnesses.Oracle.Where(value => value.Width == width))
        {
            ulong native = NativeReference(row.Name, row.Left, row.Right, width);
            CheckReference(WarpPortableTranscendentalOracle.Actual(row.Name, row.Left, row.Right, width), native, width, row.Name);
        }
    }

    [TestMethod]
    public void FrozenAlgorithmOutputBitsIncludeExtremeArgumentsAndPrecision()
    {
        Assert.HasCount(400, WarpPortableTranscendentalRegression.Results);
        foreach (var row in WarpPortableTranscendentalRegression.Results)
        {
            Assert.AreEqual(row.Bits, WarpPortableTranscendentalOracle.Actual(row.Name, row.Left, row.Right, row.Width),
                string.Create(CultureInfo.InvariantCulture, $"{row.Name} binary{row.Width}: {row.Left:X16}, {row.Right:X16}"));
        }
    }

    [TestMethod]
    public void FullRangePhaseMatchesIndependentBigIntegerConvolution()
    {
        MethodInfo reduction = typeof(WarpPortableBinary64Transcendentals).GetMethod("Reduction", BindingFlags.NonPublic | BindingFlags.Static)!;
        ulong random = 0x1173828A26C65683;
        for (int index = 0; index < 4096; index++)
        {
            random ^= random << 13; random ^= random >> 7; random ^= random << 17;
            ulong input = random & 0x7FFFFFFFFFFFFFFF;
            if (index == 0) { input = 0x7FEFFFFFFFFFFFFF; }
            if (input <= 0x3FE921FB54442D18 || input >= 0x7FF0000000000000) { continue; }
            for (int word = 0; word < 3; word++)
            {
                uint actual = (uint)reduction.Invoke(null, [unchecked((uint)input), (uint)(input >> 32), (uint)word])!;
                Assert.AreEqual(WarpPortableTranscendentalOracle.Reduction(input, word), actual);
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EveryTranscendentalExecutesVerifiedCilAcrossQuanta(bool minimumQuantum)
    {
        foreach (Type implementation in new[] { typeof(WarpPortableBinary32Transcendentals), typeof(WarpPortableBinary64Transcendentals) })
        {
            bool single = implementation == typeof(WarpPortableBinary32Transcendentals);
            string semantics = single ? WarpPortableBinary32Transcendentals.Semantics : WarpPortableBinary64Transcendentals.Semantics;
            foreach (MethodInfo method in implementation.GetMethods(BindingFlags.Public | BindingFlags.Static).OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                WarpIntegerMapKernel verified = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(method, method.GetParameters().Length));
                WarpControlFlowKernel body = verified.ControlFlow;
                var identified = new WarpControlFlowKernel(body.Name + "/" + semantics, body.InputBufferCount,
                    body.ScalarArgumentCount, body.Blocks, body.Reduction, body.Functions);
                var layout = new WarpLogicalMachineLayout(identified);
                CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
                CheckExecutable(method, layout, executable, single, minimumQuantum);
                CheckBackendSource(layout);
                Console.WriteLine(method.Name + (single ? " binary32" : " binary64") + (minimumQuantum ? " minimum quantum passed." : " large quantum passed."));
            }
        }
    }

    [TestMethod]
    public void ClampSelectionAndFaultsMatchManagedBitsIncludingNaNPayloads()
    {
        ulong[] inputs = [0, 1, 0x3FF0000000000000, 0x7FEFFFFFFFFFFFFF, 0x7FF0000000000000,
            0x7FF8000000000001, 0x7FF0000000000001, 0x8000000000000000, 0xBFF0000000000000, 0xFFF0000000000000];
        foreach (ulong value in inputs)
        {
            foreach (ulong minimum in inputs)
            {
                foreach (ulong maximum in inputs) { CheckClamp(value, minimum, maximum); }
            }
        }

        CheckSingleClampEdges();

        foreach (MethodInfo method in typeof(WarpPortableNumericClamp).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            WarpIntegerMapKernel verified = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(method, method.GetParameters().Length));
            var layout = new WarpLogicalMachineLayout(verified.ControlFlow);
            CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
            foreach (int index in new[] { 0, 1, 2, 3 })
            {
                uint[][] operands = ClampInputs(method, index);
                foreach (int quantum in new[] { layout.MaximumBlockCost, 10000000 })
                {
                    uint[] state = layout.CreateInitialState(16, 10000000);
                    Run(executable, layout, state, operands, quantum);
                    Assert.AreEqual((uint)method.Invoke(null, operands.Select(value => (object)value[0]).ToArray())!, state[WarpLogicalMachineLayout.ResultOffset]);
                }
            }

            CheckBackendSource(layout);
        }
    }

    [TestMethod]
    public void CatalogBindsEveryAlgorithmVersionAndDeclaredPrecisionToArtifactIdentity()
    {
        IReadOnlyList<WarpLogicalMachineLayout> single = WarpPortableTranscendentalKernels.CreateBinary32Transcendentals();
        IReadOnlyList<WarpLogicalMachineLayout> wide = WarpPortableTranscendentalKernels.CreateBinary64Transcendentals();
        IReadOnlyList<WarpLogicalMachineLayout> clamp = WarpPortableTranscendentalKernels.CreateClamp();
        Assert.HasCount(20, single); Assert.HasCount(40, wide); Assert.HasCount(5, clamp);
        foreach (WarpLogicalMachineLayout layout in single.Concat(wide).Concat(clamp))
        {
            string semantics = single.Contains(layout) ? WarpPortableBinary32Transcendentals.Semantics :
                wide.Contains(layout) ? WarpPortableBinary64Transcendentals.Semantics : WarpPortableNumericClamp.Semantics;
            StringAssert.Contains(layout.Kernel.Name, semantics, StringComparison.Ordinal);
            WarpControlFlowKernel kernel = layout.Kernel;
            var renamed = new WarpControlFlowKernel(kernel.Name + "/different-algorithm", kernel.InputBufferCount,
                kernel.ScalarArgumentCount, kernel.Blocks, kernel.Reduction, kernel.Functions);
            Assert.AreNotEqual(WarpIrHash.Compute(kernel), WarpIrHash.Compute(renamed), StringComparer.Ordinal);
        }
    }

    private static void CheckExecutable(MethodInfo method, WarpLogicalMachineLayout layout,
        CoreCLRResumableKernel executable, bool single, bool minimumQuantum)
    {
        string name = single ? method.Name : method.Name.EndsWith("High", StringComparison.Ordinal) ? method.Name[..^4] : method.Name[..^3];
        var rows = WarpPortableTranscendentalWitnesses.Oracle.Where(value => value.Width == (single ? 32 : 64) && string.Equals(value.Name, name, StringComparison.Ordinal)).ToArray();
        int[] cases = [0, 1, 8, 9, 10, 14, 22, 65];
        uint[] state = layout.CreateInitialState(16, 1000000000);
        foreach (int index in cases)
        {
            var row = rows[index];
            object[] arguments = single ? method.GetParameters().Length == 1 ? [checked((uint)row.Left)] : [checked((uint)row.Left), checked((uint)row.Right)] :
                method.GetParameters().Length == 2 ? [unchecked((uint)row.Left), (uint)(row.Left >> 32)] :
                    [unchecked((uint)row.Left), (uint)(row.Left >> 32), unchecked((uint)row.Right), (uint)(row.Right >> 32)];
            uint[][] inputs = arguments.Cast<uint>().Select(value => new[] { value }).ToArray();
            layout.ResetState(state, 1000000000);
            Run(executable, layout, state, inputs, minimumQuantum ? layout.MaximumBlockCost : 1000000000);
            uint expected = (uint)method.Invoke(null, arguments)!;
            Assert.AreEqual(expected, state[WarpLogicalMachineLayout.ResultOffset], method.Name);
            CheckReference(WarpPortableTranscendentalOracle.Actual(name, row.Left, row.Right, single ? 32 : 64), row.Reference, single ? 32 : 64, name);
        }
    }

    private static void Run(CoreCLRResumableKernel executable, WarpLogicalMachineLayout layout, uint[] state, uint[][] inputs, int quantum)
    {
        for (int iteration = 0; state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; iteration++)
        {
            Assert.IsLessThan(10000000, iteration);
            executable.ExecuteQuantum(inputs, [], 0, state, 16, quantum);
        }

        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
    }

    private static void CheckReference(ulong actual, ulong reference, int width, string name)
    {
        ulong magnitudeMask = width == 32 ? 0x7FFFFFFFUL : 0x7FFFFFFFFFFFFFFFUL;
        ulong infinity = width == 32 ? 0x7F800000UL : 0x7FF0000000000000UL;
        if ((reference & magnitudeMask) > infinity) { Assert.AreEqual(width == 32 ? 0x7FC00000UL : 0x7FF8000000000000UL, actual, name); return; }
        if ((reference & magnitudeMask) == 0 || (reference & magnitudeMask) == infinity) { Assert.AreEqual(reference, actual, name); return; }
        Assert.IsLessThanOrEqualTo(width == 32 ? 2UL : 8UL, WarpPortableTranscendentalOracle.Distance(actual, reference, width),
            string.Create(CultureInfo.InvariantCulture, $"{name} {width}: actual {actual:X16}, high-precision {reference:X16}"));
    }

    private static void CheckBackendSource(WarpLogicalMachineLayout layout)
    {
        foreach (WarpBackendKind backend in WarpBackendCatalog.Required.Where(value => value != WarpBackendKind.CoreCLR))
        {
            string source = WarpPortableMachineEmitter.Emit(layout, backend);
            Assert.IsFalse(source.Contains("llvm.sin", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("llvm.cos", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("llvm.exp", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("llvm.log", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("llvm.sqrt", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("llvm.fma", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("fast ", StringComparison.Ordinal));
        }
    }

    private static uint[][] ClampInputs(MethodInfo method, int index)
    {
        bool single = method.Name.StartsWith("Binary32", StringComparison.Ordinal);
        ulong[] values = single ? [0x80000000, 0x7FC12345, 0x7F800001, 1] :
            [0x8000000000000000, 0x7FF8000000012345, 0x7FF0000000000001, 1];
        ulong[] minima = single ? [0xBF800000, 0, 0x3F800000, 0x7FC12345] :
            [0xBFF0000000000000, 0, 0x3FF0000000000000, 0x7FF8000000012345];
        ulong[] maxima = single ? [0x3F800000, 0x3F800000, 0xBF800000, 0x7FC12345] :
            [0x3FF0000000000000, 0x3FF0000000000000, 0xBFF0000000000000, 0x7FF8000000012345];
        bool fault = method.Name.EndsWith("Fault", StringComparison.Ordinal);
        ulong[] arguments = fault ? [minima[index], maxima[index]] : [values[index], minima[index], maxima[index]];
        return single ? arguments.Select(value => new[] { checked((uint)value) }).ToArray() :
            arguments.SelectMany(value => new[] { new[] { unchecked((uint)value) }, new[] { (uint)(value >> 32) } }).ToArray();
    }

    private static ulong NativeReference(string name, ulong left, ulong right, int width)
    {
        Type type = width == 32 ? typeof(MathF) : typeof(Math);
        Type argumentType = width == 32 ? typeof(float) : typeof(double);
        bool binary = name is "Atan2" or "Pow" or "LogBase";
        MethodInfo method = type.GetMethod(name is "LogBase" ? "Log" : name, BindingFlags.Public | BindingFlags.Static, null,
            binary ? [argumentType, argumentType] : [argumentType], null)!;
        object leftValue = width == 32 ? (object)BitConverter.UInt32BitsToSingle(checked((uint)left)) : BitConverter.UInt64BitsToDouble(left);
        object rightValue = width == 32 ? (object)BitConverter.UInt32BitsToSingle(checked((uint)right)) : BitConverter.UInt64BitsToDouble(right);
        object result = method.Invoke(null, binary ? [leftValue, rightValue] : [leftValue])!;
        if (width == 32)
        {
            float single = (float)result;
            return float.IsNaN(single) ? 0x7FC00000u : BitConverter.SingleToUInt32Bits(single);
        }

        double wide = (double)result;
        return double.IsNaN(wide) ? 0x7FF8000000000000 : BitConverter.DoubleToUInt64Bits(wide);
    }

    private static void CheckSingleClampEdges()
    {
        uint[] inputs = [0, 1, 0x3F800000, 0x7F7FFFFF, 0x7F800000, 0x7FC12345, 0x7F800001, 0xFF812345, 0x80000000, 0xBF800000, 0xFF800000];
        foreach (uint value in inputs)
        {
            foreach (uint minimum in inputs)
            {
                foreach (uint maximum in inputs)
                {
                    float minimumValue = BitConverter.UInt32BitsToSingle(minimum);
                    float maximumValue = BitConverter.UInt32BitsToSingle(maximum);
                    if (minimumValue > maximumValue)
                    {
                        Assert.AreEqual(3u, WarpPortableNumericClamp.Binary32Fault(minimum, maximum));
                        Assert.ThrowsExactly<ArgumentException>(() => Math.Clamp(BitConverter.UInt32BitsToSingle(value), minimumValue, maximumValue));
                    }
                    else
                    {
                        Assert.AreEqual(0u, WarpPortableNumericClamp.Binary32Fault(minimum, maximum));
                        Assert.AreEqual(BitConverter.SingleToUInt32Bits(Math.Clamp(BitConverter.UInt32BitsToSingle(value), minimumValue, maximumValue)),
                            WarpPortableNumericClamp.Binary32(value, minimum, maximum));
                    }
                }
            }
        }
    }

    private static void CheckClamp(ulong value, ulong minimum, ulong maximum)
    {
        uint fault = WarpPortableNumericClamp.Binary64Fault(unchecked((uint)minimum), (uint)(minimum >> 32), unchecked((uint)maximum), (uint)(maximum >> 32));
        double minimumValue = BitConverter.UInt64BitsToDouble(minimum);
        double maximumValue = BitConverter.UInt64BitsToDouble(maximum);
        if (minimumValue > maximumValue) { Assert.AreEqual(3u, fault); Assert.ThrowsExactly<ArgumentException>(() => Math.Clamp(BitConverter.UInt64BitsToDouble(value), minimumValue, maximumValue)); return; }
        Assert.AreEqual(0u, fault);
        ulong expected = BitConverter.DoubleToUInt64Bits(Math.Clamp(BitConverter.UInt64BitsToDouble(value), minimumValue, maximumValue));
        ulong actual = (ulong)WarpPortableNumericClamp.Binary64High(unchecked((uint)value), (uint)(value >> 32), unchecked((uint)minimum), (uint)(minimum >> 32), unchecked((uint)maximum), (uint)(maximum >> 32)) << 32 |
            WarpPortableNumericClamp.Binary64Low(unchecked((uint)value), (uint)(value >> 32), unchecked((uint)minimum), (uint)(minimum >> 32), unchecked((uint)maximum), (uint)(maximum >> 32));
        Assert.AreEqual(expected, actual);
        uint singleValue = WarpPortableNumericConversions.DoubleToSingle(unchecked((uint)value), (uint)(value >> 32));
        uint singleMinimum = WarpPortableNumericConversions.DoubleToSingle(unchecked((uint)minimum), (uint)(minimum >> 32));
        uint singleMaximum = WarpPortableNumericConversions.DoubleToSingle(unchecked((uint)maximum), (uint)(maximum >> 32));
        Assert.AreEqual(BitConverter.SingleToUInt32Bits(Math.Clamp(BitConverter.UInt32BitsToSingle(singleValue), BitConverter.UInt32BitsToSingle(singleMinimum), BitConverter.UInt32BitsToSingle(singleMaximum))),
            WarpPortableNumericClamp.Binary32(singleValue, singleMinimum, singleMaximum));
    }
}
