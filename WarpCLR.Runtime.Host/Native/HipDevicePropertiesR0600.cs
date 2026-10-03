namespace WarpCLR.Runtime.Host.Native;

internal static class HipDevicePropertiesR0600
{
    // Explicitly versioned ROCm hip_runtime_api.h R0600 layout for LP64/LLP64, not the obsolete unversioned struct.
    public const int Size = 1472;
    public const int TotalGlobalMemory = 288;
    public const int MaxThreadsPerBlock = 320;
    public const int MaxGridSize = 336;
    public const int GcnArchitecture = 1160;
}
