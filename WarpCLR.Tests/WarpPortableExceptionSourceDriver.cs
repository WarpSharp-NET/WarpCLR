using System.Collections.Concurrent;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionSourceDriver
{
    private static readonly ConcurrentDictionary<string, Lazy<(WarpPortableExceptionSourceProgram Program, CoreCLRResumableKernel Compiled)>> Programs = new(StringComparer.Ordinal);
    private readonly CoreCLRResumableKernel compiled;
    private readonly uint[][] inputs;
    private readonly int quantum;
    private readonly Dictionary<(int Function, int Pc), uint> maps = [];
    private readonly List<(uint Root, uint Generation)> inputRoots = [];
    internal const int RootCapacity = 64;

    internal WarpPortableExceptionSourceDriver(string method, int quantum = 4096, Type? exceptionType = null, Type? replacementType = null, uint? flag = null,
        int logicalWorker = 0, int workerCount = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(logicalWorker);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(workerCount, logicalWorker);
        LogicalWorker = (uint)logicalWorker;
        this.quantum = quantum;
        (Program, compiled) = Programs.GetOrAdd(method, static method => new(() => Build(method), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        uint[] heap = Program.Schema.CreateArena(0x24681, 16384, 256, 1024, (uint)workerCount, 16384);
        var roots = new List<WarpPortableSchedulerRootLayout> { new(0, 0, []) };
        foreach (WarpPortableWordBody body in Program.Lowered.Bodies)
        {
            foreach (WarpPortableWordSourceBlock block in body.SourceBlocks)
            {
                int pc = Program.Layout.GetBlockEntry(body.Function, block.Block);
                maps.Add((body.Function, pc), (uint)roots.Count);
                roots.Add(new((uint)body.Function, (uint)pc, Enumerable.Range(0, RootCapacity).Select(index => (uint)(index * 3)).ToArray()));
            }
        }
        Assert.IsFalse(Program.Lowered.EntryProjection.ResultRoots.Any(root => root.IsInteriorOwner));
        var scheduler = new WarpPortableSchedulerSchema((uint)workerCount, 1, (uint)quantum, 64, 100000000, 0, RootCapacity * 3, roots, [],
            Program.Lowered.EntryProjection.ResultRoots.Select(root => checked((uint)root.ResultWordOffset)));
        Arena = Program.Plan.Attach(scheduler.AttachToEmptyHeap(heap), 16, 16, 16);
        uint exception = Program.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(exceptionType ?? typeof(InvalidOperationException)));
        Owner = Allocate(exception, array: false); RootInput(Owner);
        SecondOwner = replacementType is null ? [] : Allocate(Program.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(replacementType)), array: false);
        if (SecondOwner.Length != 0) { RootInput(SecondOwner); }
        var traces = new List<uint[]>();
        for (int index = 0; index < 16; index++) { traces.Add(Allocate(Program.Plan.TraceType, array: true)); }
        Scheduler = Arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Activate(traces);
        inputs = Owner.Concat(SecondOwner).Concat(flag is null ? [] : new[] { flag.Value }).Select(word =>
        {
            uint[] bank = new uint[workerCount]; bank[logicalWorker] = word; return bank;
        }).ToArray();
        State = Program.Layout.CreateInitialState(256, 100000000);
        Program.Layout.SetSourceBoundaryMode(State, true);
    }

    private void Activate(List<uint[]> traces)
    {
        Arena[Scheduler + WarpPortableSchedulerLayout.ControllerOwner] = WarpPortableExceptionTestBuilder.Controller;
        for (uint candidate = 0; candidate <= LogicalWorker; candidate++)
        {
            Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.TryAcquireWorker), 0));
            Assert.AreEqual(candidate, Arena[Scheduler + WarpPortableSchedulerLayout.Result]);
            if (candidate != LogicalWorker) { Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.YieldWorker), candidate, 1)); }
        }
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.AcquireHeapService), LogicalWorker, RunGeneration));
        for (int index = 0; index < traces.Count; index++)
        {
            uint[] trace = traces[index];
            Assert.AreEqual(0u, Service(nameof(WarpPortableExceptionServices.BindTraceReport), (uint)index + 1, trace[0], trace[1], trace[2]));
        }
        Assert.AreEqual(0u, Service(nameof(WarpPortableExceptionServices.SealReports)));
    }

    internal WarpPortableExceptionSourceProgram Program { get; }
    internal uint[] Arena { get; }
    internal uint[] State { get; }
    internal uint[] Owner { get; }
    internal uint[] SecondOwner { get; }
    internal uint Scheduler { get; }
    internal uint LogicalWorker { get; }
    internal uint ScheduledWorker => Scheduler + Arena[Scheduler + WarpPortableSchedulerLayout.WorkerStart] + LogicalWorker * WarpPortableSchedulerLayout.WorkerWords;
    internal uint RunGeneration => Arena[ScheduledWorker + WarpPortableSchedulerLayout.RunGeneration];
    internal uint DispatchGeneration => Arena[Scheduler + WarpPortableSchedulerLayout.DispatchGeneration];
    internal uint Descriptor => Arena[WarpPortableExceptionLayout.Descriptor];
    internal uint Worker => Descriptor + Arena[Descriptor + WarpPortableExceptionLayout.WorkerStart] + LogicalWorker * WarpPortableExceptionLayout.WorkerWords;
    internal uint ActiveRecord => Descriptor + Arena[Descriptor + WarpPortableExceptionLayout.RecordStart] +
        (LogicalWorker * Arena[Descriptor + WarpPortableExceptionLayout.RecordsPerWorker] + Arena[Worker + WarpPortableExceptionLayout.ActiveRecord] - 1) * WarpPortableExceptionLayout.RecordWords;
    internal uint Boundaries { get; private set; }
    internal uint Quanta { get; private set; }
    internal string Diagnostic()
    {
        var frames = new List<string>();
        for (uint physical = 0; physical < State[WarpLogicalMachineLayout.DepthOffset]; physical++)
        {
            int frame = WarpLogicalMachineLayout.HeaderWords + checked((int)physical) * Program.Layout.FrameWords;
            int function = checked((int)State[frame]); int pc = checked((int)State[frame + 1]);
            string name = function == 0 ? "wrapper" : Program.Layout.Kernel.Functions[function - 1].Name;
            frames.Add($"{physical + 1}:{name} PC{pc}/block{Program.Layout.Nodes[pc].Block} [{string.Join(',', State.AsSpan(frame, 8).ToArray())}]");
        }
        uint active = Arena[Worker + WarpPortableExceptionLayout.ActiveRecord];
        uint record = ActiveRecord;
        return $"header [{string.Join(',', State.AsSpan(0, 28).ToArray())}], record [{(active == 0 ? "none" : string.Join(',', Arena.AsSpan((int)record, 64).ToArray()))}], frames {string.Join(';', frames)}";
    }

    private static (WarpPortableExceptionSourceProgram, CoreCLRResumableKernel) Build(string method)
    {
        System.Reflection.MethodInfo entry = string.Equals(method, WarpPortableExceptionCilFixtures.FaultName, StringComparison.Ordinal) ?
            WarpPortableExceptionCilFixtures.Fault : typeof(WarpPortableExceptionFixtureSources).GetMethod(method)!;
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(entry,
            concreteTypes: [typeof(StackOverflowException), typeof(InvalidOperationException)]);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        var binding = new WarpPortableExceptionSourceBinding(graph, typed, schema, WarpPortableExceptionTestBuilder.Controller);
        WarpPortableWordLoweredProgram lowered = WarpPortableWordLowerer.Lower(graph, typed, schema, binding);
        var layout = new WarpLogicalMachineLayout(lowered.Kernel);
        Assert.AreEqual(binding.Plan.LayoutHash, WarpPortableExceptionPlan.ComputeLayoutHash(layout), StringComparer.Ordinal);
        return (new(graph, typed, schema, lowered, binding.Plan, layout), CoreCLRResumableKernel.Compile(layout));
    }

    internal uint Heap(string name, params uint[] arguments) => WarpPortableExceptionTestService.Run(typeof(WarpPortableHeapServices), name, Arena, arguments, quantum);
    internal uint Scheduled(string name, params uint[] arguments) => WarpPortableExceptionTestService.Run(typeof(WarpPortableSchedulerServices), name,
        Arena, [Scheduler, WarpPortableExceptionTestBuilder.Controller, .. arguments], quantum);
    internal uint Service(string name, params uint[] arguments) => WarpPortableExceptionTestService.Run(typeof(WarpPortableExceptionServices), name,
        Arena, [WarpPortableExceptionTestBuilder.Controller, LogicalWorker, RunGeneration, DispatchGeneration, .. arguments], quantum);

    private uint[] Allocate(uint type, bool array)
    {
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcquireServiceLease), uint.MaxValue));
        Assert.AreEqual(0u, Heap(array ? nameof(WarpPortableHeapServices.AllocateArray) : nameof(WarpPortableHeapServices.AllocateObject),
            array ? [type, WarpPortableExceptionTraceLayout.HeaderWords + 16 * WarpPortableExceptionTraceLayout.FrameWords * 2] : [type]));
        uint[] owner = Arena.AsSpan((int)WarpPortableHeapLayout.Result, 3).ToArray();
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), uint.MaxValue));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReleaseServiceLease), uint.MaxValue)); return owner;
    }

    private void RootInput(uint[] owner)
    {
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcquireRoot), owner[0], owner[1], owner[2], WarpPortableHeapLayout.StrongRoot, 0, 0, 0));
        inputRoots.Add((Arena[WarpPortableHeapLayout.Result], Arena[WarpPortableHeapLayout.Result + 1]));
    }
}
