using WarpCLR.Compiler;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourceContext
{
    internal void StopRemoteControl(WarpCompiledPausedCensus census, WarpCoreCLRQuarantineRecovery recovery,
        WarpCoreCLRControllerAdmission admission, IReadOnlyList<WarpCoreCLRCommandAdmission> admissions)
    {
        lock (executionIdentityGate)
        {
            recovery.ValidateArena(Arena);
            if (!ReferenceEquals(remoteCensus, census) || !ReferenceEquals(census.Context, this) || census.Grant is null ||
                census.Grant.Token != admission.Preparation.Controller || !ReferenceEquals(admission.Preparation.Arena, Arena) ||
                !ReferenceEquals(recovery.Census.Controller, admission) || recovery.Census.Count != Plan.Workers + 1 ||
                admissions.Count != Plan.Workers || admissions.Any(item => !recovery.Census.Admissions.Any(actual => ReferenceEquals(actual, item))))
            { throw new InvalidOperationException("Stopped controller recovery differs from the exact paused context and independent registered census."); }
            for (uint worker = 0; worker < Plan.Workers; worker++)
            {
                if (!ReferenceEquals(admissions[checked((int)worker)].State, helpers[worker]?.State ?? states[worker]) || tickets[worker]?.Executing == true)
                { throw new InvalidOperationException("A stopped controller omitted or changed an actual paused source bank."); }
            }
            census.Consume();
            Volatile.Write(ref stopped, 1);
            for (uint worker = 0; worker < Plan.Workers; worker++)
            {
                faults[worker] = new(Plan.Identity, Identity, Dispatch, worker, tickets[worker]?.Generation ?? 0, 5,
                    Plan.Location(states[worker]), Plan.SourceFrames(states[worker]));
            }
        }
    }

    internal void FinishRemoteControllerDisposal(WarpCompiledPausedCensus census, WarpCoreCLRControllerAdmission admission)
    {
        lock (executionIdentityGate)
        {
            if (!ReferenceEquals(remoteCensus, census) || census.Grant is null || stopped == 0 ||
                !ReferenceEquals(admission.Preparation.Arena, Arena) || admission.Preparation.Controller != census.Grant.Token ||
                State != WarpPortableSchedulerLayout.DisposedContext || Header(WarpPortableSchedulerLayout.OutputQuarantined) != 1 ||
                Header(WarpPortableSchedulerLayout.ControllerOwner) != 0)
            { throw new InvalidOperationException("Generated controller teardown did not confirm the exact terminal disposal and captured-owner release."); }
            controller.AcknowledgeRemoteRelease(census.Grant);
            ClearDisposedContinuations();
            remoteCensus = null;
        }
    }
}
