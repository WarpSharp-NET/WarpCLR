using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCoreCLRQuarantineRecovery
{
    private readonly Lock sync = new();
    private readonly WarpCoreCLRStoppedCommands.Command command;
    private readonly HashSet<WarpCoreCLRPreparedCleanup> completed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<uint[], WarpCoreCLRPreparedCleanup> states = new(ReferenceEqualityComparer.Instance);
    private readonly IReadOnlyList<WarpCoreCLRPreparedCleanup> cleanup;
    private bool finished;

    internal WarpCoreCLRQuarantineRecovery(WarpCoreCLRStoppedCommands.Command command, WarpCoreCLRStoppedCensus census, object authority)
    {
        WarpCoreCLRWorkerProcess.ValidateRegistryAuthority(authority);
        this.command = command; Census = census;
        cleanup = command.Controller is { } controller ? [controller.CurrentCleanup(), controller.Release] : command.Admission!.Cleanup;
    }
    internal WarpCoreCLRStoppedCensus Census { get; }
    internal void ValidateArena(uint[] arena)
    {
        WarpCoreCLRWorkerProcess.ValidateRecovery(command, this);
        if (!ReferenceEquals(arena, command.Arena)) { throw new InvalidOperationException("Cleanup changed its exact stopped arena."); }
    }
    internal WarpCoreCLRCommandAdmission Admission => command.Admission ?? throw new InvalidOperationException("A controller participant is independent of source admissions.");
    internal IReadOnlyList<WarpCoreCLRPreparedCleanup> Cleanup => cleanup;
    internal ulong StoppedOrdinal => command.Ordinal;

    internal void Validate(WarpCoreCLRWorkerProcess process, string irHash, uint[][] inputs, uint[] scalars, uint[] state, uint[] arena, object authority)
    {
        WarpCoreCLRWorkerProcess.ValidateRegistryAuthority(authority);
        ValidateArena(arena);
        lock (sync)
        {
            if (finished || !ReferenceEquals(arena, command.Arena) || ReferenceEquals(state, command.State))
            { throw new InvalidOperationException("Cleanup capability cannot resume source or change its stopped arena."); }
            WarpCoreCLRPreparedCleanup[] candidates = cleanup.Where(item =>
                item.ProcessId == process.ProcessId && item.Module == process.CompiledModule && string.Equals(item.IrHash, irHash, StringComparison.Ordinal) &&
                !completed.Contains(item) && item.MatchesArguments(inputs, scalars)).Take(2).ToArray();
            if (candidates.Length != 1 || candidates[0].Lease.IsFaulted)
            { throw new InvalidOperationException("Cleanup requires an exact already-prepared admitted module and purpose."); }
            WarpCoreCLRPreparedCleanup binding = candidates[0];
            binding.ValidateArguments(inputs, scalars);
            if (states.TryGetValue(state, out WarpCoreCLRPreparedCleanup? prior) && !ReferenceEquals(binding, prior))
            { throw new InvalidOperationException("A cleanup continuation changed its admitted service."); }
            states.TryAdd(state, binding);
        }
    }

    internal void ObserveGeneratedCompletion(uint[] state, uint[] returnedState, object authority)
    {
        WarpCoreCLRWorkerProcess.ValidateRegistryAuthority(authority);
        lock (sync)
        {
            WarpCoreCLRPreparedCleanup binding = states[state];
            if (returnedState[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Faulted)
            { finished = true; throw new InvalidOperationException("Generated cleanup faulted; arena remains quarantined."); }
            if (returnedState[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Completed)
            {
                if (returnedState[WarpLogicalMachineLayout.ResultOffset] != binding.ExpectedResult)
                {
                    if (binding.MayRetry(returnedState[WarpLogicalMachineLayout.ResultOffset])) { states.Remove(state); return; }
                    finished = true; throw new InvalidOperationException("Generated cleanup did not confirm its admitted abort result.");
                }
                completed.Add(binding);
            }
        }
    }

    internal async Task AcknowledgeGeneratedAbortAsync(object exactTicket, CancellationToken cancellationToken)
    {
        using WarpCoreCLRWordTransaction.Lease ownership = await WarpCoreCLRWordTransaction.AcquireQuarantinedAsync(
            new uint[1], command.Arena, this, cancellationToken).ConfigureAwait(false);
        lock (sync)
        {
            object? admittedTicket = command.Controller is { } controller ? controller : command.Admission?.ExactTicket;
            if (finished || !ReferenceEquals(admittedTicket, exactTicket) ||
                !completed.Any(binding => binding.Purpose is WarpCoreCLRCleanupPurpose.StoppedFault or WarpCoreCLRCleanupPurpose.StoppedController) ||
                cleanup.Any(binding => !completed.Contains(binding)))
            { throw new InvalidOperationException("Every fixed admitted generated cleanup purpose must complete for the exact ticket."); }
            if (command.Controller is not null) { WarpCoreCLRWorkerProcess.ValidateControllerRecoveryTerminal(command.Controller); }
            finished = true; WarpCoreCLRStoppedCommands.Completed(command);
            // The stopped arena remains permanently quarantined for ordinary execution.
        }
        if (command.Controller is not null) { await WarpCoreCLRWorkerProcess.ReleaseRecoveredControllerLeasesAsync(command.Controller).ConfigureAwait(false); }
    }
}
