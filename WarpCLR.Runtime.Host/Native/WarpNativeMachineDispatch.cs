using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;


internal static class WarpNativeMachineDispatch
{
    public static uint[] Resume(
        WarpNativeImage image,
        uint[] states,
        IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint> scalars,
        int itemCount,
        int inputBase,
        int maximumCallDepth,
        int quantum,
        WarpMachineMemoryOperations operations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(operations);
        using var bankUse = WarpNativeBankRetention.Acquire(inputs, scalars, states);
        bool launched = false;
        try
        {
            WarpNativeMachineLaunch launch = WarpNativeMachineLaunch.Admit(image, bankUse.State,
                bankUse.Inputs, bankUse.Scalars, itemCount, inputBase, maximumCallDepth, quantum);
            cancellationToken.ThrowIfCancellationRequested();
            if (itemCount == 0) { bankUse.PreparePublication(); return []; }
            var result = (uint[])bankUse.State.Clone();
            using var statePin = new WarpPinnedUInt32(result);
            nuint stateBytes = checked((nuint)result.Length * sizeof(uint));
            ulong statePointer = bankUse.Allocate(stateBytes, operations.Allocate);
            operations.Upload(statePointer, statePin.Pointer, stateBytes);
            var inputPointers = new List<ulong>(bankUse.Inputs.Count);
            foreach (uint[] input in bankUse.Inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                nuint bytes = checked((nuint)Math.Max(input.Length, 1) * sizeof(uint));
                ulong pointer = bankUse.Allocate(bytes, operations.Allocate);
                inputPointers.Add(pointer);
                using var pin = new WarpPinnedUInt32(input);
                if (input.Length != 0) { operations.Upload(pointer, pin.Pointer, bytes); }
            }
            using var arguments = new WarpNativeKernelArguments(statePointer, inputPointers, bankUse.Scalars,
                checked((uint)itemCount), checked((uint)inputBase), checked((uint)maximumCallDepth), checked((uint)quantum));
            cancellationToken.ThrowIfCancellationRequested();
            launched = true;
            operations.Launch(arguments.Pointer, launch.GridX, launch.WorkgroupSize);
            operations.Synchronize();
            operations.Readback(statePin.Pointer, statePointer, stateBytes);
            WarpNativeMachineLaunch.ValidateReturnedStates(image.MachineLayout!, result, itemCount, maximumCallDepth);
            cancellationToken.ThrowIfCancellationRequested();
            bankUse.PreparePublication();
            return result;
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
}
