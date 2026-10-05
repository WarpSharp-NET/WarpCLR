using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest instantiates this fixture through reflection.")]
internal sealed class WarpPortableSourceFrameViewTests
{
    [TestMethod]
    public void FrameViewsBindExactArgumentAndNestedFieldBytesToTheSourceMaps()
    {
        Fixture input = Capture(nameof(Kernels.Inputs));
        WarpPortableSourceFrameBody body = input.Frames.Bodies[0];
        Assert.AreEqual(input.Program.MapsHash, input.Frames.MapsHash, StringComparer.Ordinal);
        Assert.AreEqual(input.Schema.SchemaHash, input.Frames.TypeSchemaHash, StringComparer.Ordinal);
        Assert.Contains(new WarpPortableSourceFrameView(1, 8, Id(input, typeof(long)), "argument", 0), body.Views);
        Assert.Contains(new WarpPortableSourceFrameView(12, 1, Id(input, typeof(byte)), "argument", 1), body.Views);
        Assert.Contains(new WarpPortableSourceFrameView(16, 2, Id(input, typeof(ushort)), "argument", 2), body.Views);
        Assert.Contains(new WarpPortableSourceFrameView(24, 12, Id(input, typeof(object)), "argument", 4), body.Views);
        uint[] arena = Arena(input);
        foreach (WarpPortableSourceFrameView view in body.Views)
        {
            Assert.AreEqual(0u, Heap(arena, [body.Function, body.PrivateWords, view.ByteOffset, view.ByteSpan, view.ElementType]));
        }
        uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
        byte[] stored = Enumerable.Range(0, 8).SelectMany(word => BitConverter.GetBytes(arena[descriptor + WarpPortableSourceMemoryLayout.FrameHash + (uint)word])).ToArray();
        CollectionAssert.AreEqual(Convert.FromHexString(input.Frames.FrameSchemaHash), stored);
    }

