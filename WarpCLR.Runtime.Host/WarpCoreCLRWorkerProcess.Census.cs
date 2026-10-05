using System.Runtime.CompilerServices;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerProcess
{
    private static readonly ConditionalWeakTable<uint[], ArenaDomain> Domains = new();

    private static void RegisterAdmissionCore(WarpCoreCLRCommandAdmission admission)
    {
        if (RetiredAdmissions.TryGetValue(admission, out _)) { throw new InvalidOperationException("An exact retired continuation admission cannot be revived."); }
        ArenaDomain domain = Domains.GetValue(admission.Arena, static _ => new ArenaDomain());
        if (domain.Quarantined || domain.Controller is not null || domain.PendingTransition is not null ||
            domain.Generation is not null && !ReferenceEquals(domain.Generation.State, admission.State) ||
            domain.Owners.Count >= 65536 && !domain.Owners.ContainsKey(admission.State))
        { throw new InvalidOperationException("Stopped arena ownership cannot be extended or exceed its census admission."); }
        if (domain.Owners.Values.Any(owner => !string.Equals(owner.SchemaHash, admission.SchemaHash, StringComparison.Ordinal) ||
            !string.Equals(owner.PlanHash, admission.PlanHash, StringComparison.Ordinal) || owner.DispatchGeneration != admission.DispatchGeneration ||
            owner.CollectionGeneration != admission.CollectionGeneration))
        { throw new InvalidOperationException("Arena commands changed their admitted schema, plan, or dispatch/collection generation."); }
        if (RegistryCommands.Values.Any(command => ReferenceEquals(command.State, admission.State) && !command.Stopped))
        { throw new InvalidOperationException("A native command still owns this exact logical continuation."); }
        if (StateDomains.TryGetValue(admission.State, out ArenaDomain? priorDomain) && !ReferenceEquals(priorDomain, domain))
        { throw new InvalidOperationException("A logical continuation cannot belong to two arena ownership domains."); }
        domain.Owners[admission.State] = admission;
        StateDomains.Remove(admission.State); StateDomains.Add(admission.State, domain);
    }

    private static WarpCoreCLRStoppedCensus CaptureCensus(WarpCoreCLRStoppedCommands.Command command)
    {
        if (RegistryCommands.Values.Any(other => ReferenceEquals(other.Arena, command.Arena) && !other.Stopped))
        { throw new InvalidOperationException("Another native arena command has not stopped; recovery cannot be minted."); }
        ArenaDomain domain = Domains.GetValue(command.Arena, static _ => new ArenaDomain());
        domain.Quarantined = true;
        WarpCoreCLRCommandAdmission[] owners = domain.Owners.Values.ToArray();
        if (command.Controller is null && (owners.Length == 0 || !owners.Any(owner => ReferenceEquals(owner, command.Admission))) ||
            command.Controller is not null && !ReferenceEquals(domain.Controller, command.Controller))
        { throw new InvalidOperationException("The stopped ticket is absent from the authenticated arena ownership census."); }
        foreach (WarpCoreCLRCommandAdmission owner in owners) { WarpCoreCLRWordTransaction.Quarantine(owner.State, owner.Arena); }
        if (command.Controller is not null) { WarpCoreCLRWordTransaction.Quarantine(command.Controller.Preparation.State, command.Arena); }
        return new WarpCoreCLRStoppedCensus(owners, command.Controller);
    }

    private sealed class ArenaDomain
    {
        internal Dictionary<uint[], WarpCoreCLRCommandAdmission> Owners { get; } = new(ReferenceEqualityComparer.Instance);
        internal bool Quarantined { get; set; }
        internal WarpCoreCLRGenerationBaseline? Generation { get; set; }
        internal WarpCoreCLRGenerationTransition? PendingTransition { get; set; }
        internal WarpCoreCLRControllerAdmission? Controller { get; set; }
    }
}
