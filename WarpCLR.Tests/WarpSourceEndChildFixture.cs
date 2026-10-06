using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

internal sealed record WarpSourceEndChildFixture(WarpPortableMethodGraph Graph, WarpPortableSourceHeapSchema Schema,
    WarpPortableWordLoweredProgram Program, WarpPortableSourceSegmentMap Map)
{
    internal static WarpSourceEndChildFixture Capture(Type type, string name, bool initializer = false)
        => Capture(type.GetMethod(name)!, initializer);

    internal static WarpSourceEndChildFixture Capture(MethodInfo method, bool initializer)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(method);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableWordExecutionBinding binding = initializer ?
            new WarpPortableClosedInitializerSourceBinding(WarpPortableClosedInitializerSourcePlan.Capture(graph, typed, schema)) :
            new WarpPortableClosedFrameSourceBinding(WarpPortableClosedFrameSourcePlan.Capture(graph, typed, schema));
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed, schema, binding);
        return new(graph, schema, program, WarpPortableSourceSegmentMap.Capture(graph, schema, program));
    }

    internal Task<WarpCoreCLRWorkerKernel> CompileAsync()
    {
        string refs = Environment.GetEnvironmentVariable("WARP_SOURCE_EVENTS_REFS") ?? throw new InvalidOperationException("The exact900 worker deployment is required.");
        return WarpCoreCLRWorkerKernel.CompileAsync(Map.Layout, new()
        {
            DotnetHostPath = "/home/codex/.dotnet/dotnet",
            WorkerAssemblyPath = Path.Combine(refs, "WarpCLR.CoreCLR.Worker", "release", "WarpCLR.CoreCLR.Worker.dll"),
        }, CancellationToken.None);
    }

    internal WarpSourceSegmentSnapshot Snapshot(WarpCoreCLRWorkerLease lease, ulong ordinal, uint[] state, uint[] arena) =>
        new(checked(ordinal + 1), checked(ordinal + 1), lease.ProcessId, lease.CompiledModule, Map.IrHash, state, arena);

    internal async Task<ulong> RunToFirstGuestAsync(WarpCoreCLRWorkerLease lease, uint[][] inputs, uint[] state, uint[] arena, int quantum)
    {
        WarpSourceSegmentBankContract.Validate(Map.Layout, state, WarpSourceEventFixture.MaximumDepth);
        ulong budget = WarpSourceSegmentCheckpointContract.Remaining(state); ulong ordinal = 0;
        while (state[0] == WarpLogicalMachineLayout.Runnable &&
            state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.BeforeSourceBoundary && ordinal < 5000)
        {
            await lease.ExecuteManagedQuantumAsync(inputs, [], 0, state, WarpSourceEventFixture.MaximumDepth, quantum, arena, CancellationToken.None).ConfigureAwait(false);
            ordinal++;
            Assert.AreEqual(budget, WarpSourceSegmentCheckpointContract.Remaining(state));
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[0]);
        Assert.AreEqual(WarpLogicalMachineLayout.BeforeSourceBoundary, state[WarpLogicalMachineLayout.SourceBoundaryStateOffset]);
        Assert.IsLessThan(5000ul, ordinal); return ordinal;
    }

    internal void WriteWitness(string label, WarpCoreCLRWorkerLease lease, int quantum,
        WarpSourceSegmentSnapshot start, WarpSourceSegmentSnapshot first, WarpSourceSegmentSnapshot end, object? candidate = null)
    {
        string directory = Environment.GetEnvironmentVariable("WARP_SOURCE_END_WITNESSES") ?? throw new InvalidOperationException("The owned witness directory is required.");
        Directory.CreateDirectory(directory);
        object[] sources = Graph.Methods.Select(method => (object)new
        {
            method.Identity, Module = method.SourceMethod.Module.FullyQualifiedName, method.SourceMethod.MetadataToken,
            Mvid = method.SourceMethod.Module.ModuleVersionId,
            CilSha256 = Convert.ToHexString(SHA256.HashData(method.SourceMethod.GetMethodBody()!.GetILAsByteArray()!)),
        }).ToArray();
        File.WriteAllBytes(Path.Combine(directory, label + "-" + quantum + ".json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            Scope = "Actual ordinary CoreCLR child and complete banks; local counters are consistency labels, no authenticated RPC/private invocation/nonce/permit",
            lease.ProcessId, lease.CompiledModule, quantum, Map.IrHash, Map.MapHash, Map.CompilerIdentity,
            Invocation = Map.Invocation, Sources = sources, Candidate = candidate, Start = Bank(start), First = Bank(first), End = Bank(end),
        }));
    }

    private static object Bank(WarpSourceSegmentSnapshot snapshot) => new
    {
        snapshot.Ordinal, snapshot.Sequence, snapshot.ProcessId, snapshot.Module, snapshot.IrHash,
        snapshot.StateHash, snapshot.ArenaHash, State = snapshot.State.ToArray(), Arena = snapshot.Arena.ToArray(),
    };
}