    [TestMethod]
    public void EqualWidthRetypingUncapturedStackAndPrivateBankMismatchAreRejected()
    {
        Fixture input = Capture(nameof(Kernels.Inputs)); uint[] arena = Arena(input);
        WarpPortableSourceFrameBody body = input.Frames.Bodies[0];
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, Heap(arena, [body.Function, body.PrivateWords, 20, 4, Id(input, typeof(int))]));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, Heap(arena,
            [body.Function, body.PrivateWords, (uint)input.Program.Bodies[0].EvaluationWordOffset * 4, 4, Id(input, typeof(int))]));
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, Heap(arena, [body.Function, body.PrivateWords + 1, 20, 4, Id(input, typeof(float))]));
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, Heap(arena, [body.Function, body.PrivateWords, uint.MaxValue, 4, Id(input, typeof(float))]));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, Heap(arena, [0, 0, 0, 4, Id(input, typeof(int))]));
    }

    [TestMethod]
    public void FrameSchemaAndHeapContextRequireTheSameExactVerifiedClosure()
    {
        Fixture first = Capture(nameof(Kernels.Inputs)); Fixture second = Capture(nameof(Kernels.Other));
        Assert.AreNotEqual(first.Frames.FrameSchemaHash, second.Frames.FrameSchemaHash, StringComparer.Ordinal);
        Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableSourceFrameSchema.Create(first.Schema, second.Program));
        Assert.ThrowsExactly<WarpVerificationException>(() => first.Schema.CreateArena(81, 512, 16, 32, 2, 512, second.Frames));
        Assert.AreEqual(first.Frames.FrameSchemaHash, WarpPortableSourceFrameSchema.Create(first.Schema, first.Program).FrameSchemaHash, StringComparer.Ordinal);
    }

    [TestMethod]
    public void ActualPausedSourceFrameRequiresBothActivationAndExactCapturedStorageType()
    {
        Fixture input = Capture(nameof(Kernels.Inputs)); WarpControlFlowKernel original = input.Program.Kernel;
        var kernel = new WarpControlFlowKernel(original.Name, original.InputBufferCount, original.ScalarArgumentCount,
            original.Blocks, original.Reduction, original.Functions, new WarpLogicalExecutionMetadata(original.Execution!.Bodies, frameOwners: true));
        var layout = new WarpLogicalMachineLayout(kernel); uint[] source = layout.CreateInitialState(32, 1000000);
        layout.SetSourceBoundaryMode(source, enabled: true); CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[][] values = Enumerable.Range(0, original.InputBufferCount).Select(_ => new uint[1]).ToArray();
        for (int iteration = 0; iteration < 10000 && source[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.BeforeSourceBoundary; iteration++)
        {
            compiled.ExecuteQuantum(values, [], 0, source, 32, layout.MaximumBlockCost);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.BeforeSourceBoundary, source[WarpLogicalMachineLayout.SourceBoundaryStateOffset]); Assert.AreEqual(2u, source[WarpLogicalMachineLayout.DepthOffset]);
        uint frame = source[WarpLogicalMachineLayout.DepthOffset];
        uint start = (uint)WarpLogicalMachineLayout.HeaderWords + (frame - 1) * source[WarpLogicalMachineLayout.FrameStrideOffset];
        uint context = source[WarpLogicalMachineLayout.OwnerContextOffset]; uint generation = source[start + WarpLogicalMachineLayout.FrameActivationOffset];
        uint[] pointer = [context, frame, generation, 1, 8, Id(input, typeof(long))];
        Assert.AreEqual(0u, FrameOwner(source, pointer));
        uint[] arena = Arena(input);
        uint function = source[start + WarpLogicalMachineLayout.FrameFunctionOffset]; uint words = source[start + WarpLogicalMachineLayout.FramePrivateWordsOffset];
        Assert.AreEqual(0u, Heap(arena, [function, words, pointer[3], pointer[4], pointer[5]]));
        pointer[5] = Id(input, typeof(double));
        Assert.AreEqual(0u, FrameOwner(source, pointer));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, Heap(arena, [function, words, pointer[3], pointer[4], pointer[5]]));
        pointer[2]++;
        Assert.AreEqual(WarpPortableFrameServices.InvalidOwner, FrameOwner(source, pointer));
    }

    [TestMethod]
    public void CorruptFrameViewPointersCannotReadPayloadOrOtherTables()
    {
        Fixture input = Capture(nameof(Kernels.Inputs)); uint[] arena = Arena(input);
        WarpPortableSourceFrameBody body = input.Frames.Bodies[0];
        uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
        uint row = arena[descriptor + WarpPortableSourceMemoryLayout.FrameStart];
        arena[row + WarpPortableSourceMemoryLayout.FrameViews] = uint.MaxValue;
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, Heap(arena, [body.Function, body.PrivateWords, 1, 8, Id(input, typeof(long))]));
        arena[descriptor + WarpPortableSourceMemoryLayout.FrameStart] = uint.MaxValue;
        Assert.AreEqual(WarpPortableHeapLayout.InvalidType, Heap(arena, [body.Function, body.PrivateWords, 1, 8, Id(input, typeof(long))]));
    }

    private static Fixture Capture(string name)
    {
        MethodInfo entry = typeof(Kernels).GetMethod(name)!; WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(entry);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph); WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        return new(program, schema, WarpPortableSourceFrameSchema.Create(schema, program));
    }
    private static uint Id(Fixture input, Type type) => input.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(type));
    private static uint[] Arena(Fixture input) => input.Schema.CreateArena(81, 512, 16, 32, 2, 512, input.Frames);
    private static uint Heap(uint[] arena, uint[] arguments) => Service(
        typeof(WarpPortableHeapServices).GetMethod(nameof(WarpPortableHeapServices.ValidateSourceFrameView), BindingFlags.Public | BindingFlags.Static)!, arena, arguments);
    private static uint FrameOwner(uint[] state, uint[] arguments) => Service(
        typeof(WarpPortableFrameServices).GetMethod(nameof(WarpPortableFrameServices.ValidateOwner))!, state, arguments);
    private static uint Service(MethodInfo method, uint[] arena, uint[] arguments)
    {
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(method); CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(64, 10000000);
        for (int iteration = 0; iteration < 100000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; iteration++)
        {
            compiled.ExecuteManagedQuantum(arguments.Select(word => new[] { word }).ToArray(), [], 0, state, 64, layout.MaximumBlockCost, arena);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]); return state[layout.GetResultWordOffset(0, 64)];
    }

    private sealed record Fixture(WarpPortableWordLoweredProgram Program, WarpPortableSourceHeapSchema Schema, WarpPortableSourceFrameSchema Frames);
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Packed
    {
        public byte Tag;
        public long Wide;
        public bool Flag;
        public Packed(byte tag, long wide, bool flag) { Tag = tag; Wide = wide; Flag = flag; }
    }
    private static class Kernels
    {
        public static long Inputs(Packed value, byte tag, ushort index, float number, object? reference) =>
            value.Wide + tag + index + (value.Flag ? 1 : 0) + (number == 0 ? 0 : 1) + (reference is null ? 0 : 1);
        public static long Other(long value) => value;
    }
}
