using WarpCLR.IR;

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
        using var bankUse = WarpNativeBankRetention.Acquire([values], [], []);
        bool launched = false;
        try
        {
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
            uint[] admittedValues = bankUse.Inputs[0];
            if (admittedValues.Length == 0) { bankUse.PreparePublication(); return identity; }
            if (admittedValues.Length == 1)
            {
                uint result = admittedValues[0];
                bankUse.PreparePublication();
                return result;
            }
            (uint group, int maximumOutput) = Admit(image, admittedValues.Length);
            ulong input = bankUse.Allocate(checked((nuint)admittedValues.Length * sizeof(uint)), operations.Allocate);
            ulong output = bankUse.Allocate(checked((nuint)maximumOutput * sizeof(uint)), operations.Allocate);
            using var pin = new WarpPinnedUInt32(admittedValues);
            operations.Upload(input, pin.Pointer, checked((nuint)admittedValues.Length * sizeof(uint)));
            launched = true;
            input = RunPasses(input, output, admittedValues.Length, group, operation, operations, cancellationToken);
            using var readback = new WarpPinnedUInt32(new uint[1]);
            operations.Readback(readback.Pointer, input, sizeof(uint));
            cancellationToken.ThrowIfCancellationRequested();
            uint resultValue = readback.Data[0];
            bankUse.PreparePublication();
            return resultValue;
        }
        catch (Exception failure)
        {
            bankUse.RecordFailure(failure);
            if (launched && failure is not OperationCanceledException) { bankUse.RequestQuarantine(operations.Quarantine); }
            throw;
        }
        finally
        {
            bankUse.Retire(operations.Free, operations.Quarantine);
            bankUse.ThrowFirstFailure();
        }
    }

    private static ulong RunPasses(ulong input, ulong output, int count, uint group, WarpReductionOperation operation,
        WarpMachineMemoryOperations operations, CancellationToken cancellationToken)
    {
        while (count > 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint outputCount = checked((uint)(((ulong)count + 1) / 2));
            uint grid = checked((outputCount + group - 1) / group);
            using var arguments = new WarpNativeKernelArguments(output, input, checked((uint)count), operation);
            operations.Launch(arguments.Pointer, grid, group);
            operations.Synchronize();
            (input, output) = (output, input);
            count = checked((int)outputCount);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return input;
    }

    internal static void ValidateAdmission(WarpNativeImage image, int count, WarpReductionOperation operation)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (!image.SupportsScalableReduction || !Enum.IsDefined(operation))
        {
            throw new WarpHostException("WRPNATIVE1008", "The image or operation does not admit the portable reduction-pass ABI.");
        }

        if (count > 1) { _ = Admit(image, count); }
    }

    private static (uint Group, int MaximumOutput) Admit(WarpNativeImage image, int count)
    {
        // Pairwise passes bind two pointers and two UInt32 control words, padded to the portable eight-byte ABI alignment.
        if (image.Target.MaximumKernelArgumentBytes < 24)
        {
            throw new WarpHostException("WRPNATIVE1005", "The native target cannot admit the portable reduction argument block.");
        }
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
