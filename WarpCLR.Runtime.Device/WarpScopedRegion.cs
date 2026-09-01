using System.Buffers.Binary;
using System.Security.Cryptography;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Device;

public sealed class WarpScopedRegion : IDisposable
{
    private static long nextActivation;

    private readonly byte[] storage;
    private readonly Dictionary<uint, Allocation> allocations = [];
    private readonly long activation;
    private int cursor;
    private uint nextHandle = 1;
    private bool disposed;

    public WarpScopedRegion(int capacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacityBytes);
        storage = new byte[capacityBytes];
        activation = Interlocked.Increment(ref nextActivation);
    }

    public int CapacityBytes => storage.Length;

    public int UsedBytes => cursor;

    public int AllocationCount => allocations.Count;

    public bool IsDisposed => disposed;

    public WarpManagedReference AllocateObject(WarpTypeLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return Reserve(
            AllocationKind.Object,
            WarpDeviceAbi.ScopedObjectHeaderSize,
            layout.Size,
            layout.Alignment,
            layout,
            arrayLength: -1);
    }

    public WarpManagedReference AllocateUInt32Array(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        int payloadSize;
        try
        {
            payloadSize = checked(length * sizeof(uint));
        }
        catch (OverflowException)
        {
            throw Exhaustion();
        }

        return Reserve(
            AllocationKind.UInt32Array,
            WarpDeviceAbi.ScopedArrayHeaderSize,
            payloadSize,
            WarpDeviceAbi.ScopedMinimumAlignment,
            layout: null,
            length);
    }

    public uint ReadUInt32Field(WarpManagedReference reference, string fieldName)
    {
        (Allocation allocation, WarpFieldLayout field) = GetObjectField(
            reference,
            fieldName,
            WarpFieldKind.UInt32);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            storage.AsSpan(allocation.PayloadOffset + field.Offset, sizeof(uint)));
    }

    public void WriteUInt32Field(
        WarpManagedReference reference,
        string fieldName,
        uint value)
    {
        (Allocation allocation, WarpFieldLayout field) = GetObjectField(
            reference,
            fieldName,
            WarpFieldKind.UInt32);
        BinaryPrimitives.WriteUInt32LittleEndian(
            storage.AsSpan(allocation.PayloadOffset + field.Offset, sizeof(uint)),
            value);
    }

    public WarpManagedReference ReadReferenceField(
        WarpManagedReference reference,
        string fieldName)
    {
        (Allocation allocation, WarpFieldLayout field) = GetObjectField(
            reference,
            fieldName,
            WarpFieldKind.ManagedReference);
        uint handle = BinaryPrimitives.ReadUInt32LittleEndian(
            storage.AsSpan(allocation.PayloadOffset + field.Offset, sizeof(uint)));
        if (handle == 0)
        {
            return WarpManagedReference.Null;
        }

        if (!allocations.ContainsKey(handle))
        {
            throw new WarpDeviceRuntimeException(
                "WRPDEV1001",
                "A stored scoped reference has an invalid handle.");
        }

        return new WarpManagedReference(activation, handle);
    }

    public void WriteReferenceField(
        WarpManagedReference reference,
        string fieldName,
        WarpManagedReference value)
    {
        (Allocation allocation, WarpFieldLayout field) = GetObjectField(
            reference,
            fieldName,
            WarpFieldKind.ManagedReference);
        if (!value.IsNull)
        {
            _ = GetAllocation(value);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(
            storage.AsSpan(allocation.PayloadOffset + field.Offset, sizeof(uint)),
            value.Handle);
    }

    public int GetArrayLength(WarpManagedReference reference) =>
        GetArray(reference).ArrayLength;

    public uint ReadUInt32ArrayElement(WarpManagedReference reference, int index)
    {
        Allocation allocation = GetArrayElement(reference, index);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            storage.AsSpan(allocation.PayloadOffset + (index * sizeof(uint)), sizeof(uint)));
    }

    public void WriteUInt32ArrayElement(
        WarpManagedReference reference,
        int index,
        uint value)
    {
        Allocation allocation = GetArrayElement(reference, index);
        BinaryPrimitives.WriteUInt32LittleEndian(
            storage.AsSpan(allocation.PayloadOffset + (index * sizeof(uint)), sizeof(uint)),
            value);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(storage);
        allocations.Clear();
        cursor = 0;
        disposed = true;
    }

    private WarpManagedReference Reserve(
        AllocationKind kind,
        int headerSize,
        int payloadSize,
        int alignment,
        WarpTypeLayout? layout,
        int arrayLength)
    {
        EnsureActive();
        int payloadOffset;
        int allocationStart;
        int allocationEnd;
        try
        {
            payloadOffset = Align(checked(cursor + headerSize), alignment);
            allocationStart = checked(payloadOffset - headerSize);
            allocationEnd = checked(payloadOffset + payloadSize);
        }
        catch (OverflowException)
        {
            throw Exhaustion();
        }

        if (allocationEnd > storage.Length || nextHandle == 0)
        {
            throw Exhaustion();
        }

        uint handle = nextHandle;
        nextHandle = unchecked(nextHandle + 1);
        storage.AsSpan(allocationStart, allocationEnd - allocationStart).Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(
            storage.AsSpan(allocationStart, sizeof(uint)),
            handle);
        BinaryPrimitives.WriteUInt32LittleEndian(
            storage.AsSpan(allocationStart + 4, sizeof(uint)),
            (uint)kind);
        BinaryPrimitives.WriteUInt32LittleEndian(
            storage.AsSpan(allocationStart + 8, sizeof(uint)),
            checked((uint)payloadSize));
        BinaryPrimitives.WriteUInt32LittleEndian(
            storage.AsSpan(allocationStart + 12, sizeof(uint)),
            arrayLength < 0 ? 0u : checked((uint)arrayLength));

        allocations.Add(
            handle,
            new Allocation(kind, payloadOffset, payloadSize, layout, arrayLength));
        cursor = allocationEnd;
        return new WarpManagedReference(activation, handle);
    }

    private (Allocation Allocation, WarpFieldLayout Field) GetObjectField(
        WarpManagedReference reference,
        string fieldName,
        WarpFieldKind requiredKind)
    {
        Allocation allocation = GetAllocation(reference);
        if (allocation.Kind != AllocationKind.Object || allocation.Layout is null)
        {
            throw new WarpDeviceRuntimeException(
                "WRPDEV1002",
                "The scoped reference does not identify an object.");
        }

        WarpFieldLayout field = allocation.Layout.GetField(fieldName);
        if (field.Kind != requiredKind)
        {
            throw new WarpDeviceRuntimeException(
                "WRPDEV1002",
                $"Field '{fieldName}' has type {field.Kind}, not {requiredKind}.");
        }

        return (allocation, field);
    }

    private Allocation GetArray(WarpManagedReference reference)
    {
        Allocation allocation = GetAllocation(reference);
        if (allocation.Kind != AllocationKind.UInt32Array)
        {
            throw new WarpDeviceRuntimeException(
                "WRPDEV1002",
                "The scoped reference does not identify a System.UInt32 array.");
        }

        return allocation;
    }

    private Allocation GetArrayElement(WarpManagedReference reference, int index)
    {
        Allocation allocation = GetArray(reference);
        if ((uint)index >= (uint)allocation.ArrayLength)
        {
            throw new WarpDeviceRuntimeException(
                "WRPDEV1003",
                $"Array index {index} is outside length {allocation.ArrayLength}.");
        }

        return allocation;
    }

    private Allocation GetAllocation(WarpManagedReference reference)
    {
        EnsureActive();
        if (reference.IsNull)
        {
            throw new WarpDeviceRuntimeException("WRPDEV1001", "A scoped reference is null.");
        }

        if (reference.Activation != activation ||
            !allocations.TryGetValue(reference.Handle, out Allocation? allocation))
        {
            throw new WarpDeviceRuntimeException(
                "WRPDEV1004",
                "A scoped reference is outside its activation lifetime.");
        }

        return allocation;
    }

    private void EnsureActive()
    {
        if (disposed)
        {
            throw new WarpDeviceRuntimeException(
                "WRPDEV1004",
                "The scoped activation has ended.");
        }
    }

    private static int Align(int value, int alignment) => checked(
        (value + alignment - 1) & -alignment);

    private static WarpDeviceRuntimeException Exhaustion() => new(
        "WRPDEV1000",
        "The scoped activation does not have sufficient memory.");

    private enum AllocationKind : uint
    {
        Object = 1,
        UInt32Array = 2,
    }

    private sealed record Allocation(
        AllocationKind Kind,
        int PayloadOffset,
        int PayloadSize,
        WarpTypeLayout? Layout,
        int ArrayLength);
}
