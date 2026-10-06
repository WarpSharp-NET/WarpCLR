using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using WarpCLR.IR;
using WarpCLR.Compiler;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCompiledRuntimeServices
{
    private const int MaximumServiceQuanta = 1_000_000;
    private readonly FrozenDictionary<string, WarpCompiledWordService> scheduler;
    private readonly FrozenDictionary<string, WarpCompiledWordService> heap;
    private readonly WarpCompiledWordService store;
    private readonly WarpCompiledWordService clear;
    private readonly WarpCompiledWordService rootStore;
    internal WarpCompiledWordService Mirror { get; }
    internal string Identity { get; }
    internal FrozenDictionary<string, string> CleanupKernels { get; }

    internal WarpCompiledRuntimeServices()
    {
        string[] schedulerNames =
        [
            nameof(WarpPortableSchedulerServices.TryAcquireWorker), nameof(WarpPortableSchedulerServices.ChargeQuantum),
            nameof(WarpPortableSchedulerServices.YieldWorker), nameof(WarpPortableSchedulerServices.CompleteWorker),
            nameof(WarpPortableSchedulerServices.ChargeUserSteps), nameof(WarpPortableSchedulerServices.EnterUserFrame),
            nameof(WarpPortableSchedulerServices.ExitUserFrame), nameof(WarpPortableSchedulerServices.PublishRoots),
            nameof(WarpPortableSchedulerServices.RequestCollection), nameof(WarpPortableSchedulerServices.ParkForCollection),
            nameof(WarpPortableSchedulerServices.BeginCollection), nameof(WarpPortableSchedulerServices.FinishCollection),
            nameof(WarpPortableSchedulerServices.AcquireHeapService), nameof(WarpPortableSchedulerServices.CaptureServiceResult),
            nameof(WarpPortableSchedulerServices.AcknowledgeHeapResult), nameof(WarpPortableSchedulerServices.ReleaseHeapService),
            nameof(WarpPortableSchedulerServices.AbortHeapService), nameof(WarpPortableSchedulerServices.SetUserLocation),
            nameof(WarpPortableSchedulerServices.RecordEscapedFault), nameof(WarpPortableSchedulerServices.RequestCancellation),
            nameof(WarpPortableSchedulerServices.CancelWorker), nameof(WarpPortableSchedulerServices.RequestDisposal),
            nameof(WarpPortableSchedulerServices.FinishDisposal), nameof(WarpPortableSchedulerServices.PublishOutputRoots),
            nameof(WarpPortableSchedulerServices.AcknowledgeOutputRoots), nameof(WarpPortableSchedulerServices.ReleaseOutputRoots),
            nameof(WarpPortableSchedulerServices.BeginDispatch), nameof(WarpPortableSchedulerServices.ArriveBarrier),
            nameof(WarpPortableSchedulerServices.DetectStalledCollective), nameof(WarpPortableSchedulerServices.RecordSourceMachineFault),
            nameof(WarpPortableSchedulerServices.DisposeStoppedCensus), nameof(WarpPortableSchedulerServices.DisposeStoppedController),
        ];
        scheduler = schedulerNames.ToFrozenDictionary(name => name,
            name => WarpCompiledWordService.Create(typeof(WarpPortableSchedulerServices), name), StringComparer.Ordinal);
        string[] heapNames = [nameof(WarpPortableHeapServices.AllocateObject), nameof(WarpPortableHeapServices.Collect)];
        heap = heapNames.ToFrozenDictionary(name => name,
            name => WarpCompiledWordService.Create(typeof(WarpPortableHeapServices), name), StringComparer.Ordinal);
        store = WarpCompiledWordService.Create(typeof(WarpPortableHostPublicationServices), nameof(WarpPortableHostPublicationServices.StoreWord));
        clear = WarpCompiledWordService.Create(typeof(WarpPortableHostPublicationServices), nameof(WarpPortableHostPublicationServices.ClearRange));
        rootStore = WarpCompiledWordService.Create(typeof(WarpPortableHostPublicationServices), nameof(WarpPortableHostPublicationServices.StoreRoot));
        Mirror = WarpCompiledWordService.Create(typeof(WarpPortableHostPublicationServices), nameof(WarpPortableHostPublicationServices.MirrorSourceState));
        string canonical = string.Join('\n', scheduler.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":" + WarpIrHash.Compute(pair.Value.Layout.Kernel))) + "\n" +
            string.Join('\n', heap.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":" + WarpIrHash.Compute(pair.Value.Layout.Kernel))) + "\n" +
            string.Join('\n', new[] { store, clear, rootStore, Mirror }.Select(service => WarpIrHash.Compute(service.Layout.Kernel)));
        Identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        CleanupKernels = CleanupNames.ToFrozenDictionary(name => name, name => scheduler[name].KernelHash, StringComparer.Ordinal);
    }

    internal WarpCompiledWordService Heap(string name) => heap[name];
    internal WarpCompiledWordService Scheduler(string name) => scheduler[name];

    internal void PrepareCleanup()
    {
        foreach (string name in CleanupNames) { scheduler[name].Prepare(); }
    }

    private static readonly string[] CleanupNames =
    [
        nameof(WarpPortableSchedulerServices.RecordSourceMachineFault), nameof(WarpPortableSchedulerServices.AbortHeapService),
        nameof(WarpPortableSchedulerServices.CancelWorker), nameof(WarpPortableSchedulerServices.RequestCancellation),
        nameof(WarpPortableSchedulerServices.RequestDisposal), nameof(WarpPortableSchedulerServices.FinishDisposal),
        nameof(WarpPortableSchedulerServices.DisposeStoppedCensus), nameof(WarpPortableSchedulerServices.DisposeStoppedController),
    ];

    internal uint Invoke(string name, uint[] arena, uint schedulerOffset, WarpCompiledControllerGrant grant,
        ReadOnlySpan<uint> arguments, int quantum)
    {
        grant.Controller.Validate(grant);
        uint[] parameters = new uint[checked(arguments.Length + 2)];
        parameters[0] = schedulerOffset;
        parameters[1] = grant.Token;
        arguments.CopyTo(parameters.AsSpan(2));
        return Run(scheduler[name], arena, parameters, quantum);
    }

    internal void Store(uint[] arena, WarpCompiledControllerGrant grant, uint offset, ReadOnlySpan<uint> words, int quantum)
    {
        grant.Controller.Validate(grant);
        for (int index = 0; index < words.Length; index++)
        {
            uint status = Run(store, arena, [checked(offset + (uint)index), words[index]], quantum);
            if (status != 0)
            {
                throw new InvalidOperationException("An admitted precise word publication is outside the context arena.");
            }
        }
    }

    internal void Publish(uint[] arena, WarpCompiledControllerGrant grant, uint offset, ReadOnlySpan<uint> roots, int quantum)
    {
        grant.Controller.Validate(grant);
        if (roots.Length % 3 != 0 || Run(clear, arena, [offset, checked((uint)roots.Length)], quantum) != 0)
        {
            throw new InvalidOperationException("A precise root projection differs from its admitted tuple bank.");
        }
        for (int word = 0; word < roots.Length; word += 3)
        {
            if ((roots[word] | roots[word + 1] | roots[word + 2]) != 0 &&
                Run(rootStore, arena, [checked(offset + (uint)word), roots[word], roots[word + 1], roots[word + 2]], quantum) != 0)
            {
                throw new InvalidOperationException("A precise root tuple publication is outside its admitted bank.");
            }
        }
    }

    internal static uint Run(WarpCompiledWordService service, uint[] arena, ReadOnlySpan<uint> arguments, int quantum)
    {
        WarpCompiledServiceContinuation continuation = service.Start(arguments);
        for (int attempt = 0; attempt < MaximumServiceQuanta; attempt++)
        {
            if (continuation.Resume(arena, quantum))
            {
                return continuation.Result;
            }
        }
        throw new InvalidOperationException("A generated runtime service exceeded its admitted operational continuation quota.");
    }
}
