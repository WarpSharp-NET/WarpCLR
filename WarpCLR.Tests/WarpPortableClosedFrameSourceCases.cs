using System.Buffers.Binary;
using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableClosedFrameSourceCases
{
    internal sealed record Fixture(WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed,
        WarpPortableSourceHeapSchema Schema, WarpPortableWordLoweredProgram Program, WarpLogicalMachineLayout Layout);

    internal static Fixture Capture(string method)
    {
        MethodInfo source = typeof(Kernels).GetMethod(method)!;
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableClosedFrameSourcePlan plan = WarpPortableClosedFrameSourcePlan.Capture(graph, typed, schema);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed, schema, new WarpPortableClosedFrameSourceBinding(plan));
        return new(graph, typed, schema, program, new(program.Kernel));
    }

    internal static uint[] Execute(Fixture fixture, uint[] arguments, long sourceSteps = 100000,
        int sourceDepth = 64, Action<uint[]>? completed = null)
    {
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(fixture.Layout);
        uint[] state = fixture.Layout.CreateInitialState(sourceDepth, sourceSteps);
        uint[][] inputs = arguments.Length == 0 ? [[0]] : arguments.Select(word => new[] { word }).ToArray();
        for (int attempt = 0; attempt < 100000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; attempt++)
        {
            compiled.ExecuteQuantum(inputs, [], 0, state, sourceDepth, fixture.Layout.MaximumBlockCost);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        completed?.Invoke(state);
        return Enumerable.Range(0, fixture.Layout.ResultWordCount).Select(index => state[fixture.Layout.GetResultWordOffset(index, sourceDepth)]).ToArray();
    }

    internal static void ActualValueConstructorWritesEveryPackedByteAndPreservesRawFloatBits()
    {
        Fixture fixture = Capture(nameof(Kernels.MakePacked));
        foreach (uint bits in new uint[] { 0, 0x80000000, 0x7FC12345, 0xFFA54321, 0x3F812345 })
        {
            byte[] expected = new byte[16]; expected[0] = 0xA5;
            BinaryPrimitives.WriteInt16LittleEndian(expected.AsSpan(1), -129);
            BinaryPrimitives.WriteUInt64LittleEndian(expected.AsSpan(3), 0xFEDCBA9876543210);
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(11), bits); expected[15] = 0x5A;
            uint[] words = Enumerable.Range(0, 4).Select(index => BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(index * 4))).ToArray();
            CollectionAssert.AreEqual(words, Execute(fixture, [0xA5, unchecked((uint)-129), 0x76543210, 0xFEDCBA98, bits, 0x5A]));
        }
        WarpPortableWordPrivateTemporary temporary = Only(fixture.Program.Bodies.First(body => string.Equals(body.MethodIdentity, fixture.Graph.EntryIdentity, StringComparison.Ordinal)).PrivateTemporaries);
        Assert.AreEqual(16, temporary.Type.ByteSize); Assert.AreEqual(4, temporary.Type.WordCount);
        Assert.IsTrue(fixture.Graph.Methods.Any(method => method.SourceMethod.IsConstructor && method.Intrinsic is null));
    }

    internal static void ValueConstructorResultAndInstanceFieldReadsPreserveDeclaredWidths()
    {
        Fixture fixture = Capture(nameof(Kernels.ObservePacked));
        uint[] result = Execute(fixture, [0xFF, unchecked((uint)short.MinValue), uint.MaxValue, 0x80000001, 0x7FC12345, 0x81]);
        WarpPortableTypedType tuple = fixture.Typed.Types.First(type => string.Equals(type.Identity, fixture.Program.EntryProjection.ResultType.Identity, StringComparison.Ordinal));
        WarpPortableTypedField[] fields = tuple.Fields.Where(field => !field.IsStatic).ToArray();
        Assert.AreEqual(255u, WordAt(result, fields[0].ByteOffset));
        Assert.AreEqual(unchecked((uint)short.MinValue), WordAt(result, fields[1].ByteOffset));
        Assert.AreEqual(uint.MaxValue, WordAt(result, fields[2].ByteOffset));
        Assert.AreEqual(0x80000001u, WordAt(result, fields[2].ByteOffset + 4));
        Assert.AreEqual(0x7FC12345u, WordAt(result, fields[3].ByteOffset));
        Assert.AreEqual(129u, WordAt(result, fields[4].ByteOffset));
    }

    internal static void CalleeWritesThroughCallerFrameByrefsWithoutOverwritingPackedNeighbours()
    {
        Fixture fixture = Capture(nameof(Kernels.MutatePacked));
        uint[] result = Execute(fixture, [0x81234567]);
        Assert.AreEqual(0xAAu, result[0] & 255);
        Assert.AreEqual(0xEEFFu, (result[0] >> 8) & 65535);
        byte[] bytes = result.SelectMany(BitConverter.GetBytes).ToArray();
        Assert.AreEqual(0x0123456781234567ul, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(3)));
        Assert.AreEqual(0xDEADBEEFu, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(11)));
        Assert.AreEqual((byte)0x55, bytes[15]);
    }

    internal static void ClosedRecursiveFrameCallsKeepDistinctPrivateOwnerActivations()
    {
        Fixture fixture = Capture(nameof(Kernels.RecursiveOwners));
        foreach (uint count in new uint[] { 0, 1, 2, 8, 15 })
        {
            uint expected = 0; for (uint index = 1; index <= count; index++) { expected += index; }
            CollectionAssert.AreEqual(new uint[] { expected }, Execute(fixture, [count]));
        }
        Assert.IsTrue(fixture.Program.Kernel.Execution!.RecursiveCalls);
    }

    private static uint WordAt(uint[] words, int offset)
    {
        byte[] bytes = words.SelectMany(BitConverter.GetBytes).ToArray();
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    }

    private static T Only<T>(IEnumerable<T> values)
    {
        T[] captured = values.Take(2).ToArray(); Assert.HasCount(1, captured); return captured[0];
    }
}
