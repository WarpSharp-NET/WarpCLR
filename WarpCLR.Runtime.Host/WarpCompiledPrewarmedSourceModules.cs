using System.Runtime.ExceptionServices;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCompiledPrewarmedSourceModules : IAsyncDisposable
{
    internal const string DisposalSemantics = "warp.compiled-source-prewarming/concurrent-joined-stop-and-lease-distinct-exception-objects-stable-first-outcome/0.1";
    private readonly WarpCoreCLRWorkerKernel[] kernels;
    private readonly WarpCoreCLRWorkerLease[] leases;
    private readonly WarpCompiledSourceContext context;
    private readonly Lock gate = new();
    private Task? disposal;

    private WarpCompiledPrewarmedSourceModules(WarpCompiledSourceContext context, WarpCoreCLRWorkerKernel[] kernels, WarpCoreCLRWorkerLease[] leases)
    { this.context = context; this.kernels = kernels; this.leases = leases; }

    internal WarpCoreCLRWorkerLease Source => leases[0];
    internal WarpCoreCLRWorkerLease CompareExchange => leases[1];
    internal WarpCoreCLRWorkerLease Census => leases[2];
    internal WarpCoreCLRWorkerLease PublicationRelease => leases[3];
    internal WarpCoreCLRWorkerLease DisposeController => leases[4];

    internal static async Task<WarpCompiledPrewarmedSourceModules> CreateAsync(WarpCompiledSourceContext context,
        WarpCoreCLRWorkerOptions options, WarpCoreCLRWorkerTestHooks? probes = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        WarpCoreCLRWorkerOptions.Validate(options);
        WarpCompiledSourceContext.PrewarmScope startup = context.BeginSourcePrewarming();
        var preparedKernels = new List<WarpCoreCLRWorkerKernel>();
        var preparedLeases = new List<WarpCoreCLRWorkerLease>();
        try
        {
            WarpLogicalMachineLayout[] layouts =
            [
                context.Plan.Layout,
                new(WarpManagedAtomicKernels.Create32()[2].Kernel),
                context.Plan.Services.Scheduler(nameof(WarpPortableSchedulerServices.DisposeStoppedCensus)).Layout,
                new(WarpManagedAtomicKernels.Create32()[2].Kernel),
                context.Plan.Services.Scheduler(nameof(WarpPortableSchedulerServices.DisposeStoppedController)).Layout,
            ];
            foreach (WarpLogicalMachineLayout layout in layouts)
            {
                WarpCoreCLRWorkerKernel kernel = await WarpCoreCLRWorkerKernel.CompileAsync(layout, options, cancellationToken, probes).ConfigureAwait(false);
                preparedKernels.Add(kernel);
                WarpCoreCLRWorkerLease lease = kernel.TryAcquireLease()
                    ?? throw new InvalidOperationException("A freshly compiled source role could not retain its exact live child lease.");
                preparedLeases.Add(lease);
            }
            if (preparedLeases.Select(lease => lease.ProcessId).Distinct().Count() != layouts.Length ||
                preparedLeases.Select(lease => lease.CompiledModule).Distinct().Count() != layouts.Length ||
                preparedLeases.Any(lease => lease.IsFaulted || lease.IsReleased || lease.CompiledModule == Guid.Empty || lease.ProcessId <= 1))
            { throw new InvalidOperationException("Source, normal release and emergency cleanup require distinct actual live children and compiled modules."); }
            for (int i = 0; i < layouts.Length; i++)
            {
                if (!string.Equals(WarpIrHash.Compute(layouts[i].Kernel), WarpIrHash.Compute(preparedLeases[i].Layout.Kernel), StringComparison.Ordinal))
                { throw new InvalidOperationException("A prepared source role changed its exact admitted semantic IR."); }
            }
            var modules = new WarpCompiledPrewarmedSourceModules(context, preparedKernels.ToArray(), preparedLeases.ToArray());
            startup.Finish(modules);
            return modules;
        }
        catch (Exception failure)
        {
            startup.Fail();
            try { await DisposeModulesAsync(preparedKernels, preparedLeases).ConfigureAwait(false); }
            catch (Exception cleanupFailure) { throw new AggregateException(failure, cleanupFailure); }
            throw;
        }
    }

    internal void Validate(WarpCompiledSourceContext exactContext)
    {
        if (!ReferenceEquals(context, exactContext) || Volatile.Read(ref disposal) is not null ||
            leases.Any(lease => lease.IsFaulted || lease.IsReleased))
        { throw new InvalidOperationException("The source role bank is stale or belongs to another exact runtime context."); }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is null)
            {
                context.SuppressUnresolvedRemoteFailure();
                disposal = DisposeModulesAsync(kernels, leases);
            }
            return new(disposal);
        }
    }

    private static async Task DisposeModulesAsync(IEnumerable<WarpCoreCLRWorkerKernel> kernels, IEnumerable<WarpCoreCLRWorkerLease> leases)
    {
        Task[] stops = kernels.Select(kernel => kernel.DisposeAsync().AsTask()).ToArray();
        Task[] releases = leases.Select(lease => lease.DisposeAsync().AsTask()).ToArray();
        Task completion = Task.WhenAll(stops.Concat(releases));
        try { await completion.ConfigureAwait(false); }
        catch (Exception) when (completion.Exception is { } failures)
        {
            // A child's stop and final lease release share the same failure.
            // Await selects one fault, so preserve every distinct original object.
            Exception[] distinct = failures.Flatten().InnerExceptions.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            if (distinct.Length == 1) { ExceptionDispatchInfo.Capture(distinct[0]).Throw(); }
            throw new AggregateException(distinct);
        }
    }
}
