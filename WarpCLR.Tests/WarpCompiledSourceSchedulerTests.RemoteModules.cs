using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    private sealed class RemoteModules : IAsyncDisposable
    {
        private readonly WarpCoreCLRWorkerKernel[] kernels;
        private RemoteModules(WarpCoreCLRWorkerKernel[] kernels)
        {
            this.kernels = kernels;
            Source = kernels[0].TryAcquireLease()!;
            CompareExchange = kernels[1].TryAcquireLease()!;
            Census = kernels[2].TryAcquireLease()!;
            Publication = kernels[3].TryAcquireLease()!;
            Stopped = kernels[4].TryAcquireLease()!;
        }
        internal WarpCoreCLRWorkerLease Source { get; }
        internal WarpCoreCLRWorkerLease CompareExchange { get; }
        internal WarpCoreCLRWorkerLease Census { get; }
        internal WarpCoreCLRWorkerLease Publication { get; }
        internal WarpCoreCLRWorkerLease Stopped { get; }
        internal static async Task<RemoteModules> CreateAsync(WarpCompiledSourceContext context, WarpCoreCLRWorkerOptions sourceOptions,
            WarpCoreCLRWorkerOptions? publicationOptions = null)
        {
            WarpLogicalMachineLayout exchange = WarpManagedAtomicKernels.Create32()[2];
            WarpLogicalMachineLayout census = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableSchedulerServices)
                .GetMethod(nameof(WarpPortableSchedulerServices.DisposeStoppedCensus))!);
            WarpLogicalMachineLayout stopped = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableSchedulerServices)
                .GetMethod(nameof(WarpPortableSchedulerServices.DisposeStoppedController))!);
            WarpLogicalMachineLayout[] layouts = [context.Plan.Layout, exchange, census, exchange, stopped];
            var prepared = new List<WarpCoreCLRWorkerKernel>();
            try
            {
                for (int index = 0; index < layouts.Length; index++)
                {
                    WarpCoreCLRWorkerOptions options = index == 0 ? sourceOptions : index == 3 ? publicationOptions ?? new() : new();
                    prepared.Add(await WarpCoreCLRWorkerKernel.CompileAsync(layouts[index], options, CancellationToken.None).ConfigureAwait(false));
                }
                return new(prepared.ToArray());
            }
            catch
            {
                foreach (WarpCoreCLRWorkerKernel kernel in prepared.AsEnumerable().Reverse()) { await kernel.DisposeAsync().ConfigureAwait(false); }
                throw;
            }
        }
        public async ValueTask DisposeAsync()
        {
            foreach (WarpCoreCLRWorkerLease lease in new[] { Stopped, Publication, Census, CompareExchange, Source })
            { await lease.DisposeAsync().ConfigureAwait(false); }
            foreach (WarpCoreCLRWorkerKernel kernel in kernels.Reverse()) { await kernel.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
