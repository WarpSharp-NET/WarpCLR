namespace WarpCLR.IR;

public static class WarpRuntimeAbi
{
    public const string Version = "warp.runtime-abi/0.1";

    public const string SafepointPolicy = "warp.block-charge/0.1";

    public const int DefaultMaximumCallDepth = 128;

    public const long DefaultMaximumStepsPerWorker = 100_000_000;
}
