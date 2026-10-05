namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableCollectivePlan
{
    private sealed partial class Builder
    {
        private readonly uint type, operation, overflow, kind, count, maximumWords, offset, scheduler;
        private readonly uint[] members;
        private readonly string parentSchemaIdentity;
        private readonly List<Node> nodes = [new(0, 0, 0, 0, 0, -1)];
        private readonly Dictionary<(uint Start, uint Width), uint> complete = [];
        private readonly Dictionary<(uint Kind, uint Left, uint Right), uint> combinations = [];

        internal Builder(uint type, uint operation, uint overflow, uint kind, uint count, uint[] members, string parentSchemaIdentity,
            uint maximumWords, uint offset, uint scheduler)
        {
            this.type = type; this.operation = operation; this.overflow = overflow; this.kind = kind; this.count = count;
            this.members = members; this.maximumWords = maximumWords; this.offset = offset; this.scheduler = scheduler;
            this.parentSchemaIdentity = parentSchemaIdentity;
        }

        internal WarpPortableCollectivePlan Build()
        {
            uint width = 1;
            while (width < count) { width <<= 1; }
            uint outputCount = kind == WarpPortableCollectiveLayout.Reduction ? 1 : count;
            uint[] outputs = new uint[outputCount];
            for (uint i = 0; i < outputCount; i++)
            {
                uint end = kind == 1 ? count : kind == 2 ? i + 1 : i;
                uint source = Prefix(0, width, end);
                outputs[i] = Add(new(3, source, 0, i, i + 1, nodes[(int)source].Level + 1));
            }
            uint faultRoot = FaultTree(outputs, 0, outputCount);
            return Serialize(outputs, faultRoot);
        }

        private uint Prefix(uint start, uint width, uint end)
        {
            if (end <= start || start >= count) { return 0; }
            if (end >= start + width) { return Complete(start, width); }
            uint half = width >> 1;
            return Combine(2, Prefix(start, half, end), Prefix(start + half, half, end));
        }

        private uint Complete(uint start, uint width)
        {
            if (start >= count) { return 0; }
            if (complete.TryGetValue((start, width), out uint existing)) { return existing; }
            uint result;
            if (width == 1)
            {
                result = Add(new(1, 0, 0, start, start + 1, 0));
            }
            else
            {
                uint half = width >> 1;
                result = Combine(2, Complete(start, half), Complete(start + half, half));
            }
            complete.Add((start, width), result);
            return result;
        }

        private uint FaultTree(uint[] outputs, uint start, uint length)
        {
            if (length == 0) { return 0; }
            if (length == 1) { return outputs[start]; }
            uint half = length / 2;
            return Combine(4, FaultTree(outputs, start, half), FaultTree(outputs, start + half, length - half));
        }

        private uint Combine(uint nodeKind, uint left, uint right)
        {
            if (left == 0) { return right; }
            if (right == 0) { return left; }
            if (combinations.TryGetValue((nodeKind, left, right), out uint existing)) { return existing; }
            Node first = nodes[(int)left], second = nodes[(int)right];
            if (first.End != second.Start)
            {
                throw new InvalidOperationException("Every combination joins adjacent logical ranges.");
            }
            uint result = Add(new(nodeKind, left, right, first.Start, second.End, Math.Max(first.Level, second.Level) + 1));
            combinations.Add((nodeKind, left, right), result);
            return result;
        }

        private uint Add(Node node)
        {
            if (nodes.Count >= WarpPortableCollectiveLayout.MaximumNodes ||
                (long)(nodes.Count + 1) * 21 + members.Length * 9L + count * 3L + 256 > maximumWords)
            {
                throw new InvalidOperationException("The immutable node plan exceeds admitted collective scratch.");
            }
            uint id = (uint)nodes.Count;
            nodes.Add(node);
            return id;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        private readonly record struct Node(uint Kind, uint Left, uint Right, uint Start, uint End, int Level);
    }
}
