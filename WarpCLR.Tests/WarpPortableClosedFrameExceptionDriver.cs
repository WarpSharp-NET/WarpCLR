using System.Collections.Concurrent;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

// Isolated proof controller for the exact captured fixture. This class is not a
// production invocation authority and does not issue a prepared-fault ticket.
internal sealed partial class WarpPortableClosedFrameExceptionDriver
{
    internal const int RootCapacity = 256;
    internal const int MaximumDepth = 256;
    private static readonly ConcurrentDictionary<string, Lazy<(WarpPortableExceptionSourceProgram Program,
        WarpPortableSourceFrameSchema Frames, CoreCLRResumableKernel Compiled)>> Programs = new(StringComparer.Ordinal);
    private readonly CoreCLRResumableKernel compiled;
    private readonly uint[][] inputs;
    private readonly int quantum;
    private readonly Dictionary<(int Function, int Pc), uint> maps = [];
    private readonly List<(uint Root, uint Generation)> inputRoots = [];

    internal WarpPortableClosedFrameExceptionDriver(string method, int quantum = 4096, uint? flag = null)
    {
        this.quantum = quantum;
        (Program, Frames, compiled) = Programs.GetOrAdd(method, static source =>
            new(() => Build(source), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        uint[] heap = Program.Schema.CreateArena(0x37681, 32768, 256, 2048, 1, 32768, Frames);
        var roots = new List<WarpPortableSchedulerRootLayout> { new(0, 0, []) };
        foreach (WarpPortableWordBody body in Program.Lowered.Bodies)
        {
            foreach (WarpPortableWordSourceBlock block in body.SourceBlocks)
            {
                int pc = Program.Layout.GetBlockEntry(body.Function, block.Block);
                maps.Add((body.Function, pc), (uint)roots.Count);
                roots.Add(new((uint)body.Function, (uint)pc,
                    Enumerable.Range(0, RootCapacity).Select(index => (uint)(index * 3)).ToArray()));
            }
        }
        Assert.IsFalse(Program.Lowered.EntryProjection.ResultRoots.Any(root => root.IsInteriorOwner));
        var scheduler = new WarpPortableSchedulerSchema(1, 1, (uint)quantum, 64, 100000000, 0,
            RootCapacity * 3, roots, [], Program.Lowered.EntryProjection.ResultRoots.Select(root => checked((uint)root.ResultWordOffset)));
        Arena = Program.Plan.Attach(scheduler.AttachToEmptyHeap(heap), 16, 16, 16);
        Owner = Allocate(Program.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(InvalidOperationException))), array: false);
        RootInput(Owner);
        var traces = new List<uint[]>();
        for (int index = 0; index < 16; index++) { traces.Add(Allocate(Program.Plan.TraceType, array: true)); }
        Scheduler = Arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Activate(traces);
        inputs = Owner.Concat(flag is null ? [] : new[] { flag.Value }).Select(word => new[] { word }).ToArray();
        State = Program.Layout.CreateInitialState(MaximumDepth, 100000000);
        Program.Layout.SetSourceBoundaryMode(State, true);
    }

    internal WarpPortableExceptionSourceProgram Program { get; }
    internal WarpPortableSourceFrameSchema Frames { get; }
    internal uint[] Arena { get; }
    internal uint[] State { get; }
    internal uint[] Owner { get; }
    internal uint Scheduler { get; }
    internal uint ScheduledWorker => Scheduler + Arena[Scheduler + WarpPortableSchedulerLayout.WorkerStart];
    internal uint RunGeneration => Arena[ScheduledWorker + WarpPortableSchedulerLayout.RunGeneration];
    internal uint DispatchGeneration => Arena[Scheduler + WarpPortableSchedulerLayout.DispatchGeneration];
    internal uint Descriptor => Arena[WarpPortableExceptionLayout.Descriptor];
    internal uint Worker => Descriptor + Arena[Descriptor + WarpPortableExceptionLayout.WorkerStart];
    internal uint ActiveRecord => Descriptor + Arena[Descriptor + WarpPortableExceptionLayout.RecordStart] +
        (Arena[Worker + WarpPortableExceptionLayout.ActiveRecord] - 1) * WarpPortableExceptionLayout.RecordWords;
    internal uint Boundaries { get; private set; }
    internal uint Quanta { get; private set; }
    internal uint FrameOwnersValidated { get; private set; }

    private static (WarpPortableExceptionSourceProgram, WarpPortableSourceFrameSchema, CoreCLRResumableKernel) Build(string method)
    {
        WarpPortableExceptionSourceProgram program = WarpPortableClosedFrameExceptionCases.Capture(method);
        WarpPortableSourceFrameSchema frames = WarpPortableSourceFrameSchema.Create(program.Schema, program.Lowered);
        return (program, frames, CoreCLRResumableKernel.Compile(program.Layout));
    }

    private void Activate(List<uint[]> traces)
    {
        Arena[Scheduler + WarpPortableSchedulerLayout.ControllerOwner] = WarpPortableExceptionTestBuilder.Controller;
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.TryAcquireWorker), 0));
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.AcquireHeapService), 0, RunGeneration));
        for (int index = 0; index < traces.Count; index++)
        {
            uint[] trace = traces[index];
            Assert.AreEqual(0u, Service(nameof(WarpPortableExceptionServices.BindTraceReport), (uint)index + 1,
                trace[0], trace[1], trace[2]));
        }
        Assert.AreEqual(0u, Service(nameof(WarpPortableExceptionServices.SealReports)));
    }

    internal uint Heap(string name, params uint[] arguments) =>
        WarpPortableExceptionTestService.Run(typeof(WarpPortableHeapServices), name, Arena, arguments, quantum);

    internal uint Scheduled(string name, params uint[] arguments) =>
        WarpPortableExceptionTestService.Run(typeof(WarpPortableSchedulerServices), name, Arena,
            [Scheduler, WarpPortableExceptionTestBuilder.Controller, .. arguments], quantum);

    internal uint Service(string name, params uint[] arguments) =>
        WarpPortableExceptionTestService.Run(typeof(WarpPortableExceptionServices), name, Arena,
            [WarpPortableExceptionTestBuilder.Controller, 0, RunGeneration, DispatchGeneration, .. arguments], quantum);

    private uint[] Allocate(uint type, bool array)
    {
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcquireServiceLease), uint.MaxValue));
        Assert.AreEqual(0u, Heap(array ? nameof(WarpPortableHeapServices.AllocateArray) : nameof(WarpPortableHeapServices.AllocateObject),
            array ? [type, WarpPortableExceptionTraceLayout.HeaderWords + 16 * WarpPortableExceptionTraceLayout.FrameWords * 2] : [type]));
        uint[] owner = Arena.AsSpan((int)WarpPortableHeapLayout.Result, 3).ToArray();
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), uint.MaxValue));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReleaseServiceLease), uint.MaxValue));
        return owner;
    }

    private void RootInput(uint[] owner)
    {
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcquireRoot), owner[0], owner[1], owner[2],
            WarpPortableHeapLayout.StrongRoot, 0, 0, 0));
        inputRoots.Add((Arena[WarpPortableHeapLayout.Result], Arena[WarpPortableHeapLayout.Result + 1]));
    }
}
