using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableCollectivePlan
{
    private readonly uint[] metadata;

    private WarpPortableCollectivePlan(uint offset, uint[] words, string identity)
    {
        DescriptorOffset = offset;
        metadata = words;
        Identity = identity;
        Metadata = Array.AsReadOnly(metadata);
    }

    internal uint DescriptorOffset { get; }
    internal string Identity { get; }
    internal ReadOnlyCollection<uint> Metadata { get; }
    internal uint ScratchWords => (uint)metadata.Length;
    internal uint Nodes => metadata[WarpPortableCollectiveLayout.NodeCount];
    internal uint Levels => metadata[WarpPortableCollectiveLayout.LevelCount];
    internal uint Outputs => metadata[WarpPortableCollectiveLayout.OutputCount];

    internal static WarpPortableCollectivePlan Create(uint type, uint operation, uint overflow, uint kind,
        uint count, IEnumerable<uint> logicalMembers, string parentSchemaIdentity, uint maximumScratchWords = WarpPortableCollectiveLayout.MaximumScratchWords,
        uint descriptorOffset = 64, uint schedulerOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(logicalMembers);
        ArgumentNullException.ThrowIfNull(parentSchemaIdentity);
        if (parentSchemaIdentity.Length != 64 || parentSchemaIdentity.Any(static character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("The parent heap/type/root/scheduler schema identity is a 256-bit hexadecimal digest.", nameof(parentSchemaIdentity));
        }
        ValidateArguments(type, operation, overflow, kind, count, maximumScratchWords, descriptorOffset, schedulerOffset);
        uint[] members = logicalMembers.Take((int)WarpPortableCollectiveLayout.MaximumElements + 1).ToArray();
        if (members.Length == 0 || members.Length > WarpPortableCollectiveLayout.MaximumElements ||
            members.Distinct().Count() != members.Length)
        {
            throw new ArgumentException("A collective has a bounded, nonempty, unique logical membership.", nameof(logicalMembers));
        }
        var builder = new Builder(type, operation, overflow, kind, count, members, parentSchemaIdentity, maximumScratchWords, descriptorOffset, schedulerOffset);
        return builder.Build();
    }

    internal uint[] CreateArena(ReadOnlySpan<uint> inputWords, uint dispatchGeneration = 1)
    {
        ArgumentOutOfRangeException.ThrowIfZero(dispatchGeneration);
        if (inputWords.Length != metadata[WarpPortableCollectiveLayout.InputWords])
        {
            throw new ArgumentException("Input is exactly two UInt32 words per element, low word first.", nameof(inputWords));
        }
        uint type = metadata[WarpPortableCollectiveLayout.ElementType];
        if (type == 1 || type == 2 || type == 5)
        {
            for (int i = 1; i < inputWords.Length; i += 2)
            {
                if (inputWords[i] != 0)
                {
                    throw new ArgumentException("A 32-bit input must have a zero high transport word.", nameof(inputWords));
                }
            }
        }
        uint[] arena = new uint[checked(DescriptorOffset + ScratchWords)];
        metadata.CopyTo(arena.AsSpan(checked((int)DescriptorOffset)));
        inputWords.CopyTo(arena.AsSpan(checked((int)metadata[WarpPortableCollectiveLayout.Inputs])));
        uint scheduler = metadata[WarpPortableCollectiveLayout.SchedulerAddress];
        // Standalone primitive execution binds a versioned controller-header probe.
        // Combined runtime attachment belongs to the parent schema admission.
        arena[scheduler] = 0x57525343;
        arena[scheduler + 1] = 2;
        arena[scheduler + 3] = (uint)arena.Length;
        arena[scheduler + 56] = 64;
        arena[scheduler + 60] = dispatchGeneration;
        return arena;
    }

    private static void ValidateArguments(uint type, uint operation, uint overflow, uint kind, uint count,
        uint maximumScratchWords, uint descriptorOffset, uint schedulerOffset)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(type, 1u);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(type, 6u);
        ArgumentOutOfRangeException.ThrowIfLessThan(operation, 1u);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(operation, 4u);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(overflow, 1u);
        ArgumentOutOfRangeException.ThrowIfLessThan(kind, 1u);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(kind, 3u);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, WarpPortableCollectiveLayout.MaximumElements);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumScratchWords, WarpPortableCollectiveLayout.MaximumScratchWords);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumScratchWords, WarpPortableCollectiveLayout.HeaderWords);
        if (overflow != 0 && (type >= 5 || operation == 2 || operation == 3))
        {
            throw new ArgumentException("Checked overflow is specified only for integer sum and product.", nameof(overflow));
        }
        if (descriptorOffset > int.MaxValue - WarpPortableCollectiveLayout.MaximumScratchWords ||
            schedulerOffset > descriptorOffset || descriptorOffset - schedulerOffset < 64)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptorOffset), "The admitted scheduler header precedes the disjoint collective descriptor.");
        }
    }

    private static string HashWords(uint[] words)
    {
        byte[] domain = Encoding.UTF8.GetBytes(WarpPortableCollectiveLayout.Semantics + "\n" +
            WarpPortableCollectiveLayout.BindingSemantics + "\n" + WarpPortableCollectiveLayout.SchedulerSemantics + "\n" +
            WarpPortableCollectiveLayout.HeapSemantics + "\n" +
            WarpPortableCollectiveArithmetic.Semantics + "\n" + WarpPortableBinary32.Semantics + "\n" +
            WarpPortableBinary64.Semantics + "\n" + WarpPortableNumericComparisons.Semantics + "\n" +
            WarpPortableInteger32.Semantics + "\n" + WarpPortableInteger64.Semantics + "\n");
        byte[] bytes = new byte[checked(domain.Length + words.Length * 4)];
        domain.CopyTo(bytes, 0);
        for (int i = 0; i < words.Length; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(domain.Length + i * 4, 4), words[i]);
        }
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
