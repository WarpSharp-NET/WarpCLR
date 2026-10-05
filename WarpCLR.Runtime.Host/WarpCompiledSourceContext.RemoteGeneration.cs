using WarpCLR.Compiler;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourceContext
{
    internal WarpCompiledPausedCensus PrepareRemoteControl(WarpCompiledControllerGrant grant)
    {
        lock (executionIdentityGate)
        {
            if (stopped != 0 || remoteCensus is not null)
            { throw new InvalidOperationException("The context cannot admit another owned controller command."); }
            WarpCompiledPausedCensus census = SnapshotPausedCensus(grant);
            controller.SuspendForRemote(grant, true);
            remoteCensus = census;
            return census;
        }
    }

    internal uint[][] ValidateRedispatchInputs(uint[][] arguments) => ValidateArguments(arguments);

    internal uint[][] PrepareRemoteRedispatchStates(WarpCompiledPausedCensus census)
    {
        lock (executionIdentityGate)
        {
            RequireRemoteCensus(census);
            if (State != WarpPortableSchedulerLayout.CompletedContext || census.Workers.Any(worker => worker.Ticket is not null || worker.Helper is not null))
            { throw new InvalidOperationException("Remote redispatch requires completed source continuations."); }
            return CreateDispatchSources();
        }
    }

    // Called only after the worker authenticates and applies its complete generation receipt.
    // The adapter never uses this local reset as authority to update the remote registry.
    internal void CommitRemoteControl(WarpCompiledPausedCensus census, uint[][]? nextInputs, uint[][]? nextStates)
    {
        lock (executionIdentityGate)
        {
            if (!ReferenceEquals(remoteCensus, census) || census.Grant is null || stopped != 0)
            { throw new InvalidOperationException("The exact remote controller command is no longer owned."); }
            if (nextInputs is not null)
            {
                if (nextStates is null) { throw new InvalidOperationException("Redispatch has no admitted successor source banks."); }
                ResetDispatchSources(nextInputs, nextStates);
            }
        }
    }

    internal void FinishRemoteControl(WarpCompiledPausedCensus census)
    {
        lock (executionIdentityGate)
        {
            if (!ReferenceEquals(remoteCensus, census) || census.Grant is null || stopped != 0)
            { throw new InvalidOperationException("The exact remote controller command is no longer owned."); }
            controller.AcknowledgeRemoteRelease(census.Grant);
            controller.ResumeAfterRemoteCommit(null);
            remoteCensus = null;
        }
    }
}
