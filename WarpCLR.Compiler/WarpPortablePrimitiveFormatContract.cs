using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace WarpCLR.Compiler;

internal sealed class WarpPortablePrimitiveFormatContract
{
    private readonly ImmutableArray<uint> words;

    private WarpPortablePrimitiveFormatContract(ImmutableArray<string> symbols)
    {
        Symbols = symbols;
        ContractHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            WarpPortablePrimitiveFormatLayout.CultureSemantics,
            WarpPortablePrimitiveFormatLayout.Semantics,
            WarpPortablePrimitiveFormatLayout.CoreClrFallbackCorpusSha256,
            Utf16Symbols = symbols.Select(symbol => symbol.Select(character => (uint)character).ToImmutableArray()).ToImmutableArray(),
        })));
        int length = checked((int)WarpPortablePrimitiveFormatLayout.ContractHeader + symbols.Sum(symbol => symbol.Length));
        uint[] data = new uint[length];
        data[0] = WarpPortablePrimitiveFormatLayout.Magic;
        data[1] = checked((uint)length);
        data[2] = 1;
        int cursor = checked((int)WarpPortablePrimitiveFormatLayout.ContractHeader);
        for (int index = 0; index < symbols.Length; index++)
        {
            data[3 + index * 2] = checked((uint)cursor);
            data[4 + index * 2] = checked((uint)symbols[index].Length);
            foreach (char character in symbols[index]) { data[cursor++] = character; }
        }
        byte[] hash = Convert.FromHexString(ContractHash);
        for (int word = 0; word < 8; word++)
        {
            data[15 + word] = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(word * 4, 4));
        }
        words = ImmutableArray.CreateRange(data);
    }

    internal string ContractHash { get; }
    internal ImmutableArray<string> Symbols { get; }
    internal int WordCount => words.Length;

    internal static WarpPortablePrimitiveFormatContract Capture(NumberFormatInfo format)
    {
        ArgumentNullException.ThrowIfNull(format);
        NumberFormatInfo snapshot = NumberFormatInfo.ReadOnly((NumberFormatInfo)format.Clone());
        ImmutableArray<string> symbols = [snapshot.NegativeSign, snapshot.PositiveSign, snapshot.NumberDecimalSeparator,
            snapshot.NaNSymbol, snapshot.PositiveInfinitySymbol, snapshot.NegativeInfinitySymbol];
        if (symbols.Any(symbol => symbol.Length > WarpPortablePrimitiveFormatLayout.SymbolLimit))
        {
            throw new ArgumentException("Numeric symbols exceed the common formatter's explicit resource bound.", nameof(format));
        }
        return new(symbols);
    }

    internal void Install(uint[] arena, int offset)
    {
        ArgumentNullException.ThrowIfNull(arena);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (offset > arena.Length || words.Length > arena.Length - offset)
        {
            throw new ArgumentException("The exact captured numeric contract does not fit its admitted bank.", nameof(arena));
        }
        words.AsSpan().CopyTo(arena.AsSpan(offset));
    }

    internal bool Matches(ReadOnlySpan<uint> bank) => bank.Length == words.Length && bank.SequenceEqual(words.AsSpan());
}
