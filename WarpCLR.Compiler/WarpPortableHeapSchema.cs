using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace WarpCLR.Compiler;

internal sealed class WarpPortableHeapSchema
{
    private readonly WarpPortableHeapTypeLayout[] types;
    private readonly uint[] metadata;

    public WarpPortableHeapSchema(IEnumerable<WarpPortableHeapTypeLayout> layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        types = layouts.OrderBy(item => item.Id).ToArray();
        if (types.Length == 0)
        {
            throw new ArgumentException("The type closure must not be empty.", nameof(layouts));
        }

        Validate(layouts);
        metadata = CreateMetadata();
    }

    public uint[] CreateArena(uint context, uint payloadWords, uint maximumObjects, uint maximumRoots, uint maximumWorkers, uint quotaWords)
    {
        ArgumentOutOfRangeException.ThrowIfZero(context);
        ArgumentOutOfRangeException.ThrowIfZero(maximumObjects);
        ArgumentOutOfRangeException.ThrowIfZero(maximumRoots);
        ArgumentOutOfRangeException.ThrowIfZero(maximumWorkers);
        ArgumentOutOfRangeException.ThrowIfLessThan(payloadWords, WarpPortableHeapLayout.BlockWords + 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quotaWords, payloadWords);
        uint slotStart = checked((uint)metadata.Length);
        uint rootStart = checked(slotStart + maximumObjects * WarpPortableHeapLayout.SlotWords);
        uint workerStart = checked(rootStart + maximumRoots * WarpPortableHeapLayout.RootWords);
        uint workStart = checked(workerStart + maximumWorkers * WarpPortableHeapLayout.WorkerWords);
        uint transferStart = checked(workStart + maximumObjects);
        uint scratchStart = checked(transferStart + maximumObjects * WarpPortableHeapLayout.TransferWords);
        uint scratchWords = checked(payloadWords * 2);
        foreach (WarpPortableHeapTypeLayout type in types)
        {
            if (type.Kind == WarpPortableHeapLayout.Value)
            {
                scratchWords = Math.Max(scratchWords, type.PayloadWords);
            }
        }
        uint dataStart = checked(scratchStart + scratchWords);
        uint[] arena = new uint[checked((int)(dataStart + payloadWords))];
        metadata.CopyTo(arena, 0);
        arena[WarpPortableHeapLayout.Context] = context;
        arena[3] = (uint)arena.Length;
        SetDimensions(arena, maximumObjects, slotStart, maximumRoots, rootStart, maximumWorkers, workerStart);
        arena[WarpPortableHeapLayout.WorkStart] = workStart;
        arena[WarpPortableHeapLayout.TransferStart] = transferStart;
        arena[WarpPortableHeapLayout.TransferCapacity] = maximumObjects;
        arena[WarpPortableHeapLayout.ScratchStart] = scratchStart;
        arena[WarpPortableHeapLayout.ScratchWords] = scratchWords;
        arena[WarpPortableHeapLayout.DataStart] = dataStart;
        arena[WarpPortableHeapLayout.AllocationQuota] = quotaWords;
        arena[dataStart + WarpPortableHeapLayout.BlockSize] = payloadWords;
        for (uint slot = 0; slot < maximumObjects; slot++)
        {
            arena[slotStart + slot * WarpPortableHeapLayout.SlotWords + WarpPortableHeapLayout.SlotGeneration] = 1;
        }
        for (uint root = 0; root < maximumRoots; root++)
        {
            arena[rootStart + root * WarpPortableHeapLayout.RootWords + WarpPortableHeapLayout.RootGeneration] = 1;
        }
        return arena;
    }

