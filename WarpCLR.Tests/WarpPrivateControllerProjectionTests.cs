using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest constructs this internal fixture through reflected discovery under DiscoverInternals.")]
internal sealed class WarpPrivateControllerProjectionTests
{
    private const string Service = "warp.proof.private-controller/pure-u32-comparison/0.1";

    [TestMethod]
    [DataRow(0u)]
    [DataRow(1u)]
    [DataRow(0x7FFFFFFFu)]
    [DataRow(0x80000000u)]
    [DataRow(uint.MaxValue)]
    public void ActualPreparedPrivateControllerProjectionPreservesEveryU32Bit(uint controller)
    {
        WarpControlFlowKernel program = Fixture(controller);
        var layout = new WarpLogicalMachineLayout(program);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(4, 100);
        uint[] before = (uint[])state.Clone();
        Assert.ThrowsExactly<InvalidOperationException>(() => compiled.ExecuteQuantum([[0]], [controller], 0,
            state, 4, layout.MaximumBlockCost));
        CollectionAssert.AreEqual(before, state);
        // Direct invocation is a consistency fixture, not registry authority.
        // Ordinary runtime invocation above is denied before any state change.
        for (int attempt = 0; attempt < 100 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; attempt++)
        {
            compiled.CompiledEntryPoint.Invoke(null, [new uint[][] { [0] }, new uint[] { controller }, 0, state,
                4, layout.MaximumBlockCost, CancellationToken.None, Array.Empty<uint>()]);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(1u, state[layout.GetResultWordOffset(0, 4)]);
    }

    [TestMethod]
    public void PrivateControllerCodecRoundTripsRawContractsAndRejectsAuthenticatedForgeries()
    {
        WarpControlFlowKernel original = Fixture(0x81234567, Service + (char)0xD800);
        string hash = WarpIrHash.Compute(original);
        byte[] bytes = WarpCoreCLRBinaryPlanCodec.Serialize(original);
        WarpControlFlowKernel restored = WarpCoreCLRBinaryPlanCodec.Deserialize(bytes, hash);
        Assert.AreEqual(hash, WarpIrHash.Compute(restored), StringComparer.Ordinal);
        Assert.AreEqual(original.Execution!.PrivateControllerProjection!.Uses[0], restored.Execution!.PrivateControllerProjection!.Uses[0]);
        byte[] marker = RawStringBytes(WarpPrivateControllerOpCode.Version);
        int first = bytes.AsSpan().IndexOf(marker);
        Assert.IsGreaterThanOrEqualTo(0, first);
        int count = checked(first + marker.Length);
        foreach ((int offset, int value) in Enumerable.Range(0, 6).Select(part =>
            (count + 4 + part * 4, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(count + 4 + part * 4)) + 1))
            .Append((count, int.MaxValue)).Append((count, 0)))
        {
            byte[] changed = (byte[])bytes.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(changed.AsSpan(offset), value);
            SHA256.HashData(changed.AsSpan(0, changed.Length - 32), changed.AsSpan(changed.Length - 32));
            Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRBinaryPlanCodec.Deserialize(changed, hash));
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    public void PrivateControllerCannotEscapeItsExactPureHelperUse(int change) =>
        Assert.ThrowsExactly<ArgumentException>(() => Fixture(7, mutation: change));

    [TestMethod]
    public void PrivateControllerOpcodeCannotEnterWithoutItsImmutableCapability()
    {
        WarpControlFlowKernel original = Fixture(7);
        Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel("unadmitted-private-controller", 1, 1,
            original.Blocks, functions: original.Functions));
    }

    [TestMethod]
    public void PrivateControllerIdentityDoesNotFollowMutableUseCollections()
    {
        var uses = new List<WarpPrivateControllerUse> { new(0, 0, 0, 0, 0, 1, Service) };
        var projection = new WarpPrivateControllerProjection(uses);
        WarpControlFlowKernel original = Fixture(7, projection: projection);
        string before = WarpIrHash.Compute(original);
        uses[0] = uses[0] with { Value = 999, ServiceIdentity = "changed" };
        Assert.AreEqual(before, WarpIrHash.Compute(original), StringComparer.Ordinal);
        Assert.AreEqual(0, projection.Uses[0].Value);
    }

