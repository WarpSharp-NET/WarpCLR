using WarpCLR.IR;
using System.Runtime.InteropServices;

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
        WarpNativeMachineLaunch launch = WarpNativeMachineLaunch.Admit(image, states, inputs, scalars,
            itemCount, inputBase, maximumCallDepth, quantum);
        cancellationToken.ThrowIfCancellationRequested();
        if (itemCount == 0) { return []; }
        var allocations = new List<ulong>();
        bool launched = false;
        try
        {
            var result = (uint[])states.Clone();
            using var statePin = new WarpPinnedUInt32(result);
            nuint stateBytes = checked((nuint)result.Length * sizeof(uint));
            ulong statePointer = operations.Allocate(stateBytes);
            allocations.Add(statePointer);
            operations.Upload(statePointer, statePin.Pointer, stateBytes);
            var inputPointers = new List<ulong>(inputs.Count);
            foreach (uint[] input in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                nuint bytes = checked((nuint)Math.Max(input.Length, 1) * sizeof(uint));
                ulong pointer = operations.Allocate(bytes);
                allocations.Add(pointer);
                inputPointers.Add(pointer);
                using var pin = new WarpPinnedUInt32(input);
                if (input.Length != 0) { operations.Upload(pointer, pin.Pointer, bytes); }
            }

            using var arguments = new WarpNativeKernelArguments(statePointer, inputPointers, scalars,
                checked((uint)itemCount), checked((uint)inputBase), checked((uint)maximumCallDepth), checked((uint)quantum));
            cancellationToken.ThrowIfCancellationRequested();
            launched = true;
            operations.Launch(arguments.Pointer, launch.GridX, launch.WorkgroupSize);
            operations.Synchronize();
            operations.Readback(statePin.Pointer, statePointer, stateBytes);
            // Every launch is bounded by the logical quantum. Cancellation discards the completed quantum,
            // never publishes partial state, and is handled by the shared scheduler between launches.
            cancellationToken.ThrowIfCancellationRequested();
            return result;
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
}
