using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpIntegerSemanticTests
{
    private static readonly ulong[] Edges =
    [
        0, 1, 2, 0x7F, 0x80, 0xFF, 0x100, 0x7FFF, 0x8000, 0xFFFF, 0x10000,
        0x7FFFFFFF, 0x80000000, 0xFFFFFFFF, 0x100000000,
        0x7FFFFFFFFFFFFFFF, 0x8000000000000000, 0x8000000000000001, 0xFFFFFFFFFFFFFFFE, 0xFFFFFFFFFFFFFFFF,
    ];
    private static readonly uint[] ShiftDistances = [0, 1, 7, 15, 16, 31, 32, 33, 63, 64, 65, 127, 128, 0xFFFFFFFF];
    private static readonly Lazy<CompiledPrimitives> Integer32 = new(() => new CompiledPrimitives(WarpPortableIntegerKernels.Create32()));
    private static readonly Lazy<CompiledPrimitives> Integer64 = new(() => new CompiledPrimitives(WarpPortableIntegerKernels.Create64()));

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompiledInteger64ArithmeticAndDivisionPreserveAllBits(bool minimumQuantum)
    {
        foreach ((ulong left, ulong right) in Operands())
        {
            uint[] inputs = Words(left, right);
            CompiledPrimitives compiled = Integer64.Value;
            Assert.AreEqual(unchecked(left + right), Result64(compiled, "Add", inputs, minimumQuantum));
            Assert.AreEqual(unchecked(left - right), Result64(compiled, "Subtract", inputs, minimumQuantum));
            Assert.AreEqual(unchecked(left * right), Result64(compiled, "Multiply", inputs, minimumQuantum));
            Assert.AreEqual(left < right ? 1u : 0u, compiled.Execute(nameof(WarpPortableInteger64.LessThanUnsigned), inputs, minimumQuantum));
            Assert.AreEqual(unchecked((long)left) < unchecked((long)right) ? 1u : 0u,
                compiled.Execute(nameof(WarpPortableInteger64.LessThanSigned), inputs, minimumQuantum));
            Assert.AreEqual(left == right ? 1u : 0u, compiled.Execute(nameof(WarpPortableInteger64.Equal), inputs, minimumQuantum));

            uint unsignedFault = right == 0 ? WarpPortableIntegerFault.DivideByZero : WarpPortableIntegerFault.None;
            Assert.AreEqual(unsignedFault, compiled.Execute(nameof(WarpPortableInteger64.DivideUnsignedFault), inputs, minimumQuantum));
            Assert.AreEqual(right == 0 ? 0 : left / right, Result64(compiled, "DivideUnsigned", inputs, minimumQuantum));
            Assert.AreEqual(right == 0 ? 0 : left % right, Result64(compiled, "RemainderUnsigned", inputs, minimumQuantum));

            long signedLeft = unchecked((long)left);
            long signedRight = unchecked((long)right);
            uint signedFault = right == 0 ? WarpPortableIntegerFault.DivideByZero :
                signedLeft == long.MinValue && signedRight == -1 ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;
            Assert.AreEqual(signedFault, compiled.Execute(nameof(WarpPortableInteger64.DivideSignedFault), inputs, minimumQuantum));
            ulong quotient = signedFault == WarpPortableIntegerFault.None ? unchecked((ulong)(signedLeft / signedRight)) : 0;
            ulong remainder = signedFault == WarpPortableIntegerFault.None ? unchecked((ulong)(signedLeft % signedRight)) : 0;
            Assert.AreEqual(quotient, Result64(compiled, "DivideSigned", inputs, minimumQuantum));
            Assert.AreEqual(remainder, Result64(compiled, "RemainderSigned", inputs, minimumQuantum));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompiledInteger32ArithmeticAndDivisionPreserveSignedInterpretation(bool minimumQuantum)
    {
        foreach ((ulong wideLeft, ulong wideRight) in Operands())
        {
            uint left = unchecked((uint)wideLeft);
            uint right = unchecked((uint)wideRight);
            uint[] inputs = [left, right];
            CompiledPrimitives compiled = Integer32.Value;
            Assert.AreEqual(unchecked(left + right), compiled.Execute(nameof(WarpPortableInteger32.Add), inputs, minimumQuantum));
            Assert.AreEqual(unchecked(left - right), compiled.Execute(nameof(WarpPortableInteger32.Subtract), inputs, minimumQuantum));
            Assert.AreEqual(unchecked(left * right), compiled.Execute(nameof(WarpPortableInteger32.Multiply), inputs, minimumQuantum));
            Assert.AreEqual(left < right ? 1u : 0u, compiled.Execute(nameof(WarpPortableInteger32.LessThanUnsigned), inputs, minimumQuantum));
            Assert.AreEqual(unchecked((int)left) < unchecked((int)right) ? 1u : 0u,
                compiled.Execute(nameof(WarpPortableInteger32.LessThanSigned), inputs, minimumQuantum));
            uint unsignedFault = right == 0 ? WarpPortableIntegerFault.DivideByZero : WarpPortableIntegerFault.None;
            Assert.AreEqual(unsignedFault, compiled.Execute(nameof(WarpPortableInteger32.DivideUnsignedFault), inputs, minimumQuantum));
            Assert.AreEqual(right == 0 ? 0u : left / right, compiled.Execute(nameof(WarpPortableInteger32.DivideUnsigned), inputs, minimumQuantum));
            Assert.AreEqual(right == 0 ? 0u : left % right, compiled.Execute(nameof(WarpPortableInteger32.RemainderUnsigned), inputs, minimumQuantum));

            int signedLeft = unchecked((int)left);
            int signedRight = unchecked((int)right);
            uint signedFault = right == 0 ? WarpPortableIntegerFault.DivideByZero :
                signedLeft == int.MinValue && signedRight == -1 ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;
            Assert.AreEqual(signedFault, compiled.Execute(nameof(WarpPortableInteger32.DivideSignedFault), inputs, minimumQuantum));
            uint quotient = signedFault == WarpPortableIntegerFault.None ? unchecked((uint)(signedLeft / signedRight)) : 0;
            uint remainder = signedFault == WarpPortableIntegerFault.None ? unchecked((uint)(signedLeft % signedRight)) : 0;
            Assert.AreEqual(quotient, compiled.Execute(nameof(WarpPortableInteger32.DivideSigned), inputs, minimumQuantum));
            Assert.AreEqual(remainder, compiled.Execute(nameof(WarpPortableInteger32.RemainderSigned), inputs, minimumQuantum));
        }
    }

    [TestMethod]
    public void CompiledCheckedArithmeticMatchesUnboundedIntegerRangeOracle()
    {
        foreach ((ulong left, ulong right) in Operands())
        {
            BigInteger unsignedLeft = left;
            BigInteger unsignedRight = right;
            BigInteger signedLeft = unchecked((long)left);
            BigInteger signedRight = unchecked((long)right);
            uint[] inputs = Words(left, right);
            AssertFault64(nameof(WarpPortableInteger64.AddUnsignedFault), unsignedLeft + unsignedRight, 0, ulong.MaxValue, inputs);
            AssertFault64(nameof(WarpPortableInteger64.SubtractUnsignedFault), unsignedLeft - unsignedRight, 0, ulong.MaxValue, inputs);
            AssertFault64(nameof(WarpPortableInteger64.MultiplyUnsignedFault), unsignedLeft * unsignedRight, 0, ulong.MaxValue, inputs);
            AssertFault64(nameof(WarpPortableInteger64.AddSignedFault), signedLeft + signedRight, long.MinValue, long.MaxValue, inputs);
            AssertFault64(nameof(WarpPortableInteger64.SubtractSignedFault), signedLeft - signedRight, long.MinValue, long.MaxValue, inputs);
            AssertFault64(nameof(WarpPortableInteger64.MultiplySignedFault), signedLeft * signedRight, long.MinValue, long.MaxValue, inputs);

            uint narrowLeft = unchecked((uint)left);
            uint narrowRight = unchecked((uint)right);
            unsignedLeft = narrowLeft;
            unsignedRight = narrowRight;
            signedLeft = unchecked((int)narrowLeft);
            signedRight = unchecked((int)narrowRight);
            uint[] narrowInputs = [narrowLeft, narrowRight];
            AssertFault32(nameof(WarpPortableInteger32.AddUnsignedFault), unsignedLeft + unsignedRight, 0, uint.MaxValue, narrowInputs);
            AssertFault32(nameof(WarpPortableInteger32.SubtractUnsignedFault), unsignedLeft - unsignedRight, 0, uint.MaxValue, narrowInputs);
            AssertFault32(nameof(WarpPortableInteger32.MultiplyUnsignedFault), unsignedLeft * unsignedRight, 0, uint.MaxValue, narrowInputs);
            AssertFault32(nameof(WarpPortableInteger32.AddSignedFault), signedLeft + signedRight, int.MinValue, int.MaxValue, narrowInputs);
            AssertFault32(nameof(WarpPortableInteger32.SubtractSignedFault), signedLeft - signedRight, int.MinValue, int.MaxValue, narrowInputs);
            AssertFault32(nameof(WarpPortableInteger32.MultiplySignedFault), signedLeft * signedRight, int.MinValue, int.MaxValue, narrowInputs);
        }
    }

    [TestMethod]
    public void CompiledMaskedShiftsNegationAndExtensionRetainWidthAndSign()
    {
        foreach (ulong value in Edges)
        {
            uint low = unchecked((uint)value);
            uint high = (uint)(value >> 32);
            uint[] wide = [low, high];
            Assert.AreEqual(unchecked(0ul - value), Result64(Integer64.Value, "Negate", wide, minimumQuantum: true));
            Assert.AreEqual(value == 0x8000000000000000 ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None,
                Integer64.Value.Execute(nameof(WarpPortableInteger64.NegateSignedFault), wide, minimumQuantum: true));
            Assert.AreEqual(unchecked(0u - low), Integer32.Value.Execute(nameof(WarpPortableInteger32.Negate), [low], minimumQuantum: true));
            Assert.AreEqual(low == 0x80000000u ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None,
                Integer32.Value.Execute(nameof(WarpPortableInteger32.NegateSignedFault), [low], minimumQuantum: true));
            Assert.AreEqual(low & 0xFFu, Integer32.Value.Execute(nameof(WarpPortableInteger32.ExtendUnsigned8), [low], minimumQuantum: true));
            Assert.AreEqual(low & 0xFFFFu, Integer32.Value.Execute(nameof(WarpPortableInteger32.ExtendUnsigned16), [low], minimumQuantum: true));
            Assert.AreEqual(unchecked((uint)(sbyte)low), Integer32.Value.Execute(nameof(WarpPortableInteger32.ExtendSigned8), [low], minimumQuantum: true));
            Assert.AreEqual(unchecked((uint)(short)low), Integer32.Value.Execute(nameof(WarpPortableInteger32.ExtendSigned16), [low], minimumQuantum: true));
            Assert.AreEqual(low >> 31 == 0 ? 0u : uint.MaxValue,
                Integer32.Value.Execute(nameof(WarpPortableInteger32.ExtendSignedHigh), [low], minimumQuantum: true));

            foreach (uint distance in ShiftDistances)
            {
                uint[] shiftInputs = [low, high, distance];
                Assert.AreEqual(value << unchecked((int)distance), Result64(Integer64.Value, "ShiftLeft", shiftInputs, minimumQuantum: true));
                Assert.AreEqual(value >> unchecked((int)distance), Result64(Integer64.Value, "ShiftRightUnsigned", shiftInputs, minimumQuantum: true));
                Assert.AreEqual(unchecked((ulong)(unchecked((long)value) >> unchecked((int)distance))),
                    Result64(Integer64.Value, "ShiftRightSigned", shiftInputs, minimumQuantum: true));
                Assert.AreEqual(low << unchecked((int)distance),
                    Integer32.Value.Execute(nameof(WarpPortableInteger32.ShiftLeft), [low, distance], minimumQuantum: true));
                Assert.AreEqual(low >> unchecked((int)distance),
                    Integer32.Value.Execute(nameof(WarpPortableInteger32.ShiftRightUnsigned), [low, distance], minimumQuantum: true));
                Assert.AreEqual(unchecked((uint)(unchecked((int)low) >> unchecked((int)distance))),
                    Integer32.Value.Execute(nameof(WarpPortableInteger32.ShiftRightSigned), [low, distance], minimumQuantum: true));
            }
        }
    }

    [TestMethod]
    public void CompiledCheckedConversionsUseSourceSignednessAndExactDestinationRange()
    {
        foreach (ulong value in Edges.Concat(Edges.Select(item => unchecked(0ul - item))))
        {
            uint low = unchecked((uint)value);
            uint high = (uint)(value >> 32);
            uint[] unsignedSource = [low, high, 1];
            uint[] signedSource = [low, high, 0];
            BigInteger unsigned = value;
            BigInteger signed = unchecked((long)value);
            AssertFault64(nameof(WarpPortableInteger64.ConvertToSigned8Fault), unsigned, sbyte.MinValue, sbyte.MaxValue, unsignedSource);
            AssertFault64(nameof(WarpPortableInteger64.ConvertToSigned8Fault), signed, sbyte.MinValue, sbyte.MaxValue, signedSource);
            AssertFault64(nameof(WarpPortableInteger64.ConvertToSigned16Fault), unsigned, short.MinValue, short.MaxValue, unsignedSource);
            AssertFault64(nameof(WarpPortableInteger64.ConvertToSigned16Fault), signed, short.MinValue, short.MaxValue, signedSource);
            AssertFault64(nameof(WarpPortableInteger64.ConvertToSigned32Fault), unsigned, int.MinValue, int.MaxValue, unsignedSource);
            AssertFault64(nameof(WarpPortableInteger64.ConvertToSigned32Fault), signed, int.MinValue, int.MaxValue, signedSource);
            AssertFault64(nameof(WarpPortableInteger64.ConvertToUnsigned8Fault), unsigned, 0, byte.MaxValue, [low, high]);
            AssertFault64(nameof(WarpPortableInteger64.ConvertToUnsigned16Fault), unsigned, 0, ushort.MaxValue, [low, high]);
            AssertFault64(nameof(WarpPortableInteger64.ConvertToUnsigned32Fault), unsigned, 0, uint.MaxValue, [low, high]);
            AssertFault64(nameof(WarpPortableInteger64.ConvertUnsignedToSignedFault), unsigned, long.MinValue, long.MaxValue, [low, high]);
            AssertFault64(nameof(WarpPortableInteger64.ConvertSignedToUnsignedFault), signed, 0, ulong.MaxValue, [low, high]);

            BigInteger narrowUnsigned = low;
            BigInteger narrowSigned = unchecked((int)low);
            AssertFault32(nameof(WarpPortableInteger32.ConvertToSigned8Fault), narrowUnsigned, sbyte.MinValue, sbyte.MaxValue, [low, 1]);
            AssertFault32(nameof(WarpPortableInteger32.ConvertToSigned8Fault), narrowSigned, sbyte.MinValue, sbyte.MaxValue, [low, 0]);
            AssertFault32(nameof(WarpPortableInteger32.ConvertToSigned16Fault), narrowUnsigned, short.MinValue, short.MaxValue, [low, 1]);
            AssertFault32(nameof(WarpPortableInteger32.ConvertToSigned16Fault), narrowSigned, short.MinValue, short.MaxValue, [low, 0]);
            AssertFault32(nameof(WarpPortableInteger32.ConvertToUnsigned8Fault), narrowUnsigned, 0, byte.MaxValue, [low]);
            AssertFault32(nameof(WarpPortableInteger32.ConvertToUnsigned16Fault), narrowUnsigned, 0, ushort.MaxValue, [low]);
            AssertFault32(nameof(WarpPortableInteger32.ConvertUnsignedToSignedFault), narrowUnsigned, int.MinValue, int.MaxValue, [low]);
            AssertFault32(nameof(WarpPortableInteger32.ConvertSignedToUnsignedFault), narrowSigned, 0, uint.MaxValue, [low]);
        }
    }

    [TestMethod]
    public void EveryIntegerPrimitiveVerifiesAndEmitsTheSameWordProgramForAllNativeTargets()
    {
        IReadOnlyList<WarpLogicalMachineLayout>[] families = [WarpPortableIntegerKernels.Create32(), WarpPortableIntegerKernels.Create64()];
        foreach (IReadOnlyList<WarpLogicalMachineLayout> family in families)
        {
            Assert.IsGreaterThan(30, family.Count);
            foreach (WarpLogicalMachineLayout layout in family)
            {
                Assert.IsGreaterThan(0, layout.Kernel.InputBufferCount);
                foreach (WarpBackendKind backend in WarpBackendCatalog.Required.Where(item => item != WarpBackendKind.CoreCLR))
                {
                    string source = WarpPortableMachineEmitter.Emit(layout, backend);
                    Assert.IsFalse(source.Contains("fadd", StringComparison.Ordinal));
                    Assert.IsFalse(source.Contains("fmul", StringComparison.Ordinal));
                    Assert.IsFalse(source.Contains("fdiv", StringComparison.Ordinal));
                    Assert.IsFalse(source.Contains("sdiv", StringComparison.Ordinal));
                    Assert.IsFalse(source.Contains("udiv", StringComparison.Ordinal));
                    Assert.IsFalse(source.Contains("srem", StringComparison.Ordinal));
                    Assert.IsFalse(source.Contains("urem", StringComparison.Ordinal));
                }
            }
        }
    }

    private static void AssertFault64(string name, BigInteger value, BigInteger minimum, BigInteger maximum, uint[] inputs) =>
        Assert.AreEqual(value < minimum || value > maximum ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None,
            Integer64.Value.Execute(name, inputs, minimumQuantum: true), name);

    private static void AssertFault32(string name, BigInteger value, BigInteger minimum, BigInteger maximum, uint[] inputs) =>
        Assert.AreEqual(value < minimum || value > maximum ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None,
            Integer32.Value.Execute(name, inputs, minimumQuantum: true), name);

    private static ulong Result64(CompiledPrimitives compiled, string operation, uint[] inputs, bool minimumQuantum) =>
        (ulong)compiled.Execute(operation + "High", inputs, minimumQuantum) << 32 |
        compiled.Execute(operation + "Low", inputs, minimumQuantum);

    private static uint[] Words(ulong left, ulong right) =>
        [unchecked((uint)left), (uint)(left >> 32), unchecked((uint)right), (uint)(right >> 32)];

    private static IEnumerable<(ulong Left, ulong Right)> Operands()
    {
        foreach (ulong left in Edges)
        {
            foreach (ulong right in Edges)
            {
                yield return (left, right);
            }
        }

        ulong state = 0x34454BE772A1BFF5;
        for (int index = 0; index < 512; index++)
        {
            ulong left = Next(ref state);
            yield return (left, Next(ref state));
        }
    }

    private static ulong Next(ref ulong value)
    {
        value ^= value << 13;
        value ^= value >> 7;
        value ^= value << 17;
        return value;
    }

    private sealed class CompiledPrimitives
    {
        private readonly Dictionary<string, CoreCLRResumableKernel> executables = new(StringComparer.Ordinal);

        public CompiledPrimitives(IReadOnlyList<WarpLogicalMachineLayout> layouts)
        {
            foreach (WarpLogicalMachineLayout layout in layouts)
            {
                string identity = layout.Kernel.Name[..layout.Kernel.Name.IndexOf('/', StringComparison.Ordinal)];
                string method = identity[(identity.LastIndexOf('.') + 1)..];
                executables.Add(method, CoreCLRResumableKernel.Compile(layout));
            }
        }

        public uint Execute(string name, uint[] values, bool minimumQuantum)
        {
            CoreCLRResumableKernel executable = executables[name];
            uint[][] inputs = new uint[values.Length][];
            for (int index = 0; index < values.Length; index++)
            {
                inputs[index] = [values[index]];
            }

            uint[] state = executable.Layout.CreateInitialState(16, 1000000);
            int quantum = minimumQuantum ? executable.Layout.MaximumBlockCost : 1000000;
            for (int iteration = 0; state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; iteration++)
            {
                Assert.IsLessThan(10000, iteration);
                executable.ExecuteQuantum(inputs, [], 0, state, 16, quantum);
            }

            Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset], name);
            return state[WarpLogicalMachineLayout.ResultOffset];
        }
    }
}
