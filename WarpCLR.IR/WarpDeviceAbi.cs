namespace WarpCLR.IR;

public static class WarpDeviceAbi
{
    public const string Version = "warp.device-abi/0.1";

    public const string IntegerMapEntryPoint = "warp_integer_map";

    public const string IntegerReductionEntryPoint = "warp_u32_reduce";

    public const string DevelopmentConformanceMarker = "warp.conformance=development-nonconforming";

    public const uint IntegerMapWorkgroupSize = 256;

    public static string GetEntryPoint(WarpLinearKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        return kernel.Reduction.HasValue ? IntegerReductionEntryPoint : IntegerMapEntryPoint;
    }
}
