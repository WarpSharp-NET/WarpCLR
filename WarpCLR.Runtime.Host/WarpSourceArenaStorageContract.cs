using System.Collections.Frozen;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

// Exact object-alias consistency requirements. The real registry must install
// them under its locks and supply private phase and purpose-bound authority.
internal sealed class WarpSourceArenaStorageContract
{
    internal const string Version = "warp.source-arena-storage/reference-identity-state-arena-input-scalar-all-route-requirements-only/0.1";
    private readonly uint[] arena;
    private readonly FrozenSet<uint[]> states;

    internal WarpSourceArenaStorageContract(uint[] arena, ReadOnlySpan<uint[]> capturedStates)
    {
        ArgumentNullException.ThrowIfNull(arena);
        if (capturedStates.IsEmpty || capturedStates.Length > WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry)
        { throw new ArgumentException("A finite exact nonempty continuation census is required.", nameof(capturedStates)); }
        foreach (ref readonly uint[] state in capturedStates)
        {
            if (state is null || ReferenceEquals(state, arena))
            { throw new ArgumentException("Arena and exact nonnull continuation banks require separate admitted storage.", nameof(capturedStates)); }
        }
        states = capturedStates.ToArray().ToFrozenSet<uint[]>(ReferenceEqualityComparer.Instance); this.arena = arena;
        if (states.Count != capturedStates.Length) { throw new ArgumentException("Duplicate continuation objects cannot manufacture census members.", nameof(capturedStates)); }
    }

    internal WarpSourceArenaDecision ClassifyCandidate(WarpSourceArenaPhase phase, WarpSourceArenaRoute route,
        ReadOnlySpan<uint[]> requestStates, uint[] requestArena, ReadOnlySpan<uint[]> inputs, uint[] scalars)
    {
        if (!Enum.IsDefined(phase) || !Enum.IsDefined(route)) { return WarpSourceArenaDecision.Excluded; }
        if (requestStates.Length > WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry || inputs.Length > WarpCompilationAdmission.MaximumParametersPerBody)
        { throw new ArgumentException("The candidate storage references exceed existing finite admission.", nameof(requestStates)); }
        bool touches = Protected(requestArena) || Protected(scalars) || ContainsProtected(requestStates) || ContainsProtected(inputs);
        bool privateRoute = route is WarpSourceArenaRoute.OwnedSource or WarpSourceArenaRoute.OwnedPublication or WarpSourceArenaRoute.NormalRelease or WarpSourceArenaRoute.FixedStoppedCleanup;
        if (privateRoute && (!ReferenceEquals(requestArena, arena) || requestStates.IsEmpty || !AllCaptured(requestStates)))
        { return WarpSourceArenaDecision.Excluded; }
        if (touches || privateRoute) { return WarpSourceArenaParticipation.Classify(phase, route); }
        return WarpSourceArenaDecision.ExistingAdmissionRequired;
    }

    private bool Protected(uint[] candidate) => ReferenceEquals(candidate, arena) || states.Contains(candidate);
    private bool ContainsProtected(ReadOnlySpan<uint[]> candidates)
    {
        foreach (ref readonly uint[] candidate in candidates) { if (Protected(candidate)) { return true; } }
        return false;
    }
    private bool AllCaptured(ReadOnlySpan<uint[]> candidates)
    {
        foreach (ref readonly uint[] candidate in candidates) { if (!states.Contains(candidate)) { return false; } }
        return true;
    }
}
