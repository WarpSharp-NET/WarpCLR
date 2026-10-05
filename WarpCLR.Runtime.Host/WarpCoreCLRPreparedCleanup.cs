namespace WarpCLR.Runtime.Host;

internal sealed record WarpCoreCLRPreparedCleanup
{
    private readonly uint[] scalars;
    private readonly uint[][] inputs;
    private readonly uint[] retryResults;
    internal WarpCoreCLRPreparedCleanup(WarpCoreCLRWorkerLease lease, WarpCoreCLRCleanupPurpose purpose, uint expectedResult,
        IEnumerable<uint[]> exactInputs, IEnumerable<uint> exactScalars, IEnumerable<uint>? admittedRetryResults = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!Enum.IsDefined(purpose) || lease.IsFaulted) { throw new ArgumentException("Invalid prepared cleanup binding.", nameof(purpose)); }
        scalars = exactScalars.Take(IR.WarpCompilationAdmission.MaximumParametersPerBody + 1).ToArray();
        inputs = exactInputs.Take(IR.WarpCompilationAdmission.MaximumParametersPerBody + 1)
            .Select(input => input.Length == 1 ? (uint[])input.Clone() : throw new ArgumentException("Cleanup input banks contain exactly one word.", nameof(exactInputs))).ToArray();
        retryResults = admittedRetryResults?.Take(33).ToArray() ?? [];
        if (scalars.Length != lease.Layout.Kernel.ScalarArgumentCount || inputs.Length != lease.Layout.Kernel.InputBufferCount || retryResults.Length > 32)
        { throw new ArgumentException("Prepared cleanup requires exact bounded input and scalar arguments.", nameof(exactScalars)); }
        Lease = lease; Purpose = purpose; ExpectedResult = expectedResult;
        IrHash = IR.WarpIrHash.Compute(lease.Layout.Kernel); ProcessId = lease.ProcessId; Module = lease.CompiledModule;
        WarpCoreCLRRecoveryCatalog.Require(purpose, IrHash);
    }
    internal WarpCoreCLRWorkerLease Lease { get; }
    internal WarpCoreCLRCleanupPurpose Purpose { get; }
    internal uint ExpectedResult { get; }
    internal string IrHash { get; }
    internal int ProcessId { get; }
    internal Guid Module { get; }
    internal uint InputWord(int index) => inputs[index][0];
    internal void RequireControllerArguments(uint word, uint expected, uint replacement)
    {
        if (inputs.Length != 3 || scalars.Length != 0 || InputWord(0) != word || InputWord(1) != expected || InputWord(2) != replacement)
        { throw new InvalidOperationException("Controller cleanup did not bind the exact CAS index, captured owner, and replacement."); }
    }
    internal void ValidateArguments(uint[][] inputs, uint[] arguments)
    {
        if (!MatchesArguments(inputs, arguments))
        { throw new InvalidOperationException("Cleanup changed its pre-admitted exact service arguments or CAS ownership operands."); }
    }
    internal bool MatchesArguments(uint[][] inputs, uint[] arguments)
    {
        if (inputs.Length != this.inputs.Length || !scalars.AsSpan().SequenceEqual(arguments)) { return false; }
        for (int index = 0; index < inputs.Length; index++)
        {
            if (!this.inputs[index].AsSpan().SequenceEqual(inputs[index])) { return false; }
        }
        return true;
    }
    internal bool MayRetry(uint result) => retryResults.Contains(result);
    internal WarpCoreCLRPreparedCleanup Retain() => new(Lease.Retain(), Purpose, ExpectedResult, inputs, scalars, retryResults);
}
