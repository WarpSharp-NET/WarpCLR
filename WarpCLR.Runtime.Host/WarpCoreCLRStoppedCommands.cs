using System.Runtime.CompilerServices;

namespace WarpCLR.Runtime.Host;

internal static class WarpCoreCLRStoppedCommands
{
    private static readonly ConditionalWeakTable<Exception, Command> Failures = new();
    internal static void AttachFailure(Exception failure, Command command) => Failures.Add(failure, command);
    internal static Task RegisterPausedAdmissionAsync(WarpCoreCLRCommandAdmission admission, CancellationToken cancellationToken = default) =>
        WarpCoreCLRWorkerProcess.RegisterPausedAdmissionAsync(admission, cancellationToken);
    internal static Task ReplacePausedAdmissionAsync(WarpCoreCLRCommandAdmission predecessor, WarpCoreCLRCommandAdmission successor,
        CancellationToken cancellationToken = default) => WarpCoreCLRWorkerProcess.ReplacePausedAdmissionAsync(predecessor, successor, cancellationToken);
    internal static Task RetirePausedAdmissionAsync(WarpCoreCLRCommandAdmission predecessor, CancellationToken cancellationToken = default) =>
        WarpCoreCLRWorkerProcess.RetirePausedAdmissionAsync(predecessor, cancellationToken);
    internal static WarpCoreCLRGenerationTransition GetCommittedGenerationTransition(uint[] exactState) =>
        WarpCoreCLRWorkerProcess.GetCommittedGenerationTransition(exactState);
    internal static Task ApplyCommittedGenerationTransitionAsync(WarpCoreCLRGenerationTransition receipt,
        IReadOnlyList<WarpCoreCLRCommandAdmission> predecessors, IReadOnlyList<WarpCoreCLRCommandAdmission> successors,
        CancellationToken cancellationToken = default) =>
        WarpCoreCLRWorkerProcess.ApplyCommittedGenerationTransitionAsync(receipt, predecessors, successors, cancellationToken);
    internal static Command? FromFailure(Exception failure) => Failures.TryGetValue(failure, out Command? command) ? command : null;
    internal static Task<WarpCoreCLRControllerAdmission> RegisterPreparedControllerAsync(WarpCoreCLRControllerPreparation preparation,
        CancellationToken cancellationToken = default) => WarpCoreCLRWorkerProcess.RegisterPreparedControllerAsync(preparation, cancellationToken);
    internal static Task ReleaseOwnedControllerAsync(WarpCoreCLRControllerAdmission admission, CancellationToken cancellationToken = default) =>
        WarpCoreCLRWorkerProcess.ReleaseOwnedControllerAsync(admission, cancellationToken);
    internal static WarpCoreCLRQuarantineRecovery Mint(Command command, object exactTicket) =>
        WarpCoreCLRWorkerProcess.MintStoppedRecovery(command, exactTicket);
    internal static void Completed(Command command) => WarpCoreCLRWorkerProcess.CompleteStoppedRecovery(command);
    internal sealed class Command
    {
        internal Command(ulong ordinal, WarpCoreCLRWorkerProcess process, ulong sequence, byte[] digest,
            uint[] state, uint[] arena, string irHash, WarpCoreCLRCommandAdmission? admission,
            WarpCoreCLRControllerAdmission? controller = null, bool controllerRelease = false)
        { Ordinal = ordinal; Process = process; Sequence = sequence; Digest = digest; State = state; Arena = arena; IrHash = irHash;
            Admission = admission; Controller = controller; ControllerRelease = controllerRelease; }
        internal ulong Ordinal { get; }
        internal WarpCoreCLRWorkerProcess Process { get; }
        internal ulong Sequence { get; }
        internal byte[] Digest { get; }
        internal uint[] State { get; }
        internal uint[] Arena { get; }
        internal string IrHash { get; }
        internal WarpCoreCLRCommandAdmission? Admission { get; }
        internal WarpCoreCLRControllerAdmission? Controller { get; }
        internal bool ControllerRelease { get; }
        internal bool Stopped { get; private set; }
        internal WarpCoreCLRQuarantineRecovery? Recovery { get; private set; }
        internal bool Minted { get; private set; }
        internal void MarkStopped(object authority)
        { WarpCoreCLRWorkerProcess.ValidateRegistryAuthority(authority); Stopped = true; }
        internal void PublishRecovery(WarpCoreCLRQuarantineRecovery recovery, object authority)
        { WarpCoreCLRWorkerProcess.ValidateRegistryAuthority(authority); Recovery = recovery; Minted = true; }
    }
}
