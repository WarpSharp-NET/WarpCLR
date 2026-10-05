using System.Collections.Immutable;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledRemoteSourceAdapter
{
    private async Task RecoverControllerFailureAsync(WarpHostException failure, WarpCompiledPausedCensus census,
        WarpCoreCLRControllerAdmission admission)
    {
        context.SuppressUnresolvedRemoteFailure();
        WarpCoreCLRStoppedCommands.Command? command = WarpCoreCLRStoppedCommands.FromFailure(failure);
        if (command is null || !ReferenceEquals(command.Controller, admission))
        { return; }
        WarpCoreCLRQuarantineRecovery recovery = WarpCoreCLRStoppedCommands.Mint(command, admission);
        context.StopRemoteControl(census, recovery, admission, registered.Select(item => item!).ToArray());
        foreach (WarpCoreCLRPreparedCleanup binding in recovery.Cleanup)
        { await ExecuteControllerCleanupAsync(binding, recovery).ConfigureAwait(false); }
        await recovery.AcknowledgeGeneratedAbortAsync(admission, CancellationToken.None).ConfigureAwait(false);
        context.FinishRemoteControllerDisposal(census, admission);
    }

    private ImmutableArray<WarpCoreCLRPreparedCleanup> PrepareControllerCleanup(WarpCompiledPausedCensus census,
        WarpCoreCLRControllerOperation operation, uint dispatch, uint epoch)
    {
        uint token = census.Grant!.Token;
        uint word = checked(context.Scheduler + WarpPortableSchedulerLayout.ControllerOwner);
        var bindings = ImmutableArray.CreateBuilder<WarpCoreCLRPreparedCleanup>();
        bindings.Add(new(disposeController!, WarpCoreCLRCleanupPurpose.StoppedController, 0,
            Banks([context.Scheduler, token, (uint)operation, census.Dispatch, census.Epoch, census.Dispatch, census.Epoch]), []));
        bindings.Add(new(compareExchange, WarpCoreCLRCleanupPurpose.ReleaseCapturedController, token, Banks([word, token, 0]), []));
        bindings.Add(new(publicationRelease!, WarpCoreCLRCleanupPurpose.PublishControllerRelease, token, Banks([word, token, 0]), []));
        if (dispatch != census.Dispatch || epoch != census.Epoch)
        {
            bindings.Add(new(disposeController!, WarpCoreCLRCleanupPurpose.StoppedController, 0,
                Banks([context.Scheduler, token, (uint)operation, census.Dispatch, census.Epoch, dispatch, epoch]), []));
        }
        return bindings.ToImmutable();
    }

    private async Task ExecuteControllerCleanupAsync(WarpCoreCLRPreparedCleanup binding, WarpCoreCLRQuarantineRecovery recovery)
    {
        uint[][] inputs = Banks(Enumerable.Range(0, binding.Lease.Layout.Kernel.InputBufferCount).Select(binding.InputWord).ToArray());
        uint[] state = binding.Lease.Layout.CreateInitialState(32, 100_000_000);
        for (int attempt = 0; attempt < 1_000_000; attempt++)
        {
            await binding.Lease.ExecuteQuarantineQuantumAsync(inputs, [], 0, state, 32,
                Math.Max(4096, binding.Lease.Layout.MaximumBlockCost), context.Arena, recovery, CancellationToken.None).ConfigureAwait(false);
            if (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Completed) { return; }
        }
        throw new InvalidOperationException("Admitted generated controller cleanup exhausted its finite operational budget.");
    }
}
