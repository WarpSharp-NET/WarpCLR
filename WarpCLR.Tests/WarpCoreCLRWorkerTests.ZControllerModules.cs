using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    private sealed class ControllerModules(TestContext context) : IAsyncDisposable
    {
        private readonly List<WarpCoreCLRWorkerKernel> kernels = [];
        private readonly List<WarpCoreCLRWorkerLease> leases = [];
        private readonly List<(string Role, TimeSpan Quota)> children = [];
        private readonly ConcurrentQueue<CleanupObservation> observations = new();
        private ExceptionDispatchInfo? bodyFailure;
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

        internal static async Task<ControllerModules> CreateAsync(WarpCoreCLRControllerOperation operation, TestContext context)
        {
            var result = new ControllerModules(context);
            try
            {
                string method = operation == WarpCoreCLRControllerOperation.RequestCollection ? nameof(WarpPortableSchedulerServices.RequestCollection) : nameof(WarpPortableSchedulerServices.BeginDispatch);
                result.Service = await result.CompileAsync(ServiceLayout(method), nameof(Service), new()
                { BeforeControllerResultPublication = result.ObserveService }).ConfigureAwait(false);
                WarpLogicalMachineLayout cas = WarpManagedAtomicKernels.Create32()[2];
                result.Emergency = await result.CompileAsync(cas, nameof(Emergency)).ConfigureAwait(false);
                result.Publication = await result.CompileAsync(cas, nameof(Publication), new()
                { BeforeControllerResultPublication = result.ObserveRelease }).ConfigureAwait(false);
                result.Stopped = await result.CompileAsync(ServiceLayout(nameof(WarpPortableSchedulerServices.DisposeStoppedController)), nameof(Stopped)).ConfigureAwait(false);
                result.Census = await result.CompileAsync(ServiceLayout(nameof(WarpPortableSchedulerServices.DisposeStoppedCensus)), nameof(Census)).ConfigureAwait(false);
                WarpControlFlowKernel source = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(typeof(TestKernels).GetMethod(nameof(TestKernels.Branch))!, 1)).ControlFlow;
                result.Source = await result.CompileAsync(new(source), nameof(Source)).ConfigureAwait(false);
                return result;
            }
            catch (Exception failure)
            {
                await result.DrainAsync(ExceptionDispatchInfo.Capture(failure)).ConfigureAwait(false);
                throw;
            }
        }

        private static WarpLogicalMachineLayout ServiceLayout(string method) => WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableSchedulerServices).GetMethod(method)!);

        private async Task<WarpCoreCLRWorkerLease> CompileAsync(WarpLogicalMachineLayout layout, string role, WarpCoreCLRWorkerTestHooks? probes = null)
        {
            var options = new WarpCoreCLRWorkerOptions();
            WarpCoreCLRWorkerTestHooks observed = (probes ?? new()) with
            {
                Started = process => Record(role, options.CleanupTimeout, process, "started", "observed"),
                Killed = identity => Record(role, options.CleanupTimeout, identity.ChildProcess, "killed", "observed", identity),
                CleanupMilestone = row => observations.Enqueue(new(role, row.ProcessId, row.Timestamp, row.ThreadId,
                    row.ThreadPool, options.CleanupTimeout.Ticks, "cleanup-milestone", row.Milestone, row)),
            };
            Record(role, options.CleanupTimeout, Environment.ProcessId, "compile-parent", "begin");
            WarpCoreCLRWorkerKernel kernel;
            try
            {
                kernel = await WarpCoreCLRWorkerKernel.CompileAsync(layout, options, CancellationToken.None, observed).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                Record(role, options.CleanupTimeout, Environment.ProcessId, "compile-parent", "failed", failure);
                throw;
            }
            Record(role, options.CleanupTimeout, kernel.ProcessId, "compile-child", "returned", kernel.CompiledModule);
            kernels.Add(kernel);
            children.Add((role, options.CleanupTimeout));
            WarpCoreCLRWorkerLease lease = kernel.TryAcquireLease()!; leases.Add(lease); return lease;
        }

        // These observers only append existing hook data. TestContext output happens after draining;
        // no assertion, wait, continuation, or deliberate publication-failure callback is added here.
        private void Record(string role, TimeSpan quota, int process, string stage, string outcome, object? detail = null) =>
            observations.Enqueue(new(role, process, Stopwatch.GetTimestamp(), Environment.CurrentManagedThreadId,
                Thread.CurrentThread.IsThreadPoolThread, quota.Ticks, stage, outcome, detail));

        private sealed record CleanupObservation(string Role, int ProcessId, long Timestamp, int ThreadId,
            bool ThreadPool, long QuotaTicks, string Stage, string Outcome, object? Detail);

        internal void FailNextServiceResponse() => Volatile.Write(ref failService, 1);
        internal void FailReturnedGeneration() => Volatile.Write(ref failService, 2);
        internal void FailNextReleaseResponse() => Volatile.Write(ref failRelease, 1);

        internal void RecordReturnedServiceFailure(WarpHostException failure) =>
            Record(nameof(Service), children[0].Quota, Service.ProcessId, "controller-response", "failed", failure);

        internal void RecordBodyFailure(Exception failure)
        {
            bodyFailure ??= ExceptionDispatchInfo.Capture(failure);
            // This is the actual parent test body, not a worker cleanup attempt or a new quota.
            Record("RecoveryBody", TimeSpan.Zero, Environment.ProcessId, "test-body", "failed", failure);
        }

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

        public ValueTask DisposeAsync() => DrainAsync(bodyFailure);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "The test must attempt every actual lease and kernel disposal and report all later failures before rethrowing the exact first exception object; a narrow catch or immediate rethrow would strand remaining real children or mask the original body failure.")]
        private async ValueTask DrainAsync(ExceptionDispatchInfo? firstFailure)
        {
            for (int index = leases.Count - 1; index >= 0; index--)
            {
                (string role, TimeSpan quota) = children[index];
                Record(role, quota, leases[index].ProcessId, "lease-dispose", "begin");
                try
                {
                    await leases[index].DisposeAsync().ConfigureAwait(false);
                    Record(role, quota, leases[index].ProcessId, "lease-dispose", "succeeded");
                }
                catch (Exception failure)
                {
                    firstFailure ??= ExceptionDispatchInfo.Capture(failure);
                    Record(role, quota, leases[index].ProcessId, "lease-dispose", "failed", failure);
                }
            }
            for (int index = kernels.Count - 1; index >= 0; index--)
            {
                (string role, TimeSpan quota) = children[index];
                Record(role, quota, kernels[index].ProcessId, "kernel-dispose", "begin");
                try
                {
                    await kernels[index].DisposeAsync().ConfigureAwait(false);
                    Record(role, quota, kernels[index].ProcessId, "kernel-dispose", "succeeded");
                }
                catch (Exception failure)
                {
                    firstFailure ??= ExceptionDispatchInfo.Capture(failure);
                    Record(role, quota, kernels[index].ProcessId, "kernel-dispose", "failed", failure);
                }
            }
            try { WriteEvidence(firstFailure); }
            catch (Exception failure) { firstFailure ??= ExceptionDispatchInfo.Capture(failure); }
            firstFailure?.Throw();
        }

        private void WriteEvidence(ExceptionDispatchInfo? firstFailure)
        {
            context.WriteLine(FormattableString.Invariant($"ControllerModules cleanup snapshot: frequency={Stopwatch.Frequency}, children={kernels.Count}, leases={leases.Count}, firstFailure={(firstFailure is null ? "none" : "retained-original-object")}."));
            foreach (CleanupObservation row in observations.ToArray())
            {
                bool originalFailure = row.Detail is Exception failure && ReferenceEquals(failure, firstFailure?.SourceException);
                string? code = (row.Detail as WarpHostException)?.Code;
                context.WriteLine(FormattableString.Invariant($"ControllerModules role={row.Role}, pid={row.ProcessId}, timestamp={row.Timestamp}, thread={row.ThreadId}, threadPool={row.ThreadPool}, quotaTicks={row.QuotaTicks}, stage={row.Stage}, outcome={row.Outcome}, firstFailureObject={originalFailure}, code={code}, detail={row.Detail}."));
            }
            if (firstFailure is not null) { context.WriteLine(firstFailure.SourceException.ToString()); }
        }
    }
}
