using System.Reflection;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

// A prepared-nonnull consistency fixture. No source permit, factory ticket,
// hidden private invocation or production controller grant is issued here.
internal sealed partial class WarpSourceGuardedChildDriver : IAsyncDisposable
{
    private const int RootCapacity = 256;
    private readonly Dictionary<string, Service> services = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Function, int Pc), uint> maps = [];
    private readonly WarpCoreCLRWorkerKernel kernel;
    private readonly uint flag;

    private WarpSourceGuardedChildDriver(WarpSourceEndChildFixture fixture, WarpPortableExceptionPlan plan,
        WarpCoreCLRWorkerKernel kernel, WarpCoreCLRWorkerLease lease, uint flag)
    {
        Fixture = fixture; Plan = plan; this.kernel = kernel; Lease = lease; this.flag = flag;
        WarpPortableSourceFrameSchema frames = WarpPortableSourceFrameSchema.Create(fixture.Schema, fixture.Program);
        uint[] heap = fixture.Schema.CreateArena(0x906, 32768, 256, 2048, 1, 32768, frames);
        var roots = new List<WarpPortableSchedulerRootLayout> { new(0, 0, []) };
        foreach (WarpPortableWordBody body in fixture.Program.Bodies)
        foreach (WarpPortableWordSourceBlock block in body.SourceBlocks)
        {
            int pc = fixture.Map.Layout.GetBlockEntry(body.Function, block.Block); maps.Add((body.Function, pc), (uint)roots.Count);
            roots.Add(new((uint)body.Function, (uint)pc, Enumerable.Range(0, RootCapacity).Select(index => (uint)(index * 3)).ToArray()));
        }
        var scheduler = new WarpPortableSchedulerSchema(1, 1, 4096, 64, 100000000, 0, RootCapacity * 3, roots, [], []);
        Arena = plan.Attach(scheduler.AttachToEmptyHeap(heap), 16, 16, 16);
        Scheduler = Arena[WarpPortableSchedulerLayout.HeapDescriptor];
        State = fixture.Map.Layout.CreateInitialState(WarpSourceEventFixture.MaximumDepth, 100000000);
        fixture.Map.Layout.SetSourceBoundaryMode(State, true);
    }

    internal WarpSourceEndChildFixture Fixture { get; }
    internal WarpPortableExceptionPlan Plan { get; }
    internal WarpCoreCLRWorkerLease Lease { get; }
    internal uint[] Arena { get; }
    internal uint[] State { get; }
    internal uint[] Owner { get; private set; } = [];
    internal uint Scheduler { get; }
    internal uint Flag => flag;
    internal ulong Ordinal { get; private set; }
    internal uint RunGeneration => Arena[Scheduler + Arena[Scheduler + WarpPortableSchedulerLayout.WorkerStart] + WarpPortableSchedulerLayout.RunGeneration];
    internal uint DispatchGeneration => Arena[Scheduler + WarpPortableSchedulerLayout.DispatchGeneration];

    internal static async Task<WarpSourceGuardedChildDriver> CreateAsync(string method, uint flag = 1)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(WarpSourceEndKernels).GetMethod(method)!,
            concreteTypes: [typeof(InvalidOperationException), typeof(StackOverflowException)]);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph); WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        var exceptions = new WarpPortableExceptionSourceBinding(graph, typed, schema, 1);
        WarpPortableClosedFrameSourcePlan frames = WarpPortableClosedFrameSourcePlan.CaptureForExceptionComposition(graph, typed, schema, exceptions);
        var binding = new WarpPortableClosedFrameExceptionBinding(frames, exceptions);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed, schema, binding);
        var fixture = new WarpSourceEndChildFixture(graph, schema, program, WarpPortableSourceSegmentMap.Capture(graph, schema, program));
        WarpCoreCLRWorkerKernel compiled = await fixture.CompileAsync().ConfigureAwait(false); WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        var driver = new WarpSourceGuardedChildDriver(fixture, binding.ExceptionPlan, compiled, lease, flag);
        try { await driver.InitializeAsync().ConfigureAwait(false); return driver; }
        catch { await driver.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        await Lease.DisposeAsync().ConfigureAwait(false); await kernel.DisposeAsync().ConfigureAwait(false);
        foreach (Service service in services.Values)
        { await service.Lease.DisposeAsync().ConfigureAwait(false); await service.Kernel.DisposeAsync().ConfigureAwait(false); }
    }

    private sealed record Service(WarpLogicalMachineLayout Layout, WarpCoreCLRWorkerKernel Kernel, WarpCoreCLRWorkerLease Lease);

    private async Task<uint> ServiceAsync(Type type, string name, params uint[] arguments)
    {
        string identity = type.FullName + "." + name;
        if (!services.TryGetValue(identity, out Service? service))
        {
            WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(type.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!);
            string refs = Environment.GetEnvironmentVariable("WARP_SOURCE_EVENTS_REFS")!;
            WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new()
            {
                DotnetHostPath = "/home/codex/.dotnet/dotnet", WorkerAssemblyPath = Path.Combine(refs, "WarpCLR.CoreCLR.Worker", "release", "WarpCLR.CoreCLR.Worker.dll"),
            }, CancellationToken.None).ConfigureAwait(false);
            service = new(layout, compiled, compiled.TryAcquireLease()!); services.Add(identity, service);
        }
        uint[] state = service.Layout.CreateInitialState(32, 100000000);
        uint[][] inputs = arguments.Select(word => new[] { word }).ToArray();
        for (int attempt = 0; attempt < 50000 && state[0] == WarpLogicalMachineLayout.Runnable; attempt++)
        { await service.Lease.ExecuteManagedQuantumAsync(inputs, [], 0, state, 32, Math.Max(4096, service.Layout.MaximumBlockCost), Arena, CancellationToken.None).ConfigureAwait(false); }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]); return state[WarpLogicalMachineLayout.ResultOffset];
    }
}
