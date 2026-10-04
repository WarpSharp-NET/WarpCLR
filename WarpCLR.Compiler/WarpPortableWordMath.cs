namespace WarpCLR.Compiler;

internal static class WarpPortableWordMath
{
    public static uint MultiplyHigh(uint left, uint right)
    {
        uint leftLow = left & 0xFFFFu;
        uint rightLow = right & 0xFFFFu;
        uint leftHigh = left >> 16;
        uint rightHigh = right >> 16;
        uint bottom = leftLow * rightLow;
        uint cross = leftHigh * rightLow + (bottom >> 16);
        uint middle = (cross & 0xFFFFu) + leftLow * rightHigh;
        return leftHigh * rightHigh + (cross >> 16) + (middle >> 16);
    }
}
