using System.Buffers.Binary;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceHeapSchema
{
    private uint[] AttachMemoryViews(uint[] arena, WarpPortableSourceFrameSchema? frames = null)
    {
        uint start = arena[WarpPortableHeapLayout.DataStart];
        uint typeStart = checked(start + WarpPortableSourceMemoryLayout.HeaderWords);
        uint viewStart = checked(typeStart + (uint)Types.Length * WarpPortableSourceMemoryLayout.TypeWords);
        uint nullableStart = checked(viewStart + (uint)MemoryViews.Length * WarpPortableSourceMemoryLayout.ViewWords);
        uint exceptionStart = checked(nullableStart + (uint)NullableLayouts.Length * WarpPortableSourceMemoryLayout.NullableWords);
        uint exceptionStateStart = checked(exceptionStart + (uint)Types.Length * WarpPortableSourceExceptionLayout.ExceptionTypeWords);
        uint frameStart = checked(exceptionStateStart + arena[WarpPortableHeapLayout.SlotCount] * WarpPortableSourceExceptionLayout.DataStateWords);
        uint frameViewStart = checked(frameStart + (uint)(frames?.Bodies.Length ?? 0) * WarpPortableSourceMemoryLayout.FrameWords);
        uint frameViews = checked((uint)(frames?.Bodies.Sum(body => body.Views.Length) ?? 0));
        uint shapeStart = checked(frameViewStart + frameViews * WarpPortableSourceMemoryLayout.FrameViewWords);
        uint shapeCount = arena[WarpPortableHeapLayout.SlotCount];
        uint end = checked(shapeStart + shapeCount * WarpPortableSourceArrayLayout.ShapeWords);
        WarpCLR.IR.WarpCompilationAdmission.Require(GraphHash, WarpCLR.IR.WarpCompilationResourceKind.VerifierWorkspaceSlots,
            (long)end - start, WarpCLR.IR.WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
        uint[] attached = new uint[checked(arena.Length + (int)(end - start))];
        arena.AsSpan(0, (int)start).CopyTo(attached); arena.AsSpan((int)start).CopyTo(attached.AsSpan((int)end));
        attached[3] = (uint)attached.Length; attached[WarpPortableHeapLayout.DataStart] = end;
        attached[WarpPortableSourceMemoryLayout.Descriptor] = start;
        WriteSourceMemoryHeader(attached, start, typeStart, viewStart, nullableStart, exceptionStart);
        attached[start + WarpPortableSourceMemoryLayout.FrameCount] = (uint)(frames?.Bodies.Length ?? 0);
        attached[start + WarpPortableSourceMemoryLayout.FrameStart] = frameStart;
        attached[start + WarpPortableSourceMemoryLayout.FrameViewCount] = frameViews;
        attached[start + WarpPortableSourceMemoryLayout.FrameViewStart] = frameViewStart;
        WriteArrayShapeHeader(attached, start, shapeStart, shapeCount);
        byte[] hash = Convert.FromHexString(SchemaHash);
        for (int word = 0; word < hash.Length / 4; word++)
        {
            attached[start + WarpPortableSourceMemoryLayout.Hash + (uint)word] = BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(word * 4));
        }
        WriteMemoryTypes(attached, typeStart); WriteMemoryViews(attached, viewStart); WriteNullableLayouts(attached, nullableStart);
        WriteExceptionTypes(attached, exceptionStart);
        if (frames is not null) { WriteFrameViews(attached, start, frameStart, frameViewStart, frames); }
        return attached;
    }

    private void WriteSourceMemoryHeader(uint[] arena, uint descriptor, uint typeStart, uint viewStart, uint nullableStart, uint exceptionStart)
    {
        arena[descriptor] = WarpPortableSourceMemoryLayout.Magic; arena[descriptor + 1] = WarpPortableSourceMemoryLayout.Version;
        arena[descriptor + WarpPortableSourceMemoryLayout.TypeCount] = (uint)Types.Length;
        arena[descriptor + WarpPortableSourceMemoryLayout.TypeStart] = typeStart;
        arena[descriptor + WarpPortableSourceMemoryLayout.ViewCount] = (uint)MemoryViews.Length;
        arena[descriptor + WarpPortableSourceMemoryLayout.ViewStart] = viewStart;
        arena[descriptor + WarpPortableSourceMemoryLayout.NullableCount] = (uint)NullableLayouts.Length;
        arena[descriptor + WarpPortableSourceMemoryLayout.NullableStart] = nullableStart;
        arena[descriptor + WarpPortableSourceMemoryLayout.ExceptionTypeStart] = exceptionStart;
    }

    private void WriteMemoryTypes(uint[] arena, uint start)
    {
        foreach (WarpPortableSourceHeapType type in Types)
        {
            uint row = checked(start + (type.Id - 1) * WarpPortableSourceMemoryLayout.TypeWords);
            arena[row + WarpPortableSourceMemoryLayout.MemoryBytes] = type.Kind == WarpPortableHeapLayout.Value ? (uint)type.PayloadBytes : 12;
            arena[row + WarpPortableSourceMemoryLayout.TypeKind] = type.Kind;
            arena[row + WarpPortableSourceMemoryLayout.StrideBytes] = checked(type.ElementStrideWords * 4);
            arena[row + WarpPortableSourceMemoryLayout.ElementType] = type.ElementType;
            arena[row + WarpPortableSourceArrayLayout.TypeRank] = type.ArrayRank;
            arena[row + WarpPortableSourceArrayLayout.TypeVector] = type.VectorArray ? 1u : 0u;
            arena[row + WarpPortableSourceArrayLayout.TypeVectorIdentity] = type.VectorType;
        }
    }

    private static void WriteArrayShapeHeader(uint[] arena, uint descriptor, uint start, uint count)
    {
        arena[descriptor + WarpPortableSourceArrayLayout.ShapeStart] = start;
        arena[descriptor + WarpPortableSourceArrayLayout.ShapeCount] = count;
        arena[descriptor + WarpPortableSourceArrayLayout.ShapeStride] = WarpPortableSourceArrayLayout.ShapeWords;
    }

    private void WriteMemoryViews(uint[] arena, uint start)
    {
        for (int index = 0; index < MemoryViews.Length; index++)
        {
            WarpPortableSourceMemoryView view = MemoryViews[index];
            uint row = checked(start + (uint)index * WarpPortableSourceMemoryLayout.ViewWords);
            arena[row] = view.OwnerType; arena[row + 1] = view.OwnerKind; arena[row + 2] = view.ByteOffset;
            arena[row + 3] = view.ByteSpan; arena[row + 4] = view.ElementType;
        }
    }

    private void WriteNullableLayouts(uint[] arena, uint start)
    {
        for (int index = 0; index < NullableLayouts.Length; index++)
        {
            WarpPortableSourceNullableLayout layout = NullableLayouts[index];
            uint row = checked(start + (uint)index * WarpPortableSourceMemoryLayout.NullableWords);
            arena[row] = layout.Type; arena[row + 1] = layout.ElementType;
            arena[row + 2] = layout.HasValueByteOffset; arena[row + 3] = layout.ValueByteOffset;
        }
    }

    private void WriteExceptionTypes(uint[] arena, uint start)
    {
        foreach (WarpPortableSourceExceptionType type in ExceptionTypes)
        {
            uint row = checked(start + (type.Type - 1) * WarpPortableSourceExceptionLayout.ExceptionTypeWords);
            arena[row + WarpPortableSourceExceptionLayout.ExceptionTypeKind] = type.Kind;
            arena[row + WarpPortableSourceExceptionLayout.ExceptionTypeHResult] = type.DefaultHResult;
        }
    }

    private static void WriteFrameViews(uint[] arena, uint descriptor, uint frameStart, uint viewStart, WarpPortableSourceFrameSchema frames)
    {
        byte[] hash = Convert.FromHexString(frames.FrameSchemaHash);
        for (int word = 0; word < hash.Length / 4; word++)
        {
            arena[descriptor + WarpPortableSourceMemoryLayout.FrameHash + (uint)word] = BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(word * 4));
        }
        for (int index = 0; index < frames.Bodies.Length; index++)
        {
            WarpPortableSourceFrameBody body = frames.Bodies[index];
            uint row = checked(frameStart + (uint)index * WarpPortableSourceMemoryLayout.FrameWords);
            arena[row] = body.Function; arena[row + 1] = body.PrivateWords;
            arena[row + 2] = viewStart; arena[row + 3] = (uint)body.Views.Length;
            foreach (WarpPortableSourceFrameView view in body.Views)
            {
                arena[viewStart] = view.ByteOffset; arena[viewStart + 1] = view.ByteSpan; arena[viewStart + 2] = view.ElementType;
                viewStart += WarpPortableSourceMemoryLayout.FrameViewWords;
            }
        }
    }
}
