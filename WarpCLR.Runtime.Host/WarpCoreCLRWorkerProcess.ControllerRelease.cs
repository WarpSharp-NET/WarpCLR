using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerProcess
{
    internal static async Task ReleaseOwnedControllerAsync(WarpCoreCLRControllerAdmission admission, CancellationToken cancellationToken)
    {
        uint word = checked(admission.Preparation.Scheduler + WarpPortableSchedulerLayout.ControllerOwner);
        await admission.PublicationRelease.Lease.ExecuteControllerReleaseAsync(admission, [[word], [admission.Preparation.Controller], [0]],
            cancellationToken).ConfigureAwait(false);
        await admission.ReleaseLeasesAsync(RegistryAuthority).ConfigureAwait(false);
    }

    internal static void ValidateControllerRecoveryTerminal(WarpCoreCLRControllerAdmission admission)
    {
        uint[] arena = admission.Preparation.Arena;
        uint scheduler = admission.Preparation.Scheduler;
        if (arena[scheduler + WarpPortableSchedulerLayout.ControllerOwner] != 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.DisposedContext ||
            arena[scheduler + WarpPortableSchedulerLayout.OutputQuarantined] != 1 ||
            arena[scheduler + WarpPortableSchedulerLayout.RunningCount] != 0)
        { throw new InvalidOperationException("The stopped controller teardown did not commit terminal disposal and captured-owner release."); }
    }

    private static void ValidateControllerRelease(WarpCoreCLRStoppedCommands.Command command, uint[] state, uint[] arena)
    {
        WarpCoreCLRControllerAdmission admission = command.Controller!;
        uint word = checked(admission.Preparation.Scheduler + WarpPortableSchedulerLayout.ControllerOwner);
        if (state[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Completed ||
            state[WarpLogicalMachineLayout.ResultOffset] != admission.Preparation.Controller || arena[word] != 0)
        { throw new InvalidDataException("The native controller release did not confirm its exact captured-owner CAS."); }
        for (int index = 0; index < arena.Length; index++)
        {
            if ((uint)index != word && arena[index] != command.Arena[index])
            { throw new InvalidDataException("Captured-owner release changed unrelated arena storage."); }
        }
    }

    private static void CommitControllerRelease(WarpCoreCLRStoppedCommands.Command command)
    {
        WarpCoreCLRControllerAdmission admission = command.Controller!;
        ArenaDomain domain = Domains.GetValue(command.Arena, static _ => new ArenaDomain());
        admission.MarkReleaseCompleted(RegistryAuthority); domain.Controller = null; StateDomains.Remove(admission.Preparation.State);
    }
}
