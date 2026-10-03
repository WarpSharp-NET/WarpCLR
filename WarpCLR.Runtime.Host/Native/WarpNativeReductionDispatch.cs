using WarpCLR.IR;
using System.Runtime.InteropServices;

namespace WarpCLR.Runtime.Host.Native;

internal static class WarpNativeReductionDispatch
{
    public static uint Reduce(
        WarpNativeImage image,
        uint[] values,
        WarpReductionOperation operation,
        WarpMachineMemoryOperations operations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(operations);
        uint identity = operation switch
        {
            WarpReductionOperation.WrappingSum => 0,
            WarpReductionOperation.Minimum => uint.MaxValue,
            WarpReductionOperation.Maximum => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        if (!image.SupportsScalableReduction)
        {
            throw new WarpHostException("WRPNATIVE1008", "The image does not contain the portable parallel reduction-pass kernel.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (values.Length == 0) { return identity; }
        if (values.Length == 1) { return values[0]; }
        (uint group, int maximumOutput) = Admit(image, values.Length);

        var allocations = new List<ulong>();
        bool launched = false;
        try
        {
            ulong input = operations.Allocate(checked((nuint)values.Length * sizeof(uint)));
            allocations.Add(input);
            ulong output = operations.Allocate(checked((nuint)maximumOutput * sizeof(uint)));
            allocations.Add(output);
            using var pin = new WarpPinnedUInt32(values);
            operations.Upload(input, pin.Pointer, checked((nuint)values.Length * sizeof(uint)));
            int count = values.Length;
            while (count > 1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint outputCount = checked((uint)(((ulong)count + 1) / 2));
                uint grid = checked((outputCount + group - 1) / group);
                using var arguments = new WarpNativeKernelArguments(output, input, checked((uint)count), operation);
                launched = true;
                operations.Launch(arguments.Pointer, grid, group);
                operations.Synchronize();
                (input, output) = (output, input);
                count = checked((int)outputCount);
                cancellationToken.ThrowIfCancellationRequested();
            }

            using var result = new WarpPinnedUInt32(new uint[1]);
            operations.Readback(result.Pointer, input, sizeof(uint));
            cancellationToken.ThrowIfCancellationRequested();
            return result.Data[0];
        }
        catch (WarpHostException)
        {
            if (launched) { operations.Quarantine(); }
            throw;
        }
        finally
        {
            foreach (ref readonly ulong allocation in CollectionsMarshal.AsSpan(allocations)) { operations.Free(allocation); }
        }
    }

    private static (uint Group, int MaximumOutput) Admit(WarpNativeImage image, int count)
    {
        uint group = WarpDeviceAbi.IntegerMapWorkgroupSize;
        int maximumOutput = checked((int)(((ulong)count + 1) / 2));
        ulong maximumGrid = ((ulong)maximumOutput + group - 1) / group;
        ulong bytes = checked(((ulong)count + (ulong)maximumOutput) * sizeof(uint));
        if (group > image.Target.MaxWorkgroupSize || maximumGrid > image.Target.MaxGridX || bytes > image.Target.GlobalMemoryBytes)
        {
            throw new WarpHostException("WRPNATIVE1005", "The portable reduction exceeds the device memory or launch resources.");
        }

        return (group, maximumOutput);
    }
}
