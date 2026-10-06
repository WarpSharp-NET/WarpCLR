namespace WarpCLR.IR;

/// <summary>Retains an ordinary use of exact managed word arrays.</summary>
/// <remarks>This denial ledger never grants managed Source execution or recovery permission.</remarks>
public sealed class WarpOrdinaryArrayAdmission : IDisposable
{
    private readonly uint[][] inputBanks;
    private readonly IDisposable use;
    private int disposed;

    internal WarpOrdinaryArrayAdmission(object issuer, uint[][] capturedInputs, IDisposable use)
    {
        WarpOrdinaryArrayRegistry.ValidateIssuer(issuer);
        ArgumentNullException.ThrowIfNull(capturedInputs);
        ArgumentNullException.ThrowIfNull(use);
        inputBanks = capturedInputs;
        this.use = use;
    }

    /// <summary>Captures input references and retains every nonempty array across all ordinary roles.</summary>
    /// <remarks>
    /// Execution must consume the input references returned by <see cref="GetInput"/> and retain this
    /// admission through execution and publication. Scalar, state and arena references are passed
    /// by value and must remain the same invocation arguments. Quarantine denies new admissions;
    /// this lease neither stops existing uses nor permits publication from a quarantined context.
    /// </remarks>
    public static WarpOrdinaryArrayAdmission Acquire(uint[][] inputs, uint[] scalars, uint[] state, uint[] arena) =>
        WarpOrdinaryArrayRegistry.AcquireOrdinary(inputs, scalars, state, arena);

    /// <summary>Returns the exact input array captured at admission without exposing the outer snapshot.</summary>
    public uint[] GetInput(int index)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, inputBanks.Length);
        return inputBanks[index];
    }

    /// <summary>Releases this ordinary use once.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) { use.Dispose(); }
    }
}
