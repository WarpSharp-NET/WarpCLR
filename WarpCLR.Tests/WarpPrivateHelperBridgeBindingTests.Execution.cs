using System.Security.Cryptography;
using System.Text.Json;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

internal sealed partial class WarpPrivateHelperBridgeBindingTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OriginalCilBridgeExecutesOnlyAfterItsExactControlledPrivateAcknowledgment(bool largeQuantum)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Kernel).GetMethod(nameof(Kernel.Echo))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed, schema, new Binding(graph, typed, schema, true));
        var layout = new WarpLogicalMachineLayout(program.Kernel);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        int quantum = largeQuantum ? 4096 : layout.MaximumBlockCost;
        uint[] state = layout.CreateInitialState(WarpPrivateHelperPhaseFixture.MaximumDepth, 100);
        layout.SetSourceBoundaryMode(state, true);
        uint[] initial = (uint[])state.Clone(); uint[] arena = [0xA5A5A5A5, 0x80000000, 0x7FC12345];
        for (int attempt = 0; attempt < 100 && state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.NeedsPrivateHelper; attempt++)
        {
            if (state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.BeforeSourceBoundary)
            { WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state); }
            InvokeOriginal(compiled, state, arena, 0xDEADBEEF, quantum);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.NeedsPrivateHelper, state[WarpLogicalMachineLayout.SourceBoundaryStateOffset]);
        Assert.AreEqual(98u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        uint[] parked = (uint[])state.Clone(); uint[] arenaBefore = (uint[])arena.Clone();
        InvokeOriginal(compiled, state, arena, 0x81234567, quantum);
        CollectionAssert.AreEqual(parked, state); CollectionAssert.AreEqual(arenaBefore, arena);
        layout.AcknowledgePrivateHelperBoundary(state); uint[] acknowledged = (uint[])state.Clone();
        InvokeOriginal(compiled, state, arena, 0, quantum);
        CollectionAssert.AreEqual(acknowledged, state); CollectionAssert.AreEqual(arenaBefore, arena);
        layout.RequireAcknowledgedPrivateHelperInvocation(state, 0x81234567);
        for (int attempt = 0; attempt < 100 && state[0] == WarpLogicalMachineLayout.Runnable; attempt++)
        { InvokeOriginal(compiled, state, arena, 0x81234567, quantum); }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]);
        Assert.AreEqual(0x7FFFFFFFu, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(98u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        CollectionAssert.AreEqual(arenaBefore, arena);
        WriteOriginalWitness(graph, program, compiled, quantum, initial, parked, acknowledged, state, arena);
    }

    private static void InvokeOriginal(CoreCLRResumableKernel compiled, uint[] state, uint[] arena, uint controller, int quantum) =>
        compiled.CompiledEntryPoint.Invoke(null, [new uint[][] { [0xFEDCBA98] }, new uint[] { controller }, 0,
            state, WarpPrivateHelperPhaseFixture.MaximumDepth, quantum, CancellationToken.None, arena]);

    private void WriteOriginalWitness(WarpPortableMethodGraph graph, WarpPortableWordLoweredProgram program,
        CoreCLRResumableKernel compiled, int quantum, uint[] initial, uint[] parked, uint[] acknowledged, uint[] completed, uint[] arena)
    {
        System.Reflection.MethodInfo source = typeof(Kernel).GetMethod(nameof(Kernel.Echo))!;
        var witness = new
        {
            Scope = "Controlled generated CoreCLR entry; no child private protocol or operation authority.",
            HostProcessId = Environment.ProcessId, Quantum = quantum, SourceModule = source.Module.FullyQualifiedName,
            SourceModuleMvid = source.Module.ModuleVersionId, SourceToken = source.MetadataToken,
            SourceCilSha256 = Convert.ToHexStringLower(SHA256.HashData(source.GetMethodBody()!.GetILAsByteArray()!)),
            graph.GraphHash, IrHash = WarpIrHash.Compute(program.Kernel), program.CompilerIdentity!.IdentityHash,
            GeneratedModule = compiled.CompiledEntryPoint.Module.FullyQualifiedName,
            GeneratedModuleMvid = compiled.CompiledEntryPoint.Module.ModuleVersionId,
            GeneratedToken = compiled.CompiledEntryPoint.MetadataToken, Input = new uint[] { 0xFEDCBA98 },
            FreshControllerConsistencyWord = 0x81234567u, Initial = initial, Parked = parked,
            Acknowledged = acknowledged, Completed = completed, Arena = arena,
        };
        string? destination = Environment.GetEnvironmentVariable("WARP_SOURCE_END_WITNESSES");
        if (destination is not null)
        {
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, $"original-cil-private-bridge-q{quantum}.json"), JsonSerializer.Serialize(witness));
        }
        TestContext.WriteLine($"Original CIL generated proof: host={Environment.ProcessId}, quantum={quantum}, module={witness.GeneratedModuleMvid}, token={witness.GeneratedToken}, ir={witness.IrHash}.");
    }
}
