using System.Collections.ObjectModel;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCoreCLRControllerPreparation
{
    internal WarpCoreCLRControllerPreparation(WarpCoreCLRControllerOperation operation, WarpCoreCLRWorkerLease lease,
        uint[] state, uint[] arena, int depth, string schemaHash, string planHash, uint scheduler, uint controller,
        IEnumerable<WarpCoreCLRPreparedCleanup> preparedCleanup)
    {
        ArgumentNullException.ThrowIfNull(lease); ArgumentNullException.ThrowIfNull(state); ArgumentNullException.ThrowIfNull(arena);
        if (!Enum.IsDefined(operation) || controller == 0 || depth < 1 || lease.IsFaulted || lease.IsReleased || ReferenceEquals(state, arena))
        { throw new ArgumentException("A controller preparation requires an exact live module, separate state, and captured grant.", nameof(lease)); }
        RequireHash(schemaHash); RequireHash(planHash);
        string ir = WarpIrHash.Compute(lease.Layout.Kernel);
        string expected = operation == WarpCoreCLRControllerOperation.RequestCollection ? WarpCoreCLRRecoveryCatalog.RequestCollection : WarpCoreCLRRecoveryCatalog.BeginDispatch;
        if (!string.Equals(ir, expected, StringComparison.Ordinal) || lease.Layout.Kernel.InputBufferCount != 2 ||
            lease.Layout.Kernel.ScalarArgumentCount != 0 || state.Length != lease.Layout.GetStateWords(depth))
        { throw new ArgumentException("Only the exact catalog transition and complete helper state may be prepared.", nameof(lease)); }
        WarpCoreCLRPreparedCleanup[] cleanup = preparedCleanup.Take(33).ToArray();
        if (cleanup.Length is < 3 or > 32 || cleanup.Any(binding => binding.Lease.IsFaulted || binding.Lease.IsReleased) ||
            cleanup.Count(binding => binding.Purpose == WarpCoreCLRCleanupPurpose.ReleaseCapturedController) != 1 ||
            cleanup.Count(binding => binding.Purpose == WarpCoreCLRCleanupPurpose.PublishControllerRelease) != 1 ||
            cleanup.Any(binding => binding.Purpose is not (WarpCoreCLRCleanupPurpose.StoppedController or WarpCoreCLRCleanupPurpose.ReleaseCapturedController or WarpCoreCLRCleanupPurpose.PublishControllerRelease)))
        { throw new ArgumentException("All bounded transition variants and one captured-owner release must already be prepared.", nameof(preparedCleanup)); }
        Operation = operation; Lease = lease; State = state; Arena = arena; Depth = depth;
        SchemaHash = schemaHash; PlanHash = planHash; Scheduler = scheduler; Controller = controller; IrHash = ir;
        Cleanup = Array.AsReadOnly(cleanup);
    }

    internal WarpCoreCLRControllerOperation Operation { get; }
    internal WarpCoreCLRWorkerLease Lease { get; }
    internal uint[] State { get; }
    internal uint[] Arena { get; }
    internal int Depth { get; }
    internal string SchemaHash { get; }
    internal string PlanHash { get; }
    internal string IrHash { get; }
    internal uint Scheduler { get; }
    internal uint Controller { get; }
    internal ReadOnlyCollection<WarpCoreCLRPreparedCleanup> Cleanup { get; }

    private static void RequireHash(string hash)
    {
        if (hash.Length != 64 || hash.Any(character => !char.IsAsciiHexDigit(character)))
        { throw new ArgumentException("Controller identity requires exact SHA256 schema and source-plan hashes.", nameof(hash)); }
    }
}
