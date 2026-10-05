namespace WarpCLR.Compiler;

internal static partial class WarpPortablePrimitiveFormatServices
{
    public static uint Format(uint[] arena, uint kind, uint low, uint high, uint contract, uint scratch, uint output, uint capacity)
    {
        uint validation = Validate(arena, contract, scratch, output, capacity);
        if (validation != 0) { return validation; }
        if (kind > WarpPortablePrimitiveFormatLayout.Boolean ||
            kind != WarpPortablePrimitiveFormatLayout.Int64 && kind != WarpPortablePrimitiveFormatLayout.UInt64 &&
            kind != WarpPortablePrimitiveFormatLayout.Double && high != 0 ||
            kind == WarpPortablePrimitiveFormatLayout.Character && low > 0xFFFF ||
            kind == WarpPortablePrimitiveFormatLayout.Boolean && low > 1)
        { return WarpPortablePrimitiveFormatLayout.BadKind; }
        for (uint word = 0; word < WarpPortablePrimitiveFormatLayout.ScratchWords; word++) { arena[scratch + word] = 0; }
        if (kind <= WarpPortablePrimitiveFormatLayout.UInt64)
        {
            IntegerDigits(arena, kind, low, high, scratch);
            RenderInteger(arena, contract, scratch);
        }
        else if (kind <= WarpPortablePrimitiveFormatLayout.Double)
        {
            uint status = Floating(arena, kind, low, high, contract, scratch);
            if (status != 0) { return status; }
        }
        else if (kind == WarpPortablePrimitiveFormatLayout.Character) { Append(arena, scratch, low); }
        else { RenderBoolean(arena, scratch, low); }
        uint length = arena[scratch];
        if (length > capacity) { return WarpPortablePrimitiveFormatLayout.Capacity; }
        for (uint word = 0; word < length; word++) { arena[output + word] = arena[scratch + WarpPortablePrimitiveFormatLayout.Render + word]; }
        return 0;
    }

    private static uint Floating(uint[] arena, uint kind, uint low, uint high, uint contract, uint scratch)
    {
        uint classification = Decode(arena, kind, low, high, scratch);
        if (classification >= 3) { Symbol(arena, contract, scratch, classification); return 0; }
        if (classification == 1)
        {
            arena[scratch + 11] = 1;
            arena[scratch + WarpPortablePrimitiveFormatLayout.Digits] = 0;
            arena[scratch + 12] = 0;
        }
        else if (PinnedCoreClrDoubleFallback(arena, kind, scratch) == 0)
        {
            uint status = InitializeFloat(arena, kind, scratch);
            if (status == 0) { status = NormalizeFloat(arena, scratch); }
            if (status == 0) { status = GenerateFloatDigits(arena, scratch); }
            if (status != 0) { return status; }
        }
        RenderFloat(arena, contract, scratch);
        return 0;
    }

    private static uint RenderBoolean(uint[] arena, uint scratch, uint value)
    {
        if (value != 0)
        { Append(arena, scratch, 84); Append(arena, scratch, 114); Append(arena, scratch, 117); Append(arena, scratch, 101); }
        else
        { Append(arena, scratch, 70); Append(arena, scratch, 97); Append(arena, scratch, 108); Append(arena, scratch, 115); Append(arena, scratch, 101); }
        return 0;
    }
}
