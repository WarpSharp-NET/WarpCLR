using System.Collections.ObjectModel;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCoreCLRCommandAdmission
{
    internal WarpCoreCLRCommandAdmission(object exactTicket, uint[] state, uint[] arena, string schemaHash, string planHash,
        string sourceIrHash, uint dispatchGeneration, uint collectionGeneration, uint capturedControllerOwner,
        IEnumerable<WarpCoreCLRPreparedCleanup> preparedCleanup)
    {
        ArgumentNullException.ThrowIfNull(exactTicket); ArgumentNullException.ThrowIfNull(state); ArgumentNullException.ThrowIfNull(arena);
        RequireHash(schemaHash); RequireHash(planHash); RequireHash(sourceIrHash);
        WarpCoreCLRPreparedCleanup[] cleanup = preparedCleanup.Take(33).ToArray();
        if (cleanup.Length is 0 or > 32 || cleanup.Any(item => item.Lease.IsFaulted) ||
            cleanup.Count(item => item.Purpose == WarpCoreCLRCleanupPurpose.StoppedFault) != 1 ||
            capturedControllerOwner != 0 && cleanup.Any(item => item.Purpose == WarpCoreCLRCleanupPurpose.AcquireFreeController))
        { throw new ArgumentException("Cleanup kernels must be bounded, pre-admitted, prepared live remote leases.", nameof(preparedCleanup)); }
        ExactTicket = exactTicket; State = state; Arena = arena; SchemaHash = schemaHash; PlanHash = planHash;
        SourceIrHash = sourceIrHash; DispatchGeneration = dispatchGeneration; CollectionGeneration = collectionGeneration;
        CapturedControllerOwner = capturedControllerOwner; Cleanup = Array.AsReadOnly(cleanup);
        ValidatePreparedController();
    }

    internal object ExactTicket { get; }
    internal uint[] State { get; }
    internal uint[] Arena { get; }
    internal string SchemaHash { get; }
    internal string PlanHash { get; }
    internal string SourceIrHash { get; }
    internal uint DispatchGeneration { get; }
    internal uint CollectionGeneration { get; }
    internal uint CapturedControllerOwner { get; }
    internal ReadOnlyCollection<WarpCoreCLRPreparedCleanup> Cleanup { get; }

    internal void Validate(string irHash, uint[] state, uint[] arena)
    {
        if (!ReferenceEquals(State, state) || !ReferenceEquals(Arena, arena) || !string.Equals(SourceIrHash, irHash, StringComparison.Ordinal))
        { throw new InvalidOperationException("The command changed its exact admitted ticket storage or source closure."); }
    }

    private void ValidatePreparedController()
    {
        WarpCoreCLRPreparedCleanup fault = Cleanup.Single(binding => binding.Purpose == WarpCoreCLRCleanupPurpose.StoppedFault);
        uint scheduler = fault.InputWord(0), controller = fault.InputWord(1);
        if (scheduler > Arena.LongLength - Compiler.WarpPortableSchedulerLayout.HeaderWords || controller == 0 || fault.ExpectedResult != 0 ||
            Arena[scheduler] != Compiler.WarpPortableSchedulerLayout.Magic || Arena[scheduler + 1] != 2 ||
            Arena[scheduler + Compiler.WarpPortableSchedulerLayout.ControllerOwner] != CapturedControllerOwner ||
            CapturedControllerOwner != 0 && CapturedControllerOwner != controller)
        { throw new InvalidOperationException("The cleanup binding changed its exact admitted controller domain or captured owner."); }
        if (string.Equals(fault.IrHash, WarpCoreCLRRecoveryCatalog.DisposeStoppedCensus, StringComparison.Ordinal) &&
            (fault.InputWord(2) != DispatchGeneration || fault.InputWord(3) != CollectionGeneration))
        { throw new InvalidOperationException("Stopped census cleanup changed its admitted dispatch or collection generation."); }
        WarpCoreCLRPreparedCleanup[] releases = Cleanup.Where(binding => binding.Purpose == WarpCoreCLRCleanupPurpose.ReleaseCapturedController).Take(2).ToArray();
        WarpCoreCLRPreparedCleanup[] acquisitions = Cleanup.Where(binding => binding.Purpose == WarpCoreCLRCleanupPurpose.AcquireFreeController).Take(2).ToArray();
        if (releases.Length != 1 || releases[0].ExpectedResult != controller || acquisitions.Length != (CapturedControllerOwner == 0 ? 1 : 0))
        { throw new InvalidOperationException("Cleanup requires one exact captured release and a free claim only when admitted free."); }
        uint word = checked(scheduler + Compiler.WarpPortableSchedulerLayout.ControllerOwner);
        releases[0].RequireControllerArguments(word, controller, 0);
        if (acquisitions.Length != 0)
        {
            if (acquisitions[0].ExpectedResult != 0) { throw new InvalidOperationException("The free controller claim requires a successful zero comparand."); }
            acquisitions[0].RequireControllerArguments(word, 0, controller);
        }
    }

    private static void RequireHash(string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (hash.Length != 64 || hash.Any(character => character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F') and not (>= 'a' and <= 'f')))
        { throw new ArgumentException("Command admission requires exact SHA256 schema, plan, and closure identities.", nameof(hash)); }
    }
}

