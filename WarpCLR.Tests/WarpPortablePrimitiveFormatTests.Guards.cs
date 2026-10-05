using System.Globalization;
using WarpCLR.Compiler;

namespace WarpCLR.Tests;

internal sealed partial class WarpPortablePrimitiveFormatTests
{
    [TestMethod]
    public void CapturedNumericSymbolIdentityPreservesEveryUnpairedUtf16CodeUnit()
    {
        foreach (int slot in Enumerable.Range(0, 6))
        {
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (ushort codeUnit in new ushort[] { 0xD800, 0xD801, 0xDC00, 0xDC01, 0xFFFD })
            {
                string symbol = new((char)codeUnit, 1);
                NumberFormatInfo format = FormatWithSymbol(slot, symbol);
                WarpPortablePrimitiveFormatContract contract = WarpPortablePrimitiveFormatContract.Capture(format);
                Assert.IsTrue(identities.Add(contract.ContractHash), "Distinct raw UTF16 symbols must have distinct contract identities.");
                Assert.AreEqual(symbol, contract.Symbols[slot], StringComparer.Ordinal);
                uint[] arena = CreateArena(contract);
                Assert.IsTrue(contract.Matches(arena.AsSpan(Contract, contract.WordCount)));
            }
        }
    }

    private static NumberFormatInfo FormatWithSymbol(int slot, string symbol)
    {
        var format = new NumberFormatInfo();
        switch (slot)
        {
            case 0: format.NegativeSign = symbol; break;
            case 1: format.PositiveSign = symbol; break;
            case 2: format.NumberDecimalSeparator = symbol; break;
            case 3: format.NaNSymbol = symbol; break;
            case 4: format.PositiveInfinitySymbol = symbol; break;
            case 5: format.NegativeInfinitySymbol = symbol; break;
            default: throw new ArgumentOutOfRangeException(nameof(slot));
        }
        return format;
    }

    [TestMethod]
    public void CapturedCultureIsImmutableAndExactIncludingUtf16Nulls()
    {
        NumberFormatInfo format = CreateCustomFormat();
        WarpPortablePrimitiveFormatContract before = WarpPortablePrimitiveFormatContract.Capture(format);
        format.NegativeSign = "changed";
        WarpPortablePrimitiveFormatContract after = WarpPortablePrimitiveFormatContract.Capture(format);
        Assert.AreNotEqual(before.ContractHash, after.ContractHash, StringComparer.Ordinal);
        uint[] arena = CreateArena(before);
        Assert.IsTrue(before.Matches(arena.AsSpan(Contract, before.WordCount)));
        Assert.IsFalse(after.Matches(arena.AsSpan(Contract, before.WordCount)));
        arena[Contract + before.WordCount - 1] ^= 1;
        Assert.IsFalse(before.Matches(arena.AsSpan(Contract, before.WordCount)));
        format.NegativeSign = new string('x', 257);
        Assert.ThrowsExactly<ArgumentException>(() => WarpPortablePrimitiveFormatContract.Capture(format));
    }

    [TestMethod]
    public void InvalidShapeKindAndCapacityCannotPartiallyPublishText()
    {
        WarpPortablePrimitiveFormatContract contract = WarpPortablePrimitiveFormatContract.Capture(CultureInfo.InvariantCulture.NumberFormat);
        foreach ((uint kind, uint low, uint high, uint culture, uint scratch, uint output, uint capacity, uint error) in new (uint, uint, uint, uint, uint, uint, uint, uint)[]
        {
            (12, 0, 0, Contract, Scratch, Output, Capacity, WarpPortablePrimitiveFormatLayout.BadKind),
            (8, 0, 1, Contract, Scratch, Output, Capacity, WarpPortablePrimitiveFormatLayout.BadKind),
            (10, 65536, 0, Contract, Scratch, Output, Capacity, WarpPortablePrimitiveFormatLayout.BadKind),
            (11, 2, 0, Contract, Scratch, Output, Capacity, WarpPortablePrimitiveFormatLayout.BadKind),
            (5, 0, 0, uint.MaxValue, Scratch, Output, Capacity, WarpPortablePrimitiveFormatLayout.BadShape),
            (5, 0, 0, Contract, uint.MaxValue, Output, Capacity, WarpPortablePrimitiveFormatLayout.BadShape),
            (5, 0, 0, Contract, Scratch, uint.MaxValue, Capacity, WarpPortablePrimitiveFormatLayout.BadShape),
            (5, 0, 0, Contract, Scratch, Output, uint.MaxValue, WarpPortablePrimitiveFormatLayout.BadShape),
            (5, 0, 0, Contract, Scratch, Scratch, Capacity, WarpPortablePrimitiveFormatLayout.BadShape),
            (5, 0, 0, Contract, Contract, Output, Capacity, WarpPortablePrimitiveFormatLayout.BadShape),
            (5, uint.MaxValue, 0, Contract, Scratch, Output, 3, WarpPortablePrimitiveFormatLayout.Capacity),
        })
        {
            uint[] arena = CreateArena(contract), before = (uint[])arena.Clone();
            Assert.AreEqual(error, WarpPortablePrimitiveFormatServices.Format(arena, kind, low, high, culture, scratch, output, capacity));
            Assert.IsTrue(arena.AsSpan(Output, (int)Capacity).SequenceEqual(before.AsSpan(Output, (int)Capacity)));
            if (error != WarpPortablePrimitiveFormatLayout.Capacity) { Assert.IsTrue(arena.AsSpan().SequenceEqual(before)); }
        }
        uint[] malformed = CreateArena(contract), unchanged = (uint[])malformed.Clone();
        malformed[Contract + 4] = uint.MaxValue;
        unchanged[Contract + 4] = uint.MaxValue;
        Assert.AreEqual(WarpPortablePrimitiveFormatLayout.BadShape,
            WarpPortablePrimitiveFormatServices.Format(malformed, 5, 42, 0, Contract, Scratch, Output, Capacity));
        Assert.IsTrue(malformed.AsSpan().SequenceEqual(unchanged));
    }
}
