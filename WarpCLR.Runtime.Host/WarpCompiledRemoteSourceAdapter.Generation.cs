using System.Collections.Immutable;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledRemoteSourceAdapter
{
    internal Task<uint> RequestCollectionAsync(WarpCoreCLRWorkerLease lease) =>
        ExecuteGenerationAsync(lease, nameof(WarpPortableSchedulerServices.RequestCollection), null);

    internal Task<uint> BeginDispatchAsync(WarpCoreCLRWorkerLease lease, uint[][] arguments)
    {
        uint[][] nextInputs = context.ValidateRedispatchInputs(arguments);
        return context.State == WarpPortableSchedulerLayout.CompletedContext
            ? ExecuteGenerationAsync(lease, nameof(WarpPortableSchedulerServices.BeginDispatch), nextInputs)
            : Task.FromResult(WarpPortableSchedulerLayout.Invalid);
    }

    private async Task<uint> ExecuteGenerationAsync(WarpCoreCLRWorkerLease lease, string service, uint[][]? nextInputs)
    {
        RequireHash(lease, context.Plan.Services.Scheduler(service).KernelHash);
        if (publicationRelease is null || disposeController is null)
        { throw new InvalidOperationException("Controller mutation requires independently prepared normal and emergency release modules and every stopped transition variant."); }
        WarpCompiledControllerGrant? grant = context.Controller.TryAcquire();
        if (grant is null) { return WarpPortableSchedulerLayout.Yield; }
        WarpCompiledPausedCensus? census = null;
        WarpCoreCLRControllerAdmission? admission = null;
        try
        {
            census = context.PrepareRemoteControl(grant);
            uint[][]? nextStates = nextInputs is null ? null : context.PrepareRemoteRedispatchStates(census);
            ImmutableArray<WarpCoreCLRCommandAdmission> prior = await RegisterGenerationCensusAsync(census).ConfigureAwait(false);
            (uint dispatch, uint epoch) = ExpectedGeneration(service, census);
            ImmutableArray<WarpCoreCLRPreparedCleanup> cleanup = PrepareControlCleanup(grant.Token, dispatch, epoch);
            uint[] state = lease.Layout.CreateInitialState(32, 100_000_000);
            WarpCoreCLRControllerOperation operation = nextInputs is null ? WarpCoreCLRControllerOperation.RequestCollection : WarpCoreCLRControllerOperation.BeginDispatch;
            var preparation = new WarpCoreCLRControllerPreparation(operation, lease, state, context.Arena, 32,
                context.SchedulerHash, context.Plan.Identity, context.Scheduler, grant.Token, PrepareControllerCleanup(census, operation, dispatch, epoch));
            admission = await WarpCoreCLRStoppedCommands.RegisterPreparedControllerAsync(preparation).ConfigureAwait(false);
            uint result = await RunGenerationAsync(admission).ConfigureAwait(false);
            if (result == 0)
            {
                WarpCoreCLRGenerationTransition receipt = WarpCoreCLRStoppedCommands.GetCommittedGenerationTransition(state);
                if (receipt.ToDispatch != dispatch || receipt.ToCollection != epoch)
                { throw new InvalidOperationException("The committed generation differs from its exact preprepared cleanup operands."); }
                ImmutableArray<WarpCoreCLRCommandAdmission> next = GenerationSuccessors(census, nextStates, cleanup);
                await WarpCoreCLRStoppedCommands.ApplyCommittedGenerationTransitionAsync(receipt, prior, next, CancellationToken.None).ConfigureAwait(false);
                for (int worker = 0; worker < next.Length; worker++) { registered[worker] = next[worker]; }
                context.CommitRemoteControl(census, nextInputs, nextStates);
            }
            await WarpCoreCLRStoppedCommands.ReleaseOwnedControllerAsync(admission).ConfigureAwait(false);
            context.FinishRemoteControl(census);
            return result;
        }
        catch (WarpHostException failure) when (admission is not null && census is not null)
        {
            await RecoverControllerFailureAsync(failure, census, admission).ConfigureAwait(false);
            throw;
        }
        catch
        {
            if (census is null) { context.Controller.Release(grant); }
            else { context.SuppressUnresolvedRemoteFailure(); }
            throw;
        }
    }

    private static async Task<uint> RunGenerationAsync(WarpCoreCLRControllerAdmission admission)
    {
        WarpCoreCLRWorkerLease lease = admission.Preparation.Lease;
        uint[] state = admission.Preparation.State;
        for (int attempt = 0; attempt < 1_000_000; attempt++)
        {
            await lease.ExecuteOwnedControllerQuantumAsync(admission,
                Math.Max(4096, lease.Layout.MaximumBlockCost), CancellationToken.None).ConfigureAwait(false);
            if (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Completed)
            { return state[WarpLogicalMachineLayout.ResultOffset]; }
        }
        throw new InvalidOperationException("An owned generation service exceeded its finite operational quota.");
    }

    private async Task<ImmutableArray<WarpCoreCLRCommandAdmission>> RegisterGenerationCensusAsync(WarpCompiledPausedCensus census)
    {
        ImmutableArray<WarpCoreCLRPreparedCleanup> cleanup = PrepareControlCleanup(census.Grant!.Token, census.Dispatch, census.Epoch);
        ImmutableArray<WarpCoreCLRCommandAdmission> admissions = GenerationSuccessors(census, null, cleanup);
        for (uint worker = 0; worker < context.Plan.Workers; worker++)
        { await RegisterContinuationAsync(worker, admissions[checked((int)worker)]).ConfigureAwait(false); }
        return admissions;
    }

    private ImmutableArray<WarpCoreCLRCommandAdmission> GenerationSuccessors(WarpCompiledPausedCensus census, uint[][]? nextStates,
        ImmutableArray<WarpCoreCLRPreparedCleanup> cleanup)
    {
        var admissions = ImmutableArray.CreateBuilder<WarpCoreCLRCommandAdmission>(census.Workers.Length);
        foreach (WarpCompiledPausedWorker worker in census.Workers)
        {
            uint[] state = nextStates?[worker.Worker] ?? worker.Helper?.State ?? worker.SourceState;
            string ir = worker.Helper?.Service.KernelHash ?? WarpIrHash.Compute(context.Plan.Layout.Kernel);
            object identity = worker.Ticket ?? pausedIdentities[worker.Worker];
            admissions.Add(new(identity, state, context.Arena, context.SchedulerHash, context.Plan.Identity, ir,
                context.Dispatch, context.Epoch, census.Grant!.Token, cleanup));
        }
        return admissions.MoveToImmutable();
    }

    private (uint Dispatch, uint Epoch) ExpectedGeneration(string service, WarpCompiledPausedCensus census)
    {
        if (string.Equals(service, nameof(WarpPortableSchedulerServices.BeginDispatch), StringComparison.Ordinal))
        { return (census.Dispatch == uint.MaxValue ? census.Dispatch : census.Dispatch + 1, census.Epoch); }
        bool requested = context.Arena[context.Scheduler + WarpPortableSchedulerLayout.GCState] == WarpPortableSchedulerLayout.GCRequested;
        return (census.Dispatch, requested || census.Epoch == uint.MaxValue ? census.Epoch : census.Epoch + 1);
    }

    private ImmutableArray<WarpCoreCLRPreparedCleanup> PrepareControlCleanup(uint token, uint dispatch, uint epoch)
    {
        uint word = checked(context.Scheduler + WarpPortableSchedulerLayout.ControllerOwner);
        return [
            new(disposeCensus, WarpCoreCLRCleanupPurpose.StoppedFault, 0,
                Banks([context.Scheduler, token, dispatch, epoch, uint.MaxValue, 0, 0, 0, 0, 0]), []),
            new(compareExchange, WarpCoreCLRCleanupPurpose.ReleaseCapturedController, token, Banks([word, token, 0]), []),
        ];
    }
}
