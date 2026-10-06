using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerProcess
{
    // Ordinary binding provenance only. This issuer never grants a Root Source purpose.
    private static readonly object InputBindingIssuer = new();

    internal static void ValidateInputBindingIssuer(object issuer)
    {
        if (!ReferenceEquals(issuer, InputBindingIssuer))
        { throw new InvalidOperationException("The private ordinary input-binding issuer is required."); }
    }

    private WarpCoreCLRInputBinding CreateInputBinding(uint tag, byte[] identity, WarpCoreCLRTransferAdmission.Lease retention,
        uint[][] capturedInputs, uint[] scalars) => new(InputBindingIssuer, this, tag, identity, retention, capturedInputs, scalars);

    private static uint[][] CaptureOrdinaryInputs(WarpOrdinaryArrayAdmission admission, int count)
    {
        var captured = new uint[count][];
        for (int index = 0; index < count; index++) { captured[index] = admission.GetInput(index); }
        return captured;
    }

    private static (WarpOrdinaryArrayAdmission Admission, uint[][] States) AdmitBoundBatch(
        WarpCoreCLRInputBinding.BatchUse inputUse, uint[][] states)
    {
        ArgumentNullException.ThrowIfNull(states);
        uint[][] capturedStates = (uint[][])states.Clone();
        int inputCount = inputUse.InputCount;
        var combined = new uint[checked(inputCount + capturedStates.Length)][];
        for (int index = 0; index < inputCount; index++) { combined[index] = inputUse.GetInput(index); }
        capturedStates.CopyTo(combined, inputCount);
        WarpOrdinaryArrayAdmission admission = WarpOrdinaryArrayAdmission.Acquire(combined, inputUse.Scalars, [], []);
        try
        {
            // Every later mutable target comes from the same admitted private snapshot.
            for (int index = 0; index < capturedStates.Length; index++) { capturedStates[index] = admission.GetInput(inputCount + index); }
            return (admission, capturedStates);
        }
        catch { admission.Dispose(); throw; }
    }
}
