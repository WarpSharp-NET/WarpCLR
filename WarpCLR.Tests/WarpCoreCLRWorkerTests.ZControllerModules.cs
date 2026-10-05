using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    private sealed class ControllerModules : IAsyncDisposable
    {
        private readonly List<WarpCoreCLRWorkerKernel> kernels = [];
        private readonly List<WarpCoreCLRWorkerLease> leases = [];
        private int failService;
        private int failRelease;
        internal WarpCoreCLRWorkerLease Service { get; private set; } = null!;
        internal WarpCoreCLRWorkerLease Emergency { get; private set; } = null!;
        internal WarpCoreCLRWorkerLease Publication { get; private set; } = null!;
        internal WarpCoreCLRWorkerLease Stopped { get; private set; } = null!;
        internal WarpCoreCLRWorkerLease Census { get; private set; } = null!;
        internal WarpCoreCLRWorkerLease Source { get; private set; } = null!;
        internal uint[]? FailedReturnedArena { get; private set; }
        internal uint[]? FailedReturnedState { get; private set; }

        internal static async Task<ControllerModules> CreateAsync(WarpCoreCLRControllerOperation operation)
        {
            var result = new ControllerModules();
            try
            {
                string method = operation == WarpCoreCLRControllerOperation.RequestCollection ? nameof(WarpPortableSchedulerServices.RequestCollection) : nameof(WarpPortableSchedulerServices.BeginDispatch);
                result.Service = await result.CompileAsync(ServiceLayout(method), new()
                { BeforeControllerResultPublication = result.ObserveService }).ConfigureAwait(false);
                WarpLogicalMachineLayout cas = WarpManagedAtomicKernels.Create32()[2];
                result.Emergency = await result.CompileAsync(cas).ConfigureAwait(false);
                result.Publication = await result.CompileAsync(cas, new()
                { BeforeControllerResultPublication = result.ObserveRelease }).ConfigureAwait(false);
                result.Stopped = await result.CompileAsync(ServiceLayout(nameof(WarpPortableSchedulerServices.DisposeStoppedController))).ConfigureAwait(false);
                result.Census = await result.CompileAsync(ServiceLayout(nameof(WarpPortableSchedulerServices.DisposeStoppedCensus))).ConfigureAwait(false);
                WarpControlFlowKernel source = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(typeof(TestKernels).GetMethod(nameof(TestKernels.Branch))!, 1)).ControlFlow;
                result.Source = await result.CompileAsync(new(source)).ConfigureAwait(false);
                return result;
            }
            catch { await result.DisposeAsync().ConfigureAwait(false); throw; }
        }

        private static WarpLogicalMachineLayout ServiceLayout(string method) => WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableSchedulerServices).GetMethod(method)!);

        private async Task<WarpCoreCLRWorkerLease> CompileAsync(WarpLogicalMachineLayout layout, WarpCoreCLRWorkerTestHooks? probes = null)
        {
            WarpCoreCLRWorkerKernel kernel = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None, probes).ConfigureAwait(false);
            kernels.Add(kernel);
            WarpCoreCLRWorkerLease lease = kernel.TryAcquireLease()!; leases.Add(lease); return lease;
        }

        internal void FailNextServiceResponse() => Volatile.Write(ref failService, 1);
        internal void FailReturnedGeneration() => Volatile.Write(ref failService, 2);
        internal void FailNextReleaseResponse() => Volatile.Write(ref failRelease, 1);

        private void ObserveService(IReadOnlyList<uint> state, IReadOnlyList<uint> arena)
        {
            int failure = Volatile.Read(ref failService);
            uint scheduler = arena[0] == WarpPortableHeapLayout.Magic ? arena[(int)WarpPortableSchedulerLayout.HeapDescriptor] : 0;
            if (failure == 0 || failure == 2 && arena[(int)(scheduler + WarpPortableSchedulerLayout.DispatchGeneration)] == 1 && arena[(int)(scheduler + WarpPortableSchedulerLayout.GCEpoch)] == 0) { return; }
            FailedReturnedArena = arena.ToArray(); FailedReturnedState = state.ToArray();
            throw new IOException("Actual authenticated controller response deliberately aborted before publication.");
        }

        private void ObserveRelease(IReadOnlyList<uint> state, IReadOnlyList<uint> arena)
        {
            if (Volatile.Read(ref failRelease) == 0) { return; }
            FailedReturnedArena = arena.ToArray(); FailedReturnedState = state.ToArray();
            throw new IOException("Actual native publication CAS deliberately aborted before parent commit.");
        }

        public async ValueTask DisposeAsync()
        {
            for (int index = leases.Count - 1; index >= 0; index--) { await leases[index].DisposeAsync().ConfigureAwait(false); }
            for (int index = kernels.Count - 1; index >= 0; index--) { await kernels[index].DisposeAsync().ConfigureAwait(false); }
        }
    }
}
