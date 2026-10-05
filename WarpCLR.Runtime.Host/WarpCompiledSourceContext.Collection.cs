using WarpCLR.Compiler;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourceContext
{
    internal uint AdvanceCollection()
    {
        WarpCompiledControllerGrant? grant = controller.TryAcquire();
        if (grant is null) { return WarpPortableSchedulerLayout.Yield; }
        try
        {
            if (Header(WarpPortableSchedulerLayout.GCState) != WarpPortableSchedulerLayout.GCRequested)
            {
                return WarpPortableSchedulerLayout.Invalid;
            }
            if (Header(WarpPortableSchedulerLayout.ServiceOwner) != WarpPortableSchedulerLayout.NoWorker ||
                Header(WarpPortableSchedulerLayout.RunningCount) != 0)
            {
                return WarpPortableSchedulerLayout.NeedGCParking;
            }
            for (uint worker = 0; worker < Plan.Workers; worker++)
            {
                uint state = WorkerState(worker);
                if (state is WarpPortableSchedulerLayout.Completed or WarpPortableSchedulerLayout.Faulted or
                    WarpPortableSchedulerLayout.Cancelled or WarpPortableSchedulerLayout.Disposed or WarpPortableSchedulerLayout.ParkedGC)
                {
                    continue;
                }
                uint generation = Arena[Worker(worker) + WarpPortableSchedulerLayout.RunGeneration];
                RequireSuccess(Publish(grant, worker, generation, Plan.Location(states[worker])));
                RequireSuccess(Invoke(grant, nameof(WarpPortableSchedulerServices.ParkForCollection), [worker, generation, Epoch]));
            }
            uint status = Invoke(grant, nameof(WarpPortableSchedulerServices.BeginCollection), [Epoch]);
            if (status != 0) { return status; }
            uint heapStatus = WarpCompiledRuntimeServices.Run(Plan.Services.Heap(nameof(WarpPortableHeapServices.Collect)),
                Arena, [], Plan.Quantum);
            return Invoke(grant, nameof(WarpPortableSchedulerServices.FinishCollection), [Epoch, heapStatus]);
        }
        finally { controller.Release(grant); }
    }
}
