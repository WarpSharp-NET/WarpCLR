using System.Collections.Concurrent;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

// Generated helper consistency fixture only. Its fixed test data is no private
// registry ticket and never admits or executes the original implicit-fault CIL.
internal sealed partial class WarpPortableFaultTicketDriver
{
    private static readonly ConcurrentDictionary<string, Lazy<(WarpPortableFaultTicketProgram Program, CoreCLRResumableKernel Compiled)>> Programs = new(StringComparer.Ordinal);
    private readonly CoreCLRResumableKernel compiled;
    private readonly int quantum;

    internal WarpPortableFaultTicketDriver(string method, bool smallQuantum)
    {
        (Program, compiled) = Programs.GetOrAdd(method, static name => new(() =>
        {
            WarpPortableFaultTicketProgram program = WarpPortableFaultTicketBuilder.Create(name);
            return (program, CoreCLRResumableKernel.Compile(program.Layout));
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        quantum = smallQuantum ? Program.Layout.MaximumBlockCost : 4096;
        uint[] pool = Program.Pool.AttachToEmptyHeap(Program.Schema.CreateArena(0x37689, 16384, 256, 512, 1, 4096), 128);
        int pc = Program.Layout.GetBlockEntry(1, Program.Waiting);
        var scheduler = new WarpPortableSchedulerSchema(1, 1, (uint)quantum, 64, 100000000, 0, 16,
            [new(0, 0, []), new(1, (uint)pc, [])], []);
        Arena = Program.Exceptions.Attach(scheduler.AttachToEmptyHeap(pool), 8, 8, 8);
        State = Program.Layout.CreateInitialState(128, 100000000);
        PreparePool();
        uint[][] traces = Enumerable.Range(0, 8).Select(_ => AllocateTrace()).ToArray();
        Activate(traces, (uint)pc);
        DriveUntil(() => Arena[ExceptionWorker + WarpPortableExceptionLayout.PreparedRecord] != 0 && AtWait());
        Parameters = CreateParameters();
    }

    internal WarpPortableFaultTicketProgram Program { get; }
    internal uint[] Arena { get; }
    internal uint[] State { get; }
    internal uint[] Parameters { get; }
    internal uint Scheduler => Arena[WarpPortableSchedulerLayout.HeapDescriptor];
    internal uint Pool => Arena[WarpPortableSourceFaultFactoryLayout.Descriptor];
    internal uint Prepared => Arena[Pool + WarpPortableSourceFaultFactoryLayout.PreparedStart];
    internal uint ExceptionDescriptor => Arena[WarpPortableExceptionLayout.Descriptor];
    internal uint ExceptionWorker => ExceptionDescriptor + Arena[ExceptionDescriptor + WarpPortableExceptionLayout.WorkerStart];
    internal uint ExceptionRecord => ExceptionDescriptor + Arena[ExceptionDescriptor + WarpPortableExceptionLayout.RecordStart] +
        (Arena[ExceptionWorker + WarpPortableExceptionLayout.PreparedRecord] - 1) * WarpPortableExceptionLayout.RecordWords;
    internal uint Root(uint id) => Arena[WarpPortableHeapLayout.RootStart] + (id - 1) * WarpPortableHeapLayout.RootWords;
    internal uint Heap(string name, params uint[] arguments) => WarpPortableExceptionTestService.Run(typeof(WarpPortableHeapServices), name, Arena, arguments, quantum);
    internal uint Scheduled(string name, params uint[] arguments) => WarpPortableExceptionTestService.Run(typeof(WarpPortableSchedulerServices), name,
        Arena, [Scheduler, WarpPortableExceptionTestBuilder.Controller, .. arguments], quantum);
    internal uint Exception(string name, params uint[] arguments) => WarpPortableExceptionTestService.Run(typeof(WarpPortableExceptionServices), name,
        Arena, [WarpPortableExceptionTestBuilder.Controller, 0, 1, 1, .. arguments], quantum);
}
