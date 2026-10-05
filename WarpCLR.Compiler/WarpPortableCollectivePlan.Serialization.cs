namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableCollectivePlan
{
    private sealed partial class Builder
    {
        private WarpPortableCollectivePlan Serialize(uint[] outputs, uint faultRoot)
        {
            int levelCount = nodes.Max(static node => node.Level) + 1;
            uint[] words = Allocate(levelCount, outputs.Length);
            WriteNodesAndJobs(words, levelCount);
            outputs.CopyTo(words.AsSpan((int)(words[16] - offset)));
            members.CopyTo(words.AsSpan((int)(words[17] - offset)));
            words[WarpPortableCollectiveLayout.FaultRoot] = faultRoot;
            words[44] = 2; words[45] = 2; words[46] = 2; words[47] = 1;
            byte[] parent = Convert.FromHexString(parentSchemaIdentity);
            for (int i = 0; i < 8; i++)
            {
                words[48 + i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(parent.AsSpan(i * 4, 4));
            }
            string identity = HashWords(words);
            byte[] digest = Convert.FromHexString(identity);
            for (int i = 0; i < 8; i++)
            {
                words[32 + i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(digest.AsSpan(i * 4, 4));
            }
            return new WarpPortableCollectivePlan(offset, words, identity);
        }

        private uint[] Allocate(int levelCount, int outputCount)
        {
            uint nodeCount = (uint)nodes.Count;
            uint nodesOffset = offset + 64;
            uint statesOffset = nodesOffset + nodeCount * 8;
            uint jobsOffset = statesOffset + nodeCount * 12;
            uint levelsOffset = jobsOffset + nodeCount - 1;
            uint outputsOffset = levelsOffset + (uint)levelCount * 2;
            uint membersOffset = outputsOffset + (uint)outputCount;
            uint workersOffset = membersOffset + (uint)members.Length;
            uint inputsOffset = workersOffset + (uint)members.Length * 8;
            uint length = inputsOffset + count * 2 - offset;
            if (length > maximumWords)
            {
                throw new InvalidOperationException("The exact scratch layout exceeds resource admission.");
            }
            uint[] words = new uint[length];
            words[0] = WarpPortableCollectiveLayout.Magic; words[1] = WarpPortableCollectiveLayout.Version; words[2] = length;
            words[3] = nodeCount; words[4] = (uint)members.Length; words[5] = (uint)levelCount; words[6] = count; words[7] = (uint)outputCount;
            words[8] = type; words[9] = operation; words[10] = overflow; words[11] = kind;
            words[12] = nodesOffset; words[13] = statesOffset; words[14] = jobsOffset; words[15] = levelsOffset;
            words[16] = outputsOffset; words[17] = membersOffset; words[18] = workersOffset; words[19] = inputsOffset;
            words[20] = scheduler + 16; words[21] = scheduler; words[43] = count * 2;
            return words;
        }

        private void WriteNodesAndJobs(uint[] words, int levelCount)
        {
            uint descriptor = words[12] - offset;
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                words[descriptor] = node.Kind; words[descriptor + 1] = node.Left; words[descriptor + 2] = node.Right;
                words[descriptor + 3] = node.Start; words[descriptor + 4] = node.End; words[descriptor + 5] = unchecked((uint)node.Level);
                descriptor += 8;
            }
            uint cursor = words[14] - offset;
            uint levels = words[15] - offset;
            for (int level = 0; level < levelCount; level++)
            {
                uint first = cursor;
                for (uint id = 1; id < nodes.Count; id++)
                {
                    if (nodes[(int)id].Level == level) { words[cursor++] = id; }
                }
                words[levels++] = first + offset;
                words[levels++] = cursor - first;
            }
            if (cursor != words[15] - offset)
            {
                throw new InvalidOperationException("Every nonidentity node is admitted into exactly one phase.");
            }
        }
    }
}
