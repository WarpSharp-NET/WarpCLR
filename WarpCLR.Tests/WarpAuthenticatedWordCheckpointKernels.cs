namespace WarpCLR.Tests.Production;

internal static class WarpAuthenticatedWordCheckpointKernels
{
    public static uint Calculate(uint value) => unchecked(value * 3 + 1);
}