    private static void SetDimensions(uint[] arena, uint objects, uint slots, uint roots, uint rootStart, uint workers, uint workerStart)
    {
        arena[WarpPortableHeapLayout.SlotCount] = objects;
        arena[WarpPortableHeapLayout.SlotStart] = slots;
        arena[WarpPortableHeapLayout.RootCount] = roots;
        arena[WarpPortableHeapLayout.RootStart] = rootStart;
        arena[WarpPortableHeapLayout.WorkerCount] = workers;
        arena[WarpPortableHeapLayout.WorkerStart] = workerStart;
    }

    private void Validate(IEnumerable<WarpPortableHeapTypeLayout> layouts)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < types.Length; index++)
        {
            WarpPortableHeapTypeLayout type = types[index];
            if (!identities.Add(type.Identity))
            {
                throw new ArgumentException("Every closed type must have a unique canonical identity.", nameof(layouts));
            }
            if (type.Id != (uint)index + 1 || type.Kind < WarpPortableHeapLayout.Class || type.Kind > WarpPortableHeapLayout.Interface)
            {
                throw new ArgumentException("Type IDs must be dense and kinds must be defined.", nameof(layouts));
            }
            foreach (uint destination in type.AssignableTo)
            {
                RequireType(destination);
            }
            ValidateReferences(type.References, type.PayloadWords);
            ValidateReferences(type.StaticReferences, type.StaticWords);
            ValidateArray(type);
        }
    }

    private void ValidateArray(WarpPortableHeapTypeLayout type)
    {
        if (type.Kind == WarpPortableHeapLayout.ReferenceArray || type.Kind == WarpPortableHeapLayout.ValueArray)
        {
            RequireType(type.ElementType);
            WarpPortableHeapTypeLayout element = types[type.ElementType - 1];
            bool valueElement = element.Kind == WarpPortableHeapLayout.Value;
            if (valueElement != (type.Kind == WarpPortableHeapLayout.ValueArray) ||
                type.PayloadWords != 0 || type.References.Count != 0 || (valueElement && element.PayloadWords == 0))
            {
                throw new ArgumentException("Array descriptors must name their exact reference or value element type.", nameof(type));
            }
        }
        else if (type.ElementType != 0)
        {
            throw new ArgumentException("Only array descriptors may declare an element type.", nameof(type));
        }
        if ((type.Kind == WarpPortableHeapLayout.String || type.Kind == WarpPortableHeapLayout.Interface) &&
            (type.PayloadWords != 0 || type.References.Count != 0))
        {
            throw new ArgumentException("Strings and interfaces do not declare instance word fields.", nameof(type));
        }
    }

    private void RequireType(uint id)
    {
        if (id == 0 || id > types.Length)
        {
            throw new ArgumentException("A layout names a type outside the immutable closure.", nameof(id));
        }
    }

    private void ValidateReferences(IEnumerable<WarpPortableHeapReferenceLayout> references, uint wordCount)
    {
        uint previousEnd = 0;
        foreach (WarpPortableHeapReferenceLayout reference in references)
        {
            RequireType(reference.TypeId);
            if (reference.Offset < previousEnd || reference.Offset > wordCount || wordCount - reference.Offset < 3 ||
                types[reference.TypeId - 1].Kind == WarpPortableHeapLayout.Value)
            {
                throw new ArgumentException("Reference maps must contain disjoint three-word reference fields with reference types.", nameof(references));
            }
            previousEnd = checked(reference.Offset + 3);
        }
    }

    private uint[] CreateMetadata()
    {
        uint count = (uint)types.Length;
        uint closureStart = checked(WarpPortableHeapLayout.HeaderWords + count * WarpPortableHeapLayout.TypeWords);
        uint next = checked(closureStart + count * count);
        foreach (WarpPortableHeapTypeLayout type in types)
        {
            next = checked(next + (uint)(type.References.Count + type.StaticReferences.Count) * 2 + type.StaticWords);
        }
        uint[] words = new uint[checked((int)next)];
        words[0] = WarpPortableHeapLayout.Magic;
        words[1] = WarpPortableHeapLayout.Version;
        words[WarpPortableHeapLayout.TypeCount] = count;
        words[WarpPortableHeapLayout.TypeStart] = WarpPortableHeapLayout.HeaderWords;
        words[WarpPortableHeapLayout.ClosureStart] = closureStart;
        next = checked(closureStart + count * count);
        foreach (WarpPortableHeapTypeLayout type in types)
        {
            WriteType(words, type, ref next);
            words[closureStart + (type.Id - 1) * count + type.Id - 1] = 1;
            foreach (uint assignable in type.AssignableTo)
            {
                words[closureStart + (type.Id - 1) * count + assignable - 1] = 1;
            }
        }
        CompleteClosure(words);
        WriteDigest(words);
        return words;
    }

    private void WriteDigest(uint[] words)
    {
        byte[] bytes = new byte[checked(words.Length * sizeof(uint))];
        for (int index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint)), words[index]);
        }
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(bytes);
        hash.AppendData(Encoding.UTF8.GetBytes(WarpPortableHeapLayout.Semantics));
        byte[] length = new byte[sizeof(uint)];
        foreach (WarpPortableHeapTypeLayout type in types)
        {
            byte[] identity = Encoding.UTF8.GetBytes(type.Identity);
            BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)identity.Length);
            hash.AppendData(length);
            hash.AppendData(identity);
        }
        byte[] digest = hash.GetHashAndReset();
        for (int index = 0; index < digest.Length / sizeof(uint); index++)
        {
            words[WarpPortableHeapLayout.SchemaHash + index] = BinaryPrimitives.ReadUInt32LittleEndian(digest.AsSpan(index * sizeof(uint)));
        }
    }

    private void WriteType(uint[] words, WarpPortableHeapTypeLayout type, ref uint next)
    {
        uint start = WarpPortableHeapLayout.HeaderWords + (type.Id - 1) * WarpPortableHeapLayout.TypeWords;
        words[start + WarpPortableHeapLayout.TypeKind] = type.Kind;
        words[start + WarpPortableHeapLayout.TypePayloadWords] = type.PayloadWords;
        words[start + WarpPortableHeapLayout.ElementType] = type.ElementType;
        words[start + WarpPortableHeapLayout.ElementWords] = type.Kind == WarpPortableHeapLayout.ReferenceArray ? 3 :
            type.Kind == WarpPortableHeapLayout.ValueArray ? types[type.ElementType - 1].PayloadWords : 0;
        words[start + WarpPortableHeapLayout.ReferenceCount] = (uint)type.References.Count;
        words[start + WarpPortableHeapLayout.ReferenceMap] = next;
        WriteReferences(words, type.References, ref next);
        words[start + WarpPortableHeapLayout.StaticReferenceCount] = (uint)type.StaticReferences.Count;
        words[start + WarpPortableHeapLayout.StaticReferenceMap] = next;
        WriteReferences(words, type.StaticReferences, ref next);
        words[start + WarpPortableHeapLayout.StaticWords] = type.StaticWords;
        words[start + WarpPortableHeapLayout.StaticStart] = next;
        next += type.StaticWords;
    }

    private static void WriteReferences(uint[] words, IEnumerable<WarpPortableHeapReferenceLayout> references, ref uint next)
    {
        foreach (WarpPortableHeapReferenceLayout reference in references)
        {
            words[next++] = reference.Offset;
            words[next++] = reference.TypeId;
        }
    }

    private void CompleteClosure(uint[] words)
    {
        uint start = words[WarpPortableHeapLayout.ClosureStart];
        uint count = (uint)types.Length;
        for (uint middle = 0; middle < count; middle++)
        {
            for (uint source = 0; source < count; source++)
            {
                for (uint destination = 0; destination < count; destination++)
                {
                    if (words[start + source * count + middle] != 0 && words[start + middle * count + destination] != 0)
                    {
                        words[start + source * count + destination] = 1;
                    }
                }
            }
        }
    }
}
