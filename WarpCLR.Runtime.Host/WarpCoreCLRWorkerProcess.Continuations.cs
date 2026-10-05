using System.Runtime.CompilerServices;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerProcess
{
    private static readonly ConditionalWeakTable<WarpCoreCLRCommandAdmission, object> RetiredAdmissions = new();

    internal static async Task RegisterPausedAdmissionAsync(WarpCoreCLRCommandAdmission admission, CancellationToken cancellationToken)
    {
        using WarpCoreCLRWordTransaction.Lease ownership = await WarpCoreCLRWordTransaction.AcquireAsync(
            admission.State, admission.Arena, cancellationToken).ConfigureAwait(false);
        lock (RegistrySync) { RegisterAdmissionCore(admission); }
    }

    internal static async Task ReplacePausedAdmissionAsync(WarpCoreCLRCommandAdmission predecessor,
        WarpCoreCLRCommandAdmission successor, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(predecessor.Arena, successor.Arena) || !ReferenceEquals(predecessor.ExactTicket, successor.ExactTicket) ||
            !string.Equals(predecessor.SchemaHash, successor.SchemaHash, StringComparison.Ordinal) ||
            !string.Equals(predecessor.PlanHash, successor.PlanHash, StringComparison.Ordinal))
        { throw new InvalidOperationException("Continuation replacement requires the exact arena, ticket, schema, and plan lineage."); }
        uint[][] storage = new[] { predecessor.State, successor.State, predecessor.Arena }.Distinct(ReferenceEqualityComparer.Instance).Cast<uint[]>().ToArray();
        using WarpCoreCLRWordTransaction.BatchLease ownership = await WarpCoreCLRWordTransaction.AcquireBatchAsync(storage, cancellationToken).ConfigureAwait(false);
        lock (RegistrySync)
        {
            ArenaDomain domain = RequirePausedPredecessor(predecessor);
            RegisterAdmissionCore(successor);
            if (!ReferenceEquals(predecessor.State, successor.State)) { domain.Owners.Remove(predecessor.State); StateDomains.Remove(predecessor.State); }
            RetiredAdmissions.TryAdd(predecessor, new object());
        }
    }

    internal static async Task RetirePausedAdmissionAsync(WarpCoreCLRCommandAdmission predecessor, CancellationToken cancellationToken)
    {
        using WarpCoreCLRWordTransaction.Lease ownership = await WarpCoreCLRWordTransaction.AcquireAsync(
            predecessor.State, predecessor.Arena, cancellationToken).ConfigureAwait(false);
        lock (RegistrySync)
        {
            ArenaDomain domain = RequirePausedPredecessor(predecessor);
            domain.Owners.Remove(predecessor.State); StateDomains.Remove(predecessor.State); RetiredAdmissions.TryAdd(predecessor, new object());
        }
    }

    private static ArenaDomain RequirePausedPredecessor(WarpCoreCLRCommandAdmission predecessor)
    {
        if (!Domains.TryGetValue(predecessor.Arena, out ArenaDomain? domain) || domain.Quarantined || domain.Controller is not null ||
            !domain.Owners.TryGetValue(predecessor.State, out WarpCoreCLRCommandAdmission? current) || !ReferenceEquals(current, predecessor) ||
            RegistryCommands.Values.Any(command => ReferenceEquals(command.Arena, predecessor.Arena) && !command.Stopped))
        { throw new InvalidOperationException("The exact registered predecessor is absent or still owns a native transaction."); }
        return domain;
    }
}
