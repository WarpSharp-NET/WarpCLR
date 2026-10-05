using System.Runtime.CompilerServices;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerProcess
{
    private static readonly ConditionalWeakTable<uint[], WarpCoreCLRGenerationTransition> GenerationReceipts = new();

    private static void BeginGenerationCore(WarpCoreCLRStoppedCommands.Command command)
    {
        if (command.Controller is not null || !Domains.TryGetValue(command.Arena, out ArenaDomain? domain) || domain.Owners.Count == 0 || domain.Quarantined) { return; }
        if (command.IrHash is WarpCoreCLRRecoveryCatalog.RequestCollection or WarpCoreCLRRecoveryCatalog.BeginDispatch)
        { throw new InvalidOperationException("Generation mutations require a separately registered prepared controller ticket before the first quantum."); }
    }

    private static (WarpCoreCLRGenerationTransition? Transition, WarpCoreCLRControllerCheckpoint? Checkpoint) ObserveGenerationCommit(
        WarpCoreCLRStoppedCommands.Command command, uint[] returnedState, uint[] returnedArena, byte[] response)
    {
        lock (RegistrySync)
        {
            if (command.Controller is not { } controller) { return (null, null); }
            if (command.ControllerRelease) { ValidateControllerRelease(command, returnedState, returnedArena); return (null, null); }
            ArenaDomain domain = Domains.GetValue(command.Arena, static _ => new ArenaDomain());
            WarpCoreCLRGenerationBaseline generation = controller.Baseline;
            generation.ValidateIntermediate(returnedArena);
            var checkpoint = new WarpCoreCLRControllerCheckpoint(command.Ordinal, command.Sequence, command.Process.CompiledModule,
                command.Digest, response, returnedState, returnedArena, generation.Scheduler);
            if (returnedState[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable) { return (null, checkpoint); }
            if (returnedState[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Completed || returnedState[WarpLogicalMachineLayout.ResultOffset] != 0)
            { throw new InvalidDataException("The prepared generation transition did not complete its exact successful catalog result."); }
            (uint dispatch, uint collection) = generation.Validate(returnedArena);
            generation.ValidateBarriers(returnedArena);
            var transition = new WarpCoreCLRGenerationTransition(command.Ordinal, command.Arena, generation.Operation, generation.Scheduler,
                generation.Dispatch, dispatch, generation.Collection, collection, domain.Owners.Values.ToArray(), controller);
            return (transition, checkpoint);
        }
    }

    internal static WarpCoreCLRGenerationTransition GetCommittedGenerationTransition(uint[] exactState)
    {
        lock (RegistrySync)
        {
            if (!GenerationReceipts.TryGetValue(exactState, out WarpCoreCLRGenerationTransition? receipt) || receipt.Applied)
            { throw new InvalidOperationException("No exact authenticated committed generation receipt exists for this state."); }
            return receipt;
        }
    }

    internal static async Task ApplyCommittedGenerationTransitionAsync(WarpCoreCLRGenerationTransition receipt,
        IReadOnlyList<WarpCoreCLRCommandAdmission> predecessors, IReadOnlyList<WarpCoreCLRCommandAdmission> successors,
        CancellationToken cancellationToken)
    {
        if (predecessors.Count != receipt.Predecessors.Count || successors.Count != predecessors.Count || successors.Count > 65536)
        { throw new InvalidOperationException("Generation replacement requires the complete admitted predecessor and successor census."); }
        uint[][] storage = predecessors.Concat(successors).Select(item => item.State).Append(receipt.Arena)
            .Distinct(ReferenceEqualityComparer.Instance).Cast<uint[]>().ToArray();
        using WarpCoreCLRWordTransaction.BatchLease ownership = await WarpCoreCLRWordTransaction.AcquireBatchAsync(storage, cancellationToken).ConfigureAwait(false);
        lock (RegistrySync) { ApplyGenerationCore(receipt, predecessors, successors); }
    }

    private static void ApplyGenerationCore(WarpCoreCLRGenerationTransition receipt, IReadOnlyList<WarpCoreCLRCommandAdmission> predecessors,
        IReadOnlyList<WarpCoreCLRCommandAdmission> successors)
    {
        if (!Domains.TryGetValue(receipt.Arena, out ArenaDomain? domain) || domain.Quarantined || receipt.Applied ||
            !ReferenceEquals(domain.PendingTransition, receipt) || receipt.Controller is null || !ReferenceEquals(domain.Controller, receipt.Controller) ||
            RegistryCommands.Values.Any(command => ReferenceEquals(command.Arena, receipt.Arena) && !command.Stopped) ||
            receipt.Arena[receipt.Scheduler + WarpPortableSchedulerLayout.DispatchGeneration] != receipt.ToDispatch ||
            receipt.Arena[receipt.Scheduler + WarpPortableSchedulerLayout.GCEpoch] != receipt.ToCollection)
        { throw new InvalidOperationException("The receipt is not the exact unused committed and paused arena transition."); }
        ValidateGenerationReplacements(receipt, domain, predecessors, successors);
        receipt.Controller.ValidateStorage();
        foreach (WarpCoreCLRCommandAdmission predecessor in predecessors) { StateDomains.Remove(predecessor.State); }
        domain.Owners.Clear();
        for (int index = 0; index < predecessors.Count; index++)
        {
            if (!ReferenceEquals(predecessors[index], successors[index])) { RetiredAdmissions.TryAdd(predecessors[index], new object()); }
            domain.Owners.Add(successors[index].State, successors[index]);
            StateDomains.Remove(successors[index].State); StateDomains.Add(successors[index].State, domain);
        }
        receipt.Controller.ReplacePausedStates(successors, RegistryAuthority);
        domain.PendingTransition = null; receipt.MarkApplied(RegistryAuthority);
    }

    private static void ValidateGenerationReplacements(WarpCoreCLRGenerationTransition receipt, ArenaDomain domain,
        IReadOnlyList<WarpCoreCLRCommandAdmission> predecessors, IReadOnlyList<WarpCoreCLRCommandAdmission> successors)
    {
        var remaining = new HashSet<WarpCoreCLRCommandAdmission>(receipt.Predecessors, ReferenceEqualityComparer.Instance);
        var states = new HashSet<uint[]>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < predecessors.Count; index++)
        {
            WarpCoreCLRCommandAdmission predecessor = predecessors[index], successor = successors[index];
            if (!remaining.Remove(predecessor) || !domain.Owners.TryGetValue(predecessor.State, out WarpCoreCLRCommandAdmission? prior) ||
                !ReferenceEquals(prior, predecessor) || predecessor.DispatchGeneration != receipt.FromDispatch || predecessor.CollectionGeneration != receipt.FromCollection ||
                !ReferenceEquals(successor.Arena, receipt.Arena) || ReferenceEquals(successor.State, receipt.Arena) || !states.Add(successor.State) ||
                successor.DispatchGeneration != receipt.ToDispatch || successor.CollectionGeneration != receipt.ToCollection ||
                !string.Equals(successor.SchemaHash, predecessor.SchemaHash, StringComparison.Ordinal) || !string.Equals(successor.PlanHash, predecessor.PlanHash, StringComparison.Ordinal) || RetiredAdmissions.TryGetValue(successor, out _) ||
                successor.Cleanup.Any(binding => binding.Lease.IsFaulted || binding.Lease.IsReleased) ||
                StateDomains.TryGetValue(successor.State, out ArenaDomain? ownerDomain) && !ReferenceEquals(ownerDomain, domain))
            { throw new InvalidOperationException("Generation replacement changed or omitted an exact admitted predecessor or immutable lineage."); }
        }
    }
}
