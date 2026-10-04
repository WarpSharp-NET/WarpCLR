namespace WarpCLR.Compiler;

internal static partial class WarpPortableInteger64
{
    public static uint ConvertToSigned32Fault(uint low, uint high, uint sourceUnsigned)
    {
        bool valid = sourceUnsigned != 0 ? high == 0 && low <= 0x7FFFFFFFu : high == WarpPortableInteger32.ExtendSignedHigh(low);
        return valid ? WarpPortableIntegerFault.None : WarpPortableIntegerFault.Overflow;
    }

    public static uint ConvertToUnsigned32Fault(uint low, uint high) =>
        high == 0 ? WarpPortableIntegerFault.None : WarpPortableIntegerFault.Overflow;

    public static uint ConvertToSigned8Fault(uint low, uint high, uint sourceUnsigned) =>
        ConvertToSigned32Fault(low, high, sourceUnsigned) == WarpPortableIntegerFault.None ?
            WarpPortableInteger32.ConvertToSigned8Fault(low, sourceUnsigned) : WarpPortableIntegerFault.Overflow;

    public static uint ConvertToSigned16Fault(uint low, uint high, uint sourceUnsigned) =>
        ConvertToSigned32Fault(low, high, sourceUnsigned) == WarpPortableIntegerFault.None ?
            WarpPortableInteger32.ConvertToSigned16Fault(low, sourceUnsigned) : WarpPortableIntegerFault.Overflow;

    public static uint ConvertToUnsigned8Fault(uint low, uint high) =>
        high == 0 ? WarpPortableInteger32.ConvertToUnsigned8Fault(low) : WarpPortableIntegerFault.Overflow;

    public static uint ConvertToUnsigned16Fault(uint low, uint high) =>
        high == 0 ? WarpPortableInteger32.ConvertToUnsigned16Fault(low) : WarpPortableIntegerFault.Overflow;

    public static uint ConvertUnsignedToSignedFault(uint low, uint high) =>
        (high >> 31) != 0 ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;

    public static uint ConvertSignedToUnsignedFault(uint low, uint high) => ConvertUnsignedToSignedFault(low, high);
}
