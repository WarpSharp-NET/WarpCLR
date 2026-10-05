using System.Security.Cryptography;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerProcess
{
    private static readonly Lock RegistrySync = new();
    private static readonly object RegistryAuthority = new();
    private static readonly Dictionary<ulong, WarpCoreCLRStoppedCommands.Command> RegistryCommands = [];
    private static ulong nextCommandOrdinal;
    private static WarpCoreCLRStoppedCommands.Command Started(WarpCoreCLRWorkerProcess process, ulong sequence, byte[] request,
        uint[] state, uint[] arena, string irHash, WarpCoreCLRCommandAdmission? admission, uint[][] inputs, uint[] scalars,
        WarpCoreCLRControllerAdmission? controller, bool controllerRelease, WarpCoreCLRQuarantineRecovery? recovery)
    {
        admission?.Validate(irHash, state, arena);
        lock (RegistrySync)
        {
            if (nextCommandOrdinal == ulong.MaxValue || RegistryCommands.Count >= 4096)
            { throw new WarpHostException("WRPCORECLR3004", "The stopped-command ownership registry is exhausted."); }
            if (controller is null && recovery is null) { ValidateOrdinaryStorageCore([state], arena); }
            if (admission is not null) { RegisterAdmissionCore(admission); }
            var command = new WarpCoreCLRStoppedCommands.Command(++nextCommandOrdinal, process, sequence, SHA256.HashData(request), state, arena, irHash, admission,
                controller, controllerRelease);
            if (controller is not null) { ValidateControllerCommand(command, inputs, scalars); }
            BeginGenerationCore(command);
            RegistryCommands.Add(command.Ordinal, command);
            WordCheckpoints.Remove(state);
            return command;
        }
    }

    private static void Committed(WarpCoreCLRStoppedCommands.Command command, WarpCoreCLRGenerationTransition? transition,
        WarpCoreCLRControllerCheckpoint? checkpoint)
    {
        lock (RegistrySync)
        {
            RegistryCommands.Remove(command.Ordinal);
            if (checkpoint is not null) { command.Controller!.PublishCheckpoint(checkpoint, RegistryAuthority); }
            if (command.ControllerRelease) { CommitControllerRelease(command); }
            if (transition is not null)
            {
                ArenaDomain domain = Domains.GetValue(command.Arena, static _ => new ArenaDomain());
                domain.PendingTransition = transition;
                GenerationReceipts.Remove(command.State); GenerationReceipts.Add(command.State, transition);
                command.Controller!.PublishReceipt(transition, RegistryAuthority);
            }
        }
    }

    private static void ConfirmStopped(WarpCoreCLRStoppedCommands.Command command)
    {
        lock (RegistrySync)
        {
            RequireCurrent(command);
            if (!command.Process.HasSuccessfulContainment) { throw new InvalidOperationException("Successful first-attempt child containment has not been confirmed."); }
            command.MarkStopped(RegistryAuthority);
            if (command.Admission is null && command.Controller is null) { RegistryCommands.Remove(command.Ordinal); }
        }
    }

    internal static WarpCoreCLRQuarantineRecovery MintStoppedRecovery(WarpCoreCLRStoppedCommands.Command command, object exactTicket)
    {
        lock (RegistrySync)
        {
            RequireCurrent(command);
            object? admittedTicket = command.Controller is { } controller ? controller : command.Admission?.ExactTicket;
            if (!command.Process.HasSuccessfulContainment || command.Minted || admittedTicket is null || !ReferenceEquals(admittedTicket, exactTicket))
            { throw new InvalidOperationException("Only the exact registered stopped ticket can mint one cleanup capability."); }
            foreach (WarpCoreCLRStoppedCommands.Command current in RegistryCommands.Values)
            { if (ReferenceEquals(current.Arena, command.Arena) && current.Process.HasSuccessfulContainment) { current.MarkStopped(RegistryAuthority); } }
            command.Controller?.ValidateStorage();
            WarpCoreCLRStoppedCensus census = CaptureCensus(command);
            var recovery = new WarpCoreCLRQuarantineRecovery(command, census, RegistryAuthority);
            command.PublishRecovery(recovery, RegistryAuthority);
            return recovery;
        }
    }

    internal static void ValidateRecovery(WarpCoreCLRStoppedCommands.Command command, WarpCoreCLRQuarantineRecovery recovery)
    {
        lock (RegistrySync)
        {
            RequireCurrent(command);
            if (!command.Stopped || !command.Process.HasSuccessfulContainment || !command.Minted || !ReferenceEquals(command.Recovery, recovery))
            { throw new InvalidOperationException("Cleanup session is not the runtime-minted exact stopped command."); }
        }
    }

    internal static void CompleteStoppedRecovery(WarpCoreCLRStoppedCommands.Command command)
    {
        lock (RegistrySync) { RequireCurrent(command); RegistryCommands.Remove(command.Ordinal); }
    }

    private static void RetireClosedUnownedCommands(WarpCoreCLRWorkerProcess process)
    {
        lock (RegistrySync)
        {
            foreach (ulong ordinal in RegistryCommands.Values.Where(command => ReferenceEquals(command.Process, process) &&
                command.Admission is null && command.Controller is null).Select(command => command.Ordinal).ToArray()) { RegistryCommands.Remove(ordinal); }
        }
    }

    private static void RequireCurrent(WarpCoreCLRStoppedCommands.Command command)
    {
        if (!RegistryCommands.TryGetValue(command.Ordinal, out WarpCoreCLRStoppedCommands.Command? actual) || !ReferenceEquals(actual, command))
        { throw new InvalidOperationException("Stopped command is not authenticated by this runtime registry."); }
    }

    internal static void ValidateRegistryAuthority(object candidate)
    {
        if (!ReferenceEquals(candidate, RegistryAuthority)) { throw new InvalidOperationException("The private native registry authority is required."); }
    }

    internal static Task ReleaseRecoveredControllerLeasesAsync(WarpCoreCLRControllerAdmission admission) => admission.ReleaseLeasesAsync(RegistryAuthority);

}