    [TestMethod]
    public void CapturedSourcePrivateChannelIsSealedFromActualGeneratedServiceUses()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Kernels).GetMethod(nameof(Kernels.Zero))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableWordLoweredProgram lowered = WarpPortableWordLowerer.Lower(graph, typed, schema, new SourceProbeBinding(graph, typed, schema));
        WarpPortableWordProgramIdentity.Validate(graph, schema, lowered);
        Assert.AreEqual(1, lowered.Kernel.ScalarArgumentCount);
        Assert.HasCount(1, lowered.Kernel.Execution!.PrivateControllerProjection!.Uses);
        WarpPrivateControllerUse use = lowered.Kernel.Execution!.PrivateControllerProjection!.Uses[0];
        Assert.IsGreaterThan(0, use.Function);
        Assert.AreEqual(SourceProbeBinding.ServiceIdentity, use.ServiceIdentity, StringComparer.Ordinal);
        Assert.AreEqual(lowered.Kernel.Functions[use.Callee].Name, use.ServiceIdentity, StringComparer.Ordinal);
        var layout = new WarpLogicalMachineLayout(lowered.Kernel);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(4, 100);
        uint[] before = (uint[])state.Clone();
        Assert.ThrowsExactly<InvalidOperationException>(() => compiled.ExecuteQuantum([[0]], [7], 0, state, 4, layout.MaximumBlockCost));
        CollectionAssert.AreEqual(before, state);
    }

    private static WarpControlFlowKernel Fixture(uint expected, string service = Service, int mutation = -1,
        WarpPrivateControllerProjection? projection = null)
    {
        var instructions = new List<WarpIrInstruction> { new(0, mutation == 6 ? WarpIrOpCode.LoadScalar : WarpPrivateControllerOpCode.LoadController),
            new(1, 0, [0], 1) };
        WarpBlockTerminator terminator = new WarpReturnTerminator(mutation == 1 ? 0 : 1);
        if (mutation == 0) { instructions.Add(new(2, WarpIrOpCode.Constant)); instructions.Add(new(3, WarpIrOpCode.Add, 0, 2)); }
        if (mutation == 2) { instructions.Add(new(2, 0, [0], 1)); }
        WarpBasicBlock[] blocks = [new(0, [], instructions, terminator)];
        if (mutation == 3)
        {
            blocks = [new(0, [], instructions, new WarpBranchTerminator(new(1, [0]))),
                new(1, [new(2)], [], new WarpReturnTerminator(2))];
        }
        WarpBlockTerminator returned = mutation == 5 ? new WarpStateDispatchTerminator([new(0, 0)]) : new WarpReturnTerminator(2);
        WarpControlFlowFunction[] functions = [new(0, service, 1, [new(0, [],
            [new(0, WarpIrOpCode.LoadArgument), new(1, WarpIrOpCode.Constant, immediate: expected), new(2, WarpIrOpCode.Equal, 0, 1)], returned)])];
        WarpLogicalBodyMetadata[] bodies = [new(0, false, blocks.Select(_ => 1).ToArray(), countsSourceDepth: true),
            new(0, mutation != 4, [0], countsSourceDepth: mutation == 4)];
        projection ??= new([new(0, 0, 0, 0, 0, 1, service)]);
        var metadata = new WarpLogicalExecutionMetadata(bodies, frameOwners: true, runtimeStateAccess: true,
            nonlocalStateDispatch: mutation == 5, privateControllerProjection: projection);
        return new("private-controller-consistency-fixture", 1, 1, blocks, null, functions, metadata);
    }

    private static byte[] RawStringBytes(string value)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(value.Length);
        foreach (char unit in value) { writer.Write((ushort)unit); }
        writer.Flush();
        return stream.ToArray();
    }

    private sealed class SourceProbeBinding : WarpPortableWordExecutionBinding
    {
        internal const string ServiceIdentity = "warp.proof.private-controller-source/constant-zero-pure-helper/0.1";
        private int service;

        internal SourceProbeBinding(WarpPortableMethodGraph graph, WarpPortableTypedProgram typed, WarpPortableSourceHeapSchema schema)
            : base(graph.GraphHash, typed.VerifiedHash, schema.SchemaHash, ServiceIdentity, graph.GraphHash,
                new(FrameOwners: true, RuntimeStateAccess: true, PrivateController: true)) { }

        internal override void Prepare(WarpPortableWordBindingPreparation context)
        {
            service = context.ReserveFunction(ServiceIdentity);
            context.InstallFunction(new(service, ServiceIdentity, 1, [new(0, [],
                [new(0, WarpIrOpCode.LoadArgument), new(1, WarpIrOpCode.Constant)], new WarpReturnTerminator(1))]),
                new(0, true, [0], countsSourceDepth: false));
        }

        internal override WarpBlockTerminator? LowerInstruction(WarpPortableWordInstructionContext context)
        {
            if (context.SourceInstruction.OpCode != OpCodes.Ret) { return null; }
            int controller = context.Emit(WarpPrivateControllerOpCode.LoadController);
            return new WarpReturnTerminator(context.Call(service, [controller])[0]);
        }

        internal override string Complete(WarpPortableWordBindingCompletion context) => WarpIrHash.Compute(context.StructuralKernel);
    }

    private static class Kernels
    {
        public static uint Zero(uint ignored) => 0;
    }
}
