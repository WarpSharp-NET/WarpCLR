using System.Collections.ObjectModel;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCoreCLRControllerAdmission
{
    internal const string Version = "warp.coreclr.remote-controller-registry/checkpointed-grant-census/0.2";
    private readonly List<WarpCoreCLRWorkerLease> retained = [];
    private readonly Dictionary<uint[], byte[]> pausedStates = new(ReferenceEqualityComparer.Instance);
    private int released;

    private WarpCoreCLRControllerAdmission(ulong identity, WarpCoreCLRControllerPreparation preparation,
        WarpCoreCLRCommandAdmission[] owners)
    {
        Identity = identity; Preparation = preparation;
        Baseline = new(preparation.State, preparation.Arena, preparation.IrHash, preparation.Scheduler, preparation.Controller);
        Checkpoint = new(0, 0, preparation.Lease.CompiledModule, [], [], preparation.State, preparation.Arena, preparation.Scheduler);
        foreach (WarpCoreCLRCommandAdmission owner in owners) { pausedStates.Add(owner.State, WarpCoreCLRControllerCheckpoint.Digest(owner.State)); }
    }

    internal ulong Identity { get; }
    internal WarpCoreCLRControllerPreparation Preparation { get; }
    internal WarpCoreCLRGenerationBaseline Baseline { get; }
    internal WarpCoreCLRControllerCheckpoint Checkpoint { get; private set; }
    internal WarpCoreCLRGenerationTransition? Receipt { get; private set; }
    internal WarpCoreCLRWorkerLease Service { get; private set; } = null!;
    internal ReadOnlyCollection<WarpCoreCLRPreparedCleanup> Cleanup { get; private set; } = null!;
    internal WarpCoreCLRPreparedCleanup Release => Cleanup.Single(binding => binding.Purpose == WarpCoreCLRCleanupPurpose.ReleaseCapturedController);
    internal WarpCoreCLRPreparedCleanup PublicationRelease => Cleanup.Single(binding => binding.Purpose == WarpCoreCLRCleanupPurpose.PublishControllerRelease);
    internal uint[] ReleaseState { get; private set; } = [];
    internal bool ReleaseCompleted { get; private set; }

    internal static async Task<WarpCoreCLRControllerAdmission> CreateAsync(ulong identity, WarpCoreCLRControllerPreparation preparation,
        WarpCoreCLRCommandAdmission[] owners, object authority)
    {
        WarpCoreCLRWorkerProcess.ValidateRegistryAuthority(authority);
        var admission = new WarpCoreCLRControllerAdmission(identity, preparation, owners);
        try
        {
            admission.Service = preparation.Lease.Retain(); admission.retained.Add(admission.Service);
            var cleanup = new List<WarpCoreCLRPreparedCleanup>();
            foreach (WarpCoreCLRPreparedCleanup binding in preparation.Cleanup)
            {
                WarpCoreCLRPreparedCleanup copy = binding.Retain(); cleanup.Add(copy); admission.retained.Add(copy.Lease);
            }
            admission.Cleanup = cleanup.AsReadOnly();
            admission.ReleaseState = admission.PublicationRelease.Lease.Layout.CreateInitialState(32, 100_000_000);
            return admission;
        }
        catch { await admission.ReleaseLeasesAsync(authority).ConfigureAwait(false); throw; }
    }

    internal void ValidateStorage()
    {
        Checkpoint.ValidateStorage(Preparation.State, Preparation.Arena);
        foreach ((uint[] state, byte[] digest) in pausedStates)
        {
            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(digest, WarpCoreCLRControllerCheckpoint.Digest(state)))
            { throw new InvalidOperationException("A paused source/helper continuation changed under controller ownership."); }
        }
    }

    internal void PublishCheckpoint(WarpCoreCLRControllerCheckpoint checkpoint, object authority)
    { WarpCoreCLRWorkerProcess.ValidateRegistryAuthority(authority); Checkpoint = checkpoint; }
    internal void PublishReceipt(WarpCoreCLRGenerationTransition receipt, object authority)
    { WarpCoreCLRWorkerProcess.ValidateRegistryAuthority(authority); Receipt = receipt; }
    internal void MarkReleaseCompleted(object authority)
    { WarpCoreCLRWorkerProcess.ValidateRegistryAuthority(authority); ReleaseCompleted = true; }

    internal void ReplacePausedStates(IEnumerable<WarpCoreCLRCommandAdmission> successors, object authority)
    {
        WarpCoreCLRWorkerProcess.ValidateRegistryAuthority(authority);
        pausedStates.Clear();
        foreach (WarpCoreCLRCommandAdmission owner in successors) { pausedStates.Add(owner.State, WarpCoreCLRControllerCheckpoint.Digest(owner.State)); }
    }

    internal WarpCoreCLRPreparedCleanup CurrentCleanup() => Cleanup.Single(binding => binding.Purpose == WarpCoreCLRCleanupPurpose.StoppedController &&
        binding.InputWord(5) == Checkpoint.Dispatch && binding.InputWord(6) == Checkpoint.Collection);

    internal async Task ReleaseLeasesAsync(object authority)
    {
        WarpCoreCLRWorkerProcess.ValidateRegistryAuthority(authority);
        if (Interlocked.Exchange(ref released, 1) != 0) { return; }
        for (int index = 0; index < retained.Count; index++) { await retained[index].DisposeAsync().ConfigureAwait(false); }
    }
}
