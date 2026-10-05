using System.Collections.Immutable;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

// Every lease is already compiled and held before a source command is admitted.
// This adapter neither compiles source nor substitutes local execution after failure.
internal sealed partial class WarpCompiledRemoteSourceAdapter
{
    internal const string Semantics = "warp.runtime-host.authenticated-source-controller-cleanup/0.2";
    private readonly WarpCompiledSourceContext context;
    private readonly WarpCoreCLRWorkerLease source;
    private readonly WarpCoreCLRWorkerLease compareExchange;
    private readonly WarpCoreCLRWorkerLease disposeCensus;
    private readonly WarpCoreCLRWorkerLease? publicationRelease;
    private readonly WarpCoreCLRWorkerLease? disposeController;
    private readonly object[] pausedIdentities;
    private readonly WarpCoreCLRCommandAdmission?[] registered;

    internal WarpCompiledRemoteSourceAdapter(WarpCompiledSourceContext context, WarpCoreCLRWorkerLease source,
        WarpCoreCLRWorkerLease compareExchange, WarpCoreCLRWorkerLease disposeCensus,
        WarpCoreCLRWorkerLease? publicationRelease = null, WarpCoreCLRWorkerLease? disposeController = null)
    {
        this.context = context;
        this.source = source;
        this.compareExchange = compareExchange;
        this.disposeCensus = disposeCensus;
        this.publicationRelease = publicationRelease;
        this.disposeController = disposeController;
        RequireHash(source, WarpIrHash.Compute(context.Plan.Layout.Kernel));
        RequireHash(compareExchange, context.Controller.CleanupKernelHash);
        RequireHash(disposeCensus, context.Plan.Services.CleanupKernels[nameof(WarpPortableSchedulerServices.DisposeStoppedCensus)]);
        if (publicationRelease is not null) { RequireHash(publicationRelease, context.Controller.CleanupKernelHash); }
        if (disposeController is not null) { RequireHash(disposeController, WarpCoreCLRRecoveryCatalog.DisposeStoppedController); }
        pausedIdentities = Enumerable.Range(0, checked((int)context.Plan.Workers)).Select(_ => new object()).ToArray();
        registered = new WarpCoreCLRCommandAdmission?[context.Plan.Workers];
    }

    internal async Task<WarpCompiledRemotePreparedRun> PrepareAsync(WarpCompiledWorkerTicket ticket, WarpCompiledControllerGrant? capturedGrant = null)
    {
        WarpCompiledPausedCensus census = context.PrepareRemoteSource(ticket, capturedGrant);
        try
        {
            uint token = capturedGrant?.Token ?? context.Controller.ReserveToken();
            if (token == 0) { throw new InvalidOperationException("Remote cleanup token admission yields without starting source."); }
            ImmutableArray<WarpCoreCLRPreparedCleanup> cleanup = PrepareCleanup(census, ticket, token);
            var admissions = ImmutableArray.CreateBuilder<WarpCoreCLRCommandAdmission>(census.Workers.Length);
            WarpCoreCLRCommandAdmission? active = null;
            foreach (WarpCompiledPausedWorker worker in census.Workers)
            {
                uint[] state = worker.Helper?.State ?? worker.SourceState;
                string ir = worker.Helper?.Service.KernelHash ?? WarpIrHash.Compute(context.Plan.Layout.Kernel);
                object identity = worker.Ticket ?? pausedIdentities[worker.Worker];
                var admission = new WarpCoreCLRCommandAdmission(identity, state, context.Arena, context.SchedulerHash, context.Plan.Identity,
                    ir, census.Dispatch, census.Epoch, capturedGrant?.Token ?? 0, cleanup);
                await RegisterContinuationAsync(worker.Worker, admission).ConfigureAwait(false);
                admissions.Add(admission);
                if (worker.Worker == ticket.Worker) { active = admission; }
            }
            return new(census, ticket, active!, admissions.MoveToImmutable(), cleanup);
        }
        catch { context.SuppressUnresolvedRemoteFailure(); throw; }
    }

    internal async Task<WarpCoreCLRQuarantineRecovery?> ExecuteAsync(WarpCompiledRemotePreparedRun run)
    {
        run.Begin();
        context.BeginRemoteSource(run.Census, run.Ticket);
        try
        {
            await source.ExecuteOwnedManagedQuantumAsync(context.RemoteSourceInputs, [], checked((int)run.Ticket.Worker),
                context.RemoteSourceState(run.Ticket), context.Plan.MaximumDepth, context.Plan.Quantum, context.Arena,
                run.Admission, CancellationToken.None).ConfigureAwait(false);
            context.FinishRemoteSource(run.Census, run.Ticket);
            return null;
        }
        catch (WarpHostException failure)
        {
            WarpCoreCLRStoppedCommands.Command? command = WarpCoreCLRStoppedCommands.FromFailure(failure);
            if (command is null) { context.SuppressUnresolvedRemoteFailure(); throw; }
            WarpCoreCLRQuarantineRecovery recovery = WarpCoreCLRStoppedCommands.Mint(command, run.Ticket);
            context.StopRemoteSource(run.Census, run.Ticket, recovery, run.Admissions);
            foreach (WarpCoreCLRPreparedCleanup binding in run.Cleanup)
            { await ExecuteCleanupAsync(binding, run, recovery).ConfigureAwait(false); }
            await recovery.AcknowledgeGeneratedAbortAsync(run.Ticket, CancellationToken.None).ConfigureAwait(false);
            context.FinishRemoteDisposal(run.Census);
            return recovery;
        }
        catch { context.SuppressUnresolvedRemoteFailure(); throw; }
    }

    private static void RequireHash(WarpCoreCLRWorkerLease lease, string expected)
    {
        if (lease.IsFaulted || !string.Equals(WarpIrHash.Compute(lease.Layout.Kernel), expected, StringComparison.Ordinal))
        { throw new InvalidOperationException("A remote runtime lease differs from its pre-admitted exact compiled IR."); }
    }

    private async Task RegisterContinuationAsync(uint worker, WarpCoreCLRCommandAdmission admission)
    {
        if (registered[worker] is { } prior)
        {
            if (ReferenceEquals(prior.ExactTicket, admission.ExactTicket))
            {
                await WarpCoreCLRStoppedCommands.ReplacePausedAdmissionAsync(prior, admission, CancellationToken.None).ConfigureAwait(false);
                registered[worker] = admission;
                return;
            }
            await WarpCoreCLRStoppedCommands.RetirePausedAdmissionAsync(prior, CancellationToken.None).ConfigureAwait(false);
        }
        await WarpCoreCLRStoppedCommands.RegisterPausedAdmissionAsync(admission, CancellationToken.None).ConfigureAwait(false);
        registered[worker] = admission;
    }
}
