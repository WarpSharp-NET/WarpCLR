using System.Security.Cryptography;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerProcess
{
    private uint nextInputTag;

    internal async Task<WarpCoreCLRInputBinding> BindInputsAsync(uint[][] inputs, uint[] scalars, CancellationToken cancellationToken)
    {
        using WarpOrdinaryArrayAdmission ordinary = WarpOrdinaryArrayAdmission.Acquire(inputs, scalars, [], []);
        uint[][] capturedInputs = CaptureOrdinaryInputs(ordinary, inputs.Length);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
        deadline.CancelAfter(options.QuantumTimeout);
        using WarpCoreCLRAsyncGate.Lease transaction = await transactions.AcquireAsync(deadline.Token).ConfigureAwait(false);
        if (IsFaulted || nextInputTag == uint.MaxValue) { throw new WarpHostException("WRPCORECLR3002", "Immutable input binding cannot use a stopped worker or exhausted namespace."); }
        long bytes = WarpCoreCLRWorkerInputWords.Estimate(capturedInputs, scalars);
        WarpCoreCLRTransferAdmission.Lease retention = await WarpCoreCLRTransferAdmission.AcquireAsync(bytes * 10 + 4096, deadline.Token).ConfigureAwait(false);
        uint tag = ++nextInputTag;
        bool started = false;
        try
        {
            byte[] request = WarpCoreCLRWorkerInputWords.Write(tag, capturedInputs, scalars);
            deadline.Token.ThrowIfCancellationRequested(); started = true;
            WarpCoreCLRWorkerProtocol.Frame response = await ControlAsync(WarpCoreCLRWorkerProtocol.BindInputs, request, deadline.Token).ConfigureAwait(false);
            byte[] identity = SHA256.HashData(request);
            if (response.Kind != WarpCoreCLRWorkerProtocol.InputsBound ||
                !response.Payload.AsSpan().SequenceEqual(WarpCoreCLRWorkerInputWords.Reference(tag, identity)))
            { throw new InvalidDataException("Immutable input binding receipt changed the exact input identity."); }
            probes?.BeforeInputBindingPublication?.Invoke();
            lock (sync)
            {
                if (faulted) { throw new WarpHostException("WRPCORECLR3002", "A stopping CoreCLR worker cannot publish a new input binding."); }
                deadline.Token.ThrowIfCancellationRequested();
                retention.Retain(bytes * 2);
                return CreateInputBinding(tag, identity, retention, capturedInputs, scalars);
            }
        }
        catch
        {
            retention.Dispose(); if (started) { await DisposeAsync().ConfigureAwait(false); } throw;
        }
    }

    internal async Task UnbindInputsAsync(WarpCoreCLRInputBinding binding)
    {
        if (IsFaulted) { return; }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        deadline.CancelAfter(options.CleanupTimeout);
        try
        {
            using WarpCoreCLRAsyncGate.Lease transaction = await transactions.AcquireAsync(deadline.Token).ConfigureAwait(false);
            if (IsFaulted) { return; }
            byte[] request = WarpCoreCLRWorkerInputWords.Reference(binding.Tag, binding.Identity);
            WarpCoreCLRWorkerProtocol.Frame response = await ControlAsync(WarpCoreCLRWorkerProtocol.UnbindInputs, request, deadline.Token).ConfigureAwait(false);
            if (response.Kind != WarpCoreCLRWorkerProtocol.InputsUnbound || !response.Payload.AsSpan().SequenceEqual(request))
            { throw new InvalidDataException("Immutable input binding release changed its exact identity."); }
        }
        catch { await DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async Task<WarpCoreCLRWorkerProtocol.Frame> ControlAsync(ushort kind, byte[] request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sequence == ulong.MaxValue) { throw new WarpHostException("WRPCORECLR3002", "Worker command namespace exhausted."); }
        ulong current = ++sequence;
        await WarpCoreCLRWorkerProtocol.WriteAsync(process.StandardInput.BaseStream, key, kind, current, request, cancellationToken).ConfigureAwait(false);
        return await WarpCoreCLRWorkerProtocol.ReadAsync(process.StandardOutput.BaseStream, key, current, cancellationToken).ConfigureAwait(false);
    }
}
