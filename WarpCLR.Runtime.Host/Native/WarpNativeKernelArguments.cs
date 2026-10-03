using System.Runtime.InteropServices;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpNativeKernelArguments : IDisposable
{
    private readonly WarpNativeBlock values;
    private readonly WarpNativeBlock addresses;

    public WarpNativeKernelArguments(IReadOnlyList<ulong> inputPointers, ulong outputPointer,
        uint itemCount, IReadOnlyList<uint> scalars)
    {
        ArgumentNullException.ThrowIfNull(inputPointers);
        ArgumentNullException.ThrowIfNull(scalars);
        int count = checked(inputPointers.Count + scalars.Count + 2);
        values = new WarpNativeBlock(checked(count * sizeof(ulong)));
        addresses = new WarpNativeBlock(checked(count * IntPtr.Size));
        for (int index = 0; index < inputPointers.Count; index++)
        {
            Set64(index, inputPointers[index]);
        }

        Set64(inputPointers.Count, outputPointer);
        Set32(inputPointers.Count + 1, itemCount);
        for (int index = 0; index < scalars.Count; index++)
        {
            Set32(inputPointers.Count + 2 + index, scalars[index]);
        }
    }

    public WarpNativeKernelArguments(ulong statePointer, IReadOnlyList<ulong> inputPointers,
        IReadOnlyList<uint> scalars, uint itemCount, uint inputBase, uint maximumCallDepth, uint quantum)
    {
        ArgumentNullException.ThrowIfNull(inputPointers);
        ArgumentNullException.ThrowIfNull(scalars);
        int count = checked(inputPointers.Count + scalars.Count + 5);
        values = new WarpNativeBlock(checked(count * sizeof(ulong)));
        addresses = new WarpNativeBlock(checked(count * IntPtr.Size));
        int index = 0;
        Set64(index++, statePointer);
        foreach (ulong pointer in inputPointers) { Set64(index++, pointer); }
        foreach (uint scalar in scalars) { Set32(index++, scalar); }
        Set32(index++, itemCount);
        Set32(index++, inputBase);
        Set32(index++, maximumCallDepth);
        Set32(index, quantum);
    }

    public WarpNativeKernelArguments(ulong outputPointer, ulong inputPointer, uint inputCount,
        WarpCLR.IR.WarpReductionOperation operation)
    {
        values = new WarpNativeBlock(4 * sizeof(ulong));
        addresses = new WarpNativeBlock(4 * IntPtr.Size);
        Set64(0, outputPointer);
        Set64(1, inputPointer);
        Set32(2, inputCount);
        Set32(3, checked((uint)operation));
    }

    public IntPtr Pointer => addresses.Pointer;

    private void Set64(int index, ulong value)
    {
        Marshal.WriteInt64(values.Pointer, index * sizeof(ulong), unchecked((long)value));
        Marshal.WriteIntPtr(addresses.Pointer, index * IntPtr.Size, IntPtr.Add(values.Pointer, index * sizeof(ulong)));
    }

    private void Set32(int index, uint value)
    {
        Marshal.WriteInt32(values.Pointer, index * sizeof(ulong), unchecked((int)value));
        Marshal.WriteIntPtr(addresses.Pointer, index * IntPtr.Size, IntPtr.Add(values.Pointer, index * sizeof(ulong)));
    }

    public void Dispose()
    {
        addresses.Dispose();
        values.Dispose();
    }
}
