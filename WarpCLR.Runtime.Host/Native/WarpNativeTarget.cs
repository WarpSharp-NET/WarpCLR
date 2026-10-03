using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

internal sealed record WarpNativeTarget
{
    public WarpNativeTarget(
        WarpBackendKind backend,
        string architecture,
        string deviceIdentity,
        string runtimeIdentity,
        uint maxWorkgroupSize,
        uint maxGridX,
        ulong globalMemoryBytes,
        uint maximumKernelArgumentBytes = 4096)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(architecture);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentity);
        string pattern = backend switch
        {
            WarpBackendKind.NVPTX => @"\Asm_[0-9]{2,3}\z",
            WarpBackendKind.AMDGPU => @"\Agfx[0-9a-f]{3,4}(:(xnack|sramecc)[+-]){0,2}\z",
            WarpBackendKind.SPIRV => @"\Aopencl2\.2-spirv1\.2\z",
            _ => throw new ArgumentException("A native GPU target cannot select a CPU backend.", nameof(backend)),
        };
        if (!Regex.IsMatch(architecture, pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1)))
        {
            throw new ArgumentException("The concrete architecture is invalid for the backend.", nameof(architecture));
        }

        string[] architectureOptions = architecture.Split(':').Skip(1).Select(option => option[..^1]).ToArray();
        if (architectureOptions.Distinct(StringComparer.Ordinal).Count() != architectureOptions.Length)
        {
            throw new ArgumentException("The concrete architecture repeats a target option.", nameof(architecture));
        }

        if (maxWorkgroupSize == 0 || maxGridX == 0 || globalMemoryBytes == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWorkgroupSize), "Device resource limits must be positive.");
        }

        ArgumentOutOfRangeException.ThrowIfZero(maximumKernelArgumentBytes);

        Backend = backend;
        Architecture = architecture;
        DeviceIdentity = deviceIdentity;
        RuntimeIdentity = runtimeIdentity;
        MaxWorkgroupSize = maxWorkgroupSize;
        MaxGridX = maxGridX;
        GlobalMemoryBytes = globalMemoryBytes;
        MaximumKernelArgumentBytes = maximumKernelArgumentBytes;
        CacheIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', backend, architecture, deviceIdentity, runtimeIdentity,
                maxWorkgroupSize.ToString(CultureInfo.InvariantCulture),
                maxGridX.ToString(CultureInfo.InvariantCulture),
                globalMemoryBytes.ToString(CultureInfo.InvariantCulture),
                maximumKernelArgumentBytes.ToString(CultureInfo.InvariantCulture)))));
    }

    public WarpBackendKind Backend { get; }
    public string Architecture { get; }
    public string DeviceIdentity { get; }
    public string RuntimeIdentity { get; }
    public uint MaxWorkgroupSize { get; }
    public uint MaxGridX { get; }
    public ulong GlobalMemoryBytes { get; }
    public uint MaximumKernelArgumentBytes { get; }
    public string CacheIdentity { get; }
}
