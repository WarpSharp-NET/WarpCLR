using System.Globalization;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests;

[TestClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed partial class WarpPortablePrimitiveFormatTests
{
    private const int Contract = 32;
    private const int Scratch = 2048;
    private const int Output = 3500;
    private const uint Capacity = 1024;

    [TestMethod]
    public void SeparateRawFloatingTypesMatchPinnedCoreClrGeneralFormat()
    {
        NumberFormatInfo format = CultureInfo.InvariantCulture.NumberFormat;
        WarpPortablePrimitiveFormatContract contract = WarpPortablePrimitiveFormatContract.Capture(format);
        uint[] arena = CreateArena(contract);
        foreach (uint bits in Floating32Witnesses())
        {
            VerifyDirect(arena, contract, format, WarpPortablePrimitiveFormatLayout.Single, bits, 0);
        }
        foreach (ulong bits in Floating64Witnesses())
        {
            VerifyDirect(arena, contract, format, WarpPortablePrimitiveFormatLayout.Double, (uint)bits, (uint)(bits >> 32));
        }
    }

    [TestMethod]
    public void IntegerWidthsAndExactUtf16SymbolsMatchCoreClr()
    {
        NumberFormatInfo format = CreateCustomFormat();
        WarpPortablePrimitiveFormatContract contract = WarpPortablePrimitiveFormatContract.Capture(format);
        uint[] arena = CreateArena(contract);
        foreach (uint kind in Enumerable.Range(0, 8).Select(value => (uint)value))
        {
            foreach (ulong raw in new ulong[] { 0, 1, 127, 128, 255, 256, 32767, 32768, 65535, 2147483647,
                2147483648, uint.MaxValue, 0x7FFFFFFFFFFFFFFF, 0x8000000000000000, ulong.MaxValue })
            {
                uint high = kind >= WarpPortablePrimitiveFormatLayout.Int64 ? (uint)(raw >> 32) : 0;
                VerifyDirect(arena, contract, format, kind, (uint)raw, high);
            }
        }
        foreach (uint bits in new uint[] { 0, 0x80000000, 1, 0x00800000, 0x3DCCCCCD, 0x4B189680, 0x7F7FFFFF,
            0x7F800000, 0xFF800000, 0x7FA12345, 0xFFC01234 })
        {
            VerifyDirect(arena, contract, format, WarpPortablePrimitiveFormatLayout.Single, bits, 0);
        }
        foreach (ulong bits in new ulong[] { 0, 0x8000000000000000, 1, 0x0010000000000000, 0x3FB999999999999A,
            0x4341C37937E08000, 0x7FEFFFFFFFFFFFFF, 0x7FF0000000000000, 0xFFF0000000000000, 0x7FF0000000012345 })
        {
            VerifyDirect(arena, contract, format, WarpPortablePrimitiveFormatLayout.Double, (uint)bits, (uint)(bits >> 32));
        }
        foreach (uint character in new uint[] { 0, 65, 0xD800, 0xDC00, 0xFFFF })
        {
            VerifyDirect(arena, contract, format, WarpPortablePrimitiveFormatLayout.Character, character, 0);
        }
        VerifyDirect(arena, contract, format, WarpPortablePrimitiveFormatLayout.Boolean, 0, 0);
        VerifyDirect(arena, contract, format, WarpPortablePrimitiveFormatLayout.Boolean, 1, 0);
    }

    [TestMethod]
    public void GeneratedMachineMatchesRawServiceAndCoreClrAtBothQuanta()
    {
        WarpLogicalMachineLayout layout = WarpPortablePrimitiveFormatKernels.Create();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        NumberFormatInfo format = CreateCustomFormat();
        WarpPortablePrimitiveFormatContract contract = WarpPortablePrimitiveFormatContract.Capture(format);
        foreach ((uint kind, ulong bits) in GeneratedWitnesses())
        {
            foreach (int quantum in new[] { layout.MaximumBlockCost, 1_000_000 })
            {
                uint[] expected = CreateArena(contract);
                uint status = WarpPortablePrimitiveFormatServices.Format(expected, kind, (uint)bits,
                    (uint)(bits >> 32), Contract, Scratch, Output, Capacity);
                uint[] actual = CreateArena(contract);
                uint[] state = layout.CreateInitialState(64, 1_000_000_000);
                uint[][] inputs = [[kind], [(uint)bits], [(uint)(bits >> 32)], [Contract], [Scratch], [Output], [Capacity]];
                int quanta = 0;
                do
                {
                    core.ExecuteManagedQuantum(inputs, [], 0, state, 64, quantum, actual);
                    Assert.IsLessThan(5_000_000, ++quanta, "Formatter must retire bounded generated work.");
                }
                while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable);
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
                Assert.AreEqual(status, state[WarpLogicalMachineLayout.ResultOffset]);
                Assert.IsTrue(actual.AsSpan().SequenceEqual(expected), $"Exact scratch/output bank, kind {kind}, raw {bits:X16}, quantum {quantum}.");
                Assert.AreEqual(Reference(kind, (uint)bits, (uint)(bits >> 32), format), ReadResult(actual), StringComparer.Ordinal);
            }
        }
    }

    private static uint[] CreateArena(WarpPortablePrimitiveFormatContract contract)
    {
        uint[] arena = Enumerable.Repeat(0xA5C39E71U, 4600).ToArray();
        contract.Install(arena, Contract);
        return arena;
    }

    private static void VerifyDirect(uint[] arena, WarpPortablePrimitiveFormatContract contract, NumberFormatInfo format, uint kind, uint low, uint high)
    {
        Assert.IsTrue(contract.Matches(arena.AsSpan(Contract, contract.WordCount)));
        Assert.AreEqual(0U, WarpPortablePrimitiveFormatServices.Format(arena, kind, low, high, Contract, Scratch, Output, Capacity));
        Assert.AreEqual(Reference(kind, low, high, format), ReadResult(arena), StringComparer.Ordinal, $"Kind {kind}, raw {high:X8}{low:X8}.");
        Assert.AreEqual(0xA5C39E71U, arena[Scratch - 1]);
        Assert.AreEqual(0xA5C39E71U, arena[Scratch + WarpPortablePrimitiveFormatLayout.ScratchWords]);
        Assert.AreEqual(0xA5C39E71U, arena[Output - 1]);
        Assert.AreEqual(0xA5C39E71U, arena[Output + Capacity]);
    }

    private static string ReadResult(uint[] arena) => new(Enumerable.Range(0, checked((int)arena[Scratch])).Select(index => (char)arena[Output + index]).ToArray());

    private static NumberFormatInfo CreateCustomFormat() => new()
    {
        NegativeSign = "−\0minus", PositiveSign = "＋plus", NumberDecimalSeparator = "٫sep",
        NaNSymbol = "NaN\0雪", PositiveInfinitySymbol = "∞plus", NegativeInfinitySymbol = "∞minus",
    };

    private static string Reference(uint kind, uint low, uint high, NumberFormatInfo format) => kind switch
    {
        WarpPortablePrimitiveFormatLayout.Int8 => unchecked((sbyte)low).ToString(format),
        WarpPortablePrimitiveFormatLayout.UInt8 => unchecked((byte)low).ToString(format),
        WarpPortablePrimitiveFormatLayout.Int16 => unchecked((short)low).ToString(format),
        WarpPortablePrimitiveFormatLayout.UInt16 => unchecked((ushort)low).ToString(format),
        WarpPortablePrimitiveFormatLayout.Int32 => unchecked((int)low).ToString(format),
        WarpPortablePrimitiveFormatLayout.UInt32 => low.ToString(format),
        WarpPortablePrimitiveFormatLayout.Int64 => unchecked((long)((ulong)high << 32 | low)).ToString(format),
        WarpPortablePrimitiveFormatLayout.UInt64 => ((ulong)high << 32 | low).ToString(format),
        WarpPortablePrimitiveFormatLayout.Single => BitConverter.UInt32BitsToSingle(low).ToString(null, format),
        WarpPortablePrimitiveFormatLayout.Double => BitConverter.UInt64BitsToDouble((ulong)high << 32 | low).ToString(null, format),
        WarpPortablePrimitiveFormatLayout.Character => ((char)low).ToString(),
        WarpPortablePrimitiveFormatLayout.Boolean => (low != 0).ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
