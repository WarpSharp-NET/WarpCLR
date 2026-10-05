using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerProcess
{
    private static readonly ConditionalWeakTable<uint[], ArenaDomain> StateDomains = new();
    private static ulong nextControllerIdentity;

    internal static async Task<WarpCoreCLRControllerAdmission> RegisterPreparedControllerAsync(
        WarpCoreCLRControllerPreparation preparation, CancellationToken cancellationToken)
    {
        WarpCoreCLRCommandAdmission[] owners;
        lock (RegistrySync)
        {
            ArenaDomain domain = Domains.GetValue(preparation.Arena, static _ => new ArenaDomain());
            RequireAvailableControllerDomain(domain, preparation);
            owners = domain.Owners.Values.ToArray();
        }
        uint[][] storage = owners.Select(owner => owner.State).Append(preparation.State).Append(preparation.Arena)
            .Distinct(ReferenceEqualityComparer.Instance).Cast<uint[]>().ToArray();
        using WarpCoreCLRWordTransaction.BatchLease ownership = await WarpCoreCLRWordTransaction.AcquireBatchAsync(storage, cancellationToken).ConfigureAwait(false);
        ulong identity;
        lock (RegistrySync)
        {
            ArenaDomain domain = Domains.GetValue(preparation.Arena, static _ => new ArenaDomain());
            RequireAvailableControllerDomain(domain, preparation); ValidateControllerCensus(domain, owners, preparation);
            if (nextControllerIdentity == ulong.MaxValue) { throw new InvalidOperationException("Controller admission namespace exhausted."); }
            identity = ++nextControllerIdentity;
        }
        WarpCoreCLRControllerAdmission admission = await WarpCoreCLRControllerAdmission.CreateAsync(identity, preparation, owners, RegistryAuthority).ConfigureAwait(false);
        try
        {
            lock (RegistrySync)
            {
                ArenaDomain domain = Domains.GetValue(preparation.Arena, static _ => new ArenaDomain());
                RequireAvailableControllerDomain(domain, preparation); ValidateControllerCensus(domain, owners, preparation);
                domain.Controller = admission;
                StateDomains.Add(preparation.State, domain);
            }
            return admission;
        }
        catch { await admission.ReleaseLeasesAsync(RegistryAuthority).ConfigureAwait(false); throw; }
    }

    private static void RequireAvailableControllerDomain(ArenaDomain domain, WarpCoreCLRControllerPreparation preparation)
    {
        if (domain.Quarantined || domain.Controller is not null || domain.PendingTransition is not null || domain.Generation is not null ||
            StateDomains.TryGetValue(preparation.State, out _) ||
            RegistryCommands.Values.Any(command => ReferenceEquals(command.Arena, preparation.Arena) && !command.Stopped))
        { throw new InvalidOperationException("A controller admission requires an independently paused, unheld arena and helper state."); }
    }

    private static void ValidateControllerCensus(ArenaDomain domain, WarpCoreCLRCommandAdmission[] owners,
        WarpCoreCLRControllerPreparation preparation)
    {
        if (owners.Length != domain.Owners.Count || owners.Any(owner => !domain.Owners.TryGetValue(owner.State, out WarpCoreCLRCommandAdmission? actual) ||
            !ReferenceEquals(actual, owner) || !string.Equals(owner.SchemaHash, preparation.SchemaHash, StringComparison.Ordinal) ||
            !string.Equals(owner.PlanHash, preparation.PlanHash, StringComparison.Ordinal)))
        { throw new InvalidOperationException("The complete paused census changed before controller admission."); }
        ValidateInitialControllerState(preparation);
        string schema = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(preparation.Arena.AsSpan(
            checked((int)(preparation.Scheduler + WarpPortableSchedulerLayout.SchemaHash)), 8))));
        if (!string.Equals(schema, preparation.SchemaHash, StringComparison.OrdinalIgnoreCase))
        { throw new InvalidOperationException("Controller admission changed the exact arena schema identity."); }
        var baseline = new WarpCoreCLRGenerationBaseline(preparation.State, preparation.Arena, preparation.IrHash, preparation.Scheduler, preparation.Controller);
        if (owners.Any(owner => owner.DispatchGeneration != baseline.Dispatch || owner.CollectionGeneration != baseline.Collection))
        { throw new InvalidOperationException("The paused census changed its exact committed generations."); }
        ValidateControllerBindings(preparation, baseline);
    }

    private static void ValidateInitialControllerState(WarpCoreCLRControllerPreparation preparation)
    {
        WarpLogicalMachineLayout layout = preparation.Lease.Layout;
        WarpNativeMachineLaunch.ValidateReturnedStates(layout, preparation.State, 1, preparation.Depth);
        ulong budget = preparation.State[WarpLogicalMachineLayout.RemainingStepsLowOffset] |
            (ulong)preparation.State[WarpLogicalMachineLayout.RemainingStepsHighOffset] << 32;
        if (budget is 0 or > long.MaxValue) { throw new InvalidOperationException("The initial controller state changed its positive bounded step budget."); }
        uint[] expected = layout.CreateInitialState(preparation.Depth, (long)budget);
        expected[WarpLogicalMachineLayout.OwnerContextOffset] = preparation.State[WarpLogicalMachineLayout.OwnerContextOffset];
        if (!expected.AsSpan().SequenceEqual(preparation.State))
        { throw new InvalidOperationException("A controller ticket cannot adopt a caller-forged partial continuation or private bank."); }
    }

    private static void ValidateControllerBindings(WarpCoreCLRControllerPreparation preparation, WarpCoreCLRGenerationBaseline baseline)
    {
        WarpCoreCLRPreparedCleanup release = preparation.Cleanup.First(binding => binding.Purpose == WarpCoreCLRCleanupPurpose.ReleaseCapturedController);
        release.RequireControllerArguments(checked(preparation.Scheduler + WarpPortableSchedulerLayout.ControllerOwner), preparation.Controller, 0);
        if (release.ExpectedResult != preparation.Controller) { throw new InvalidOperationException("Captured-owner release changed its admitted result."); }
        WarpCoreCLRPreparedCleanup publication = preparation.Cleanup.First(binding => binding.Purpose == WarpCoreCLRCleanupPurpose.PublishControllerRelease);
        publication.RequireControllerArguments(checked(preparation.Scheduler + WarpPortableSchedulerLayout.ControllerOwner), preparation.Controller, 0);
        if (publication.ExpectedResult != preparation.Controller || publication.ProcessId == release.ProcessId || publication.Module == release.Module)
        { throw new InvalidOperationException("Normal release and emergency release require independent already-prepared exact CAS modules."); }
        bool unchangedCollection = preparation.Operation == WarpCoreCLRControllerOperation.RequestCollection &&
            preparation.Arena[preparation.Scheduler + WarpPortableSchedulerLayout.GCState] == WarpPortableSchedulerLayout.GCRequested;
        uint nextDispatch = preparation.Operation == WarpCoreCLRControllerOperation.BeginDispatch ? checked(baseline.Dispatch + 1) : baseline.Dispatch;
        uint nextEpoch = preparation.Operation == WarpCoreCLRControllerOperation.RequestCollection && !unchangedCollection ? checked(baseline.Collection + 1) : baseline.Collection;
        WarpCoreCLRPreparedCleanup[] variants = preparation.Cleanup.Where(binding => binding.Purpose == WarpCoreCLRCleanupPurpose.StoppedController).ToArray();
        if (variants.Length != (unchangedCollection ? 1 : 2)) { throw new InvalidOperationException("Every exact before/after transition cleanup variant must be prepared once."); }
        RequireControllerVariant(variants, preparation, baseline, baseline.Dispatch, baseline.Collection);
        RequireControllerVariant(variants, preparation, baseline, nextDispatch, nextEpoch);
    }

    private static void RequireControllerVariant(WarpCoreCLRPreparedCleanup[] variants, WarpCoreCLRControllerPreparation preparation,
        WarpCoreCLRGenerationBaseline baseline, uint dispatch, uint epoch)
    {
        uint[][] inputs = [[preparation.Scheduler], [preparation.Controller], [(uint)preparation.Operation],
            [baseline.Dispatch], [baseline.Collection], [dispatch], [epoch]];
        if (variants.Count(binding => binding.ExpectedResult == 0 && binding.MatchesArguments(inputs, [])) != 1)
        { throw new InvalidOperationException("A controller cleanup variant changed the fixed operation or bound original/current generations."); }
    }

    internal static void ValidateOrdinaryStorage(IReadOnlyList<uint[]> states, uint[] arena)
    {
        lock (RegistrySync) { ValidateOrdinaryStorageCore(states, arena); }
    }

    private static void ValidateOrdinaryStorageCore(IReadOnlyList<uint[]> states, uint[] arena)
    {
        if (Domains.TryGetValue(arena, out ArenaDomain? domain) && domain.Controller is not null ||
            states.Any(state => StateDomains.TryGetValue(state, out ArenaDomain? owner) && owner.Controller is not null))
        { throw new InvalidOperationException("Ordinary source/arena execution is excluded until authenticated captured-owner release."); }
    }

    private static void ValidateControllerCommand(WarpCoreCLRStoppedCommands.Command command, uint[][] inputs, uint[] scalars)
    {
        WarpCoreCLRControllerAdmission admission = command.Controller!;
        if (!Domains.TryGetValue(command.Arena, out ArenaDomain? domain) || domain.Quarantined || !ReferenceEquals(domain.Controller, admission))
        { throw new InvalidOperationException("The controller ticket is not the exact current runtime-issued arena owner."); }
        admission.ValidateStorage();
        WarpCoreCLRWorkerLease lease = command.ControllerRelease ? admission.PublicationRelease.Lease : admission.Service;
        uint[] expectedState = command.ControllerRelease ? admission.ReleaseState : admission.Preparation.State;
        if (!ReferenceEquals(command.State, expectedState) || command.Process.ProcessId != lease.ProcessId || command.Process.CompiledModule != lease.CompiledModule ||
            !string.Equals(command.IrHash, WarpIrHash.Compute(lease.Layout.Kernel), StringComparison.Ordinal) || lease.IsFaulted || lease.IsReleased)
        { throw new InvalidOperationException("Controller execution changed its exact retained module, IR, or state."); }
        if (command.ControllerRelease)
        {
            if (admission.Receipt is not { Applied: true } || admission.ReleaseCompleted || domain.PendingTransition is not null)
            { throw new InvalidOperationException("Controller release requires an applied unused exact generation receipt."); }
            admission.PublicationRelease.ValidateArguments(inputs, scalars);
        }
        else if (admission.Receipt is not null || scalars.Length != 0 || inputs.Length != 2 || inputs[0].Length != 1 || inputs[1].Length != 1 ||
            inputs[0][0] != admission.Preparation.Scheduler || inputs[1][0] != admission.Preparation.Controller)
        { throw new InvalidOperationException("Controller continuation changed its fixed catalog arguments or completed ticket."); }
    }
}
