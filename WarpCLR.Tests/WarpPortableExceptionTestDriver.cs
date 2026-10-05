using System.Collections.Concurrent;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed class WarpPortableExceptionTestDriver
{
    private static readonly ConcurrentDictionary<(Type Source, string Method, bool Stop), Lazy<(WarpPortableExceptionTestProgram Program, CoreCLRResumableKernel Compiled)>> Programs = new();
    private readonly CoreCLRResumableKernel compiled;
    private readonly uint[][] inputs;
    private readonly int quantum;
    private readonly bool stopHandlers;

    internal WarpPortableExceptionTestDriver(string method, Type? exceptionType = null, int quantum = 4096, bool stopHandlers = true, Type? source = null)
    {
        this.quantum = quantum;
        this.stopHandlers = stopHandlers;
        (Program, compiled) = Programs.GetOrAdd((source ?? typeof(WarpPortableExceptionFixtureSources), method, stopHandlers), static key => new(() =>
        {
            WarpPortableExceptionTestProgram program = WarpPortableExceptionTestBuilder.Create(key.Source.GetMethod(key.Method)!, key.Stop);
            return (program, CoreCLRResumableKernel.Compile(program.Layout));
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        uint[] heap = Program.Schema.CreateArena(0x13579, 8192, 128, 512, 1, 8192);
        var scheduler = new WarpPortableSchedulerSchema(1, 1, (uint)quantum, 32, 100000000, 0, 16, [new(0, 0, [])], []);
        Arena = Program.Plan.Attach(scheduler.AttachToEmptyHeap(heap), 8, 8, 8);
        ExceptionType = Id(exceptionType ?? typeof(InvalidOperationException));
        Owner = Allocate(ExceptionType, array: false);
        TemporaryOwner = Program.Bindings[0].PrivateTemporaries.IsEmpty ? [] : Allocate(
            Program.Schema.TypeId(Program.Bindings[0].PrivateTemporaries[0].Owners[0].TypeIdentity), array: false);
        var traces = new List<uint[]>();
        for (int report = 0; report < 8; report++) { traces.Add(Allocate(Program.Plan.TraceType, array: true)); }
        Scheduler = Arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Arena[Scheduler + WarpPortableSchedulerLayout.ControllerOwner] = WarpPortableExceptionTestBuilder.Controller;
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.TryAcquireWorker), 0));
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.AcquireHeapService), 0, 1));
        for (uint report = 0; report < (uint)traces.Count; report++)
        {
            Assert.AreEqual(0u, Service(nameof(WarpPortableExceptionServices.BindTraceReport), report + 1, traces[(int)report][0], traces[(int)report][1], traces[(int)report][2]));
        }
        Assert.AreEqual(0u, Service(nameof(WarpPortableExceptionServices.SealReports)));
        inputs = Owner.Concat(TemporaryOwner).Select(word => new[] { word }).ToArray();
        State = Program.Layout.CreateInitialState(128, 100000000);
        CaptureInitial();
    }

    internal WarpPortableExceptionTestProgram Program { get; }
    internal uint[] Arena { get; }
    internal uint[] State { get; }
    internal uint[] Owner { get; }
    internal uint[] TemporaryOwner { get; }
    internal uint ExceptionType { get; }
    internal uint Scheduler { get; }
    internal uint Run => Arena[Scheduler + Arena[Scheduler + WarpPortableSchedulerLayout.WorkerStart] + WarpPortableSchedulerLayout.RunGeneration];
    internal uint Descriptor => Arena[WarpPortableExceptionLayout.Descriptor];
    internal uint Worker => Descriptor + Arena[Descriptor + WarpPortableExceptionLayout.WorkerStart];
    internal uint Active => Record(Arena[Worker + WarpPortableExceptionLayout.ActiveRecord]);
    internal uint Prepared => Record(Arena[Worker + WarpPortableExceptionLayout.PreparedRecord]);
    internal uint Raise => Arena[Active + WarpPortableExceptionLayout.RaiseGeneration];
    internal uint Id(Type type) => Program.Schema.TypeId(WarpCLR.Verifier.WarpPortableMethodGraphIdentity.Type(type));
    internal uint Record(uint index) => Descriptor + Arena[Descriptor + WarpPortableExceptionLayout.RecordStart] + (index - 1) * WarpPortableExceptionLayout.RecordWords;
    internal uint Clause(uint index) => Descriptor + Arena[Descriptor + WarpPortableExceptionLayout.ClauseStart] + (index - 1) * WarpPortableExceptionLayout.ClauseWords;
    internal uint Captured(uint index, uint frame) => Descriptor + Arena[Descriptor + WarpPortableExceptionLayout.FrameStart] +
        ((index - 1) * 8 + frame - 1) * WarpPortableExceptionLayout.FrameWords;
    internal uint Site(uint index) => Descriptor + Arena[Descriptor + WarpPortableExceptionLayout.SiteStart] + (index - 1) * WarpPortableExceptionLayout.SiteWords;
    internal uint Payload(uint[] owner) => Arena[Arena[WarpPortableHeapLayout.SlotStart] + (owner[1] - 1) * WarpPortableHeapLayout.SlotWords + WarpPortableHeapLayout.SlotPayload];

    internal uint Service(string name, params uint[] arguments) => WarpPortableExceptionTestService.Run(typeof(WarpPortableExceptionServices), name,
        Arena, [WarpPortableExceptionTestBuilder.Controller, 0, Run, 1, .. arguments], quantum);
    internal uint Heap(string name, params uint[] arguments) => WarpPortableExceptionTestService.Run(typeof(WarpPortableHeapServices), name, Arena, arguments, quantum);
    internal uint Scheduled(string name, params uint[] arguments) => WarpPortableExceptionTestService.Run(typeof(WarpPortableSchedulerServices), name,
        Arena, [Scheduler, WarpPortableExceptionTestBuilder.Controller, .. arguments], quantum);

    private uint[] Allocate(uint type, bool array)
    {
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcquireServiceLease), uint.MaxValue));
        Assert.AreEqual(0u, Heap(array ? nameof(WarpPortableHeapServices.AllocateArray) : nameof(WarpPortableHeapServices.AllocateObject),
            array ? [type, WarpPortableExceptionTraceLayout.HeaderWords + 8 * WarpPortableExceptionTraceLayout.FrameWords * 2] : [type]));
        uint[] reference = Arena.AsSpan((int)WarpPortableHeapLayout.Result, 3).ToArray();
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), uint.MaxValue));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReleaseServiceLease), uint.MaxValue));
        return reference;
    }

    internal void CaptureInitial() => DriveUntil(() => Arena[Worker + WarpPortableExceptionLayout.PreparedRecord] != 0 && AtFixtureWait());

    private bool AtFixtureWait()
    {
        uint depth = State[WarpLogicalMachineLayout.DepthOffset];
        if (depth == 0) { return false; }
        int frame = checked(WarpLogicalMachineLayout.HeaderWords + ((int)depth - 1) * Program.Layout.FrameWords);
        int pc = checked((int)State[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]);
        WarpLogicalMachineNode node = Program.Layout.Nodes[pc];
        if (Program.Layout.IsRuntimeHelper(node.Function)) { return false; }
        int count = Program.Bindings.First(body => body.Function == node.Function).Sites.Length;
        int operation = (node.Block - count - 1) % 6;
        return node.Block > count && node.Block <= count * 7 && operation is 0 or 5;
    }

    internal void DriveUntil(Func<bool> completed)
    {
        for (int iteration = 0; iteration < 1000000; iteration++)
        {
            if (completed()) { return; }
            Assert.AreEqual(WarpLogicalMachineLayout.Runnable, State[WarpLogicalMachineLayout.StatusOffset],
                $"Unexpected machine fault {State[WarpLogicalMachineLayout.FaultKindOffset]} at {State[WarpLogicalMachineLayout.FaultFunctionOffset]}/{State[WarpLogicalMachineLayout.FaultBlockOffset]}");
            compiled.ExecuteManagedQuantum(inputs, [], 0, State, 128, Math.Max(quantum, Program.Layout.MaximumBlockCost), Arena, CancellationToken.None);
        }
        Assert.Fail("The compiled runtime witness exhausted its bounded progress iterations.");
    }

    internal void Move(int function, int offset)
    {
        if (Arena[Worker + WarpPortableExceptionLayout.PreparedRecord] != 0) { Assert.AreEqual(0u, Service(nameof(WarpPortableExceptionServices.DiscardPrepared))); }
        uint scratch = Arena[WarpPortableHeapLayout.ScratchStart];
        Arena[scratch + 1] = (uint)Program.Layout.GetBlockEntry(function, Program.Entries[(function, offset)]);
        Arena[scratch] = 2;
        DriveUntil(() => Arena[scratch] == 0 && Arena[Worker + WarpPortableExceptionLayout.PreparedRecord] != 0 && AtFixtureWait());
    }

    internal void Apply(bool terminal = false)
    {
        if (Arena[Worker + WarpPortableExceptionLayout.PreparedRecord] != 0) { Assert.AreEqual(0u, Service(nameof(WarpPortableExceptionServices.DiscardPrepared))); }
        Arena[Arena[WarpPortableHeapLayout.ScratchStart]] = 1;
        DriveUntil(() => State[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable ||
            !terminal && stopHandlers && Arena[Worker + WarpPortableExceptionLayout.PreparedRecord] != 0 && AtFixtureWait());
        Arena[Arena[WarpPortableHeapLayout.ScratchStart]] = 0;
    }

    internal uint RaiseOwner(int function = 0) => RaiseReference(Owner, ExceptionType, function);

    internal uint RaiseReference(uint[] owner, uint type, int function = 0)
    {
        uint count = Arena[Prepared + WarpPortableExceptionLayout.FrameCount];
        uint frame = Descriptor + Arena[Descriptor + WarpPortableExceptionLayout.FrameStart] +
            ((Arena[Worker + WarpPortableExceptionLayout.PreparedRecord] - 1) * 8 + count - 1) * WarpPortableExceptionLayout.FrameWords;
        uint siteId = Arena[frame + WarpPortableExceptionLayout.FrameSite];
        uint site = Descriptor + Arena[Descriptor + WarpPortableExceptionLayout.SiteStart] + (siteId - 1) * WarpPortableExceptionLayout.SiteWords;
        return Service(nameof(WarpPortableExceptionServices.RaiseReference), (uint)function,
            Arena[site + WarpPortableExceptionLayout.SiteOffset], Arena[site + WarpPortableExceptionLayout.SiteOpCode], 1,
            owner[0], owner[1], owner[2], type);
    }

    internal uint[] AllocateReplacement(uint type)
    {
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AllocateObject), type));
        uint[] owner = Arena.AsSpan((int)WarpPortableHeapLayout.Result, 3).ToArray();
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), 0));
        return owner;
    }
}
