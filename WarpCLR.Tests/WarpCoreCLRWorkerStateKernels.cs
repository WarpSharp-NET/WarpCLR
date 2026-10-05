namespace WarpCLR.Tests;

internal static class WarpCoreCLRWorkerStateKernels
{
    public static uint StateWords(uint[] words, uint index, uint value)
    {
        words[index] ^= value;
        return words[index] + (uint)words.Length;
    }

    public static uint Increment(uint[] words)
    {
        words[0]++;
        return words[0];
    }
}
