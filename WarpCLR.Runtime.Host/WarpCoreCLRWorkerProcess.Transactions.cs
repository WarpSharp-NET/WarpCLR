using System.Text;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerProcess
{
    internal async Task ExecuteManagedQuantumAsync(WarpLogicalMachineLayout layout, uint[][] inputs, uint[] scalars, int worker,
        uint[] state, int depth, int quantum, uint[] arena, CancellationToken cancellationToken) =>
        await ExecuteCoreAsync(layout, inputs, scalars, worker, state, depth, quantum, arena, null, null, null, false, cancellationToken).ConfigureAwait(false);

    internal Task ExecuteOwnedManagedQuantumAsync(WarpLogicalMachineLayout layout, uint[][] inputs, uint[] scalars, int worker,
        uint[] state, int depth, int quantum, uint[] arena, WarpCoreCLRCommandAdmission admission, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(layout, inputs, scalars, worker, state, depth, quantum, arena, admission, null, null, false, cancellationToken);

    internal Task ExecuteQuarantineQuantumAsync(WarpLogicalMachineLayout layout, uint[][] inputs, uint[] scalars, int worker,
        uint[] state, int depth, int quantum, uint[] arena, WarpCoreCLRQuarantineRecovery recovery, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(layout, inputs, scalars, worker, state, depth, quantum, arena, null, recovery, null, false, cancellationToken);

    internal Task ExecuteControllerQuantumAsync(WarpLogicalMachineLayout layout, WarpCoreCLRControllerAdmission admission,
        int quantum, CancellationToken cancellationToken) => ExecuteCoreAsync(layout,
        [[admission.Preparation.Scheduler], [admission.Preparation.Controller]], [], 0, admission.Preparation.State,
        admission.Preparation.Depth, quantum, admission.Preparation.Arena, null, null, admission, false, cancellationToken);

    internal Task ExecuteControllerReleaseAsync(WarpLogicalMachineLayout layout, WarpCoreCLRControllerAdmission admission,
        uint[][] inputs, CancellationToken cancellationToken) => ExecuteCoreAsync(layout, inputs, [], 0,
        admission.ReleaseState, 32, Math.Max(layout.MaximumBlockCost, 4096), admission.Preparation.Arena,
        null, null, admission, true, cancellationToken);

    private async Task ExecuteCoreAsync(WarpLogicalMachineLayout layout, uint[][] inputs, uint[] scalars, int worker,
        uint[] state, int depth, int quantum, uint[] arena, WarpCoreCLRCommandAdmission? admission,
        WarpCoreCLRQuarantineRecovery? recovery, WarpCoreCLRControllerAdmission? controller, bool controllerRelease, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
        deadline.CancelAfter(options.QuantumTimeout);
        bool started = false;
        WarpCoreCLRAsyncGate.Lease? transaction = null;
        WarpCoreCLRWordTransaction.Lease? buffers = null;
        WarpCoreCLRTransferAdmission.Lease? storage = null;
        WarpCoreCLRStoppedCommands.Command? command = null;
        try
        {
            buffers = recovery is null ? await WarpCoreCLRWordTransaction.AcquireAsync(state, arena, deadline.Token).ConfigureAwait(false) :
                await WarpCoreCLRWordTransaction.AcquireQuarantinedAsync(state, arena, recovery, deadline.Token).ConfigureAwait(false);
            recovery?.Validate(this, IrHash, inputs, scalars, state, arena, RegistryAuthority);
            ValidateAliases(inputs, scalars, state, arena);
            transaction = await transactions.AcquireAsync(deadline.Token).ConfigureAwait(false);
            long bytes = WarpCoreCLRWorkerWords.Estimate(inputs, scalars, state, arena);
            storage = await WarpCoreCLRTransferAdmission.AcquireAsync(bytes * 8 + 4096, deadline.Token).ConfigureAwait(false);
            byte[] request = WarpCoreCLRWorkerWords.Request(inputs, scalars, worker, state, depth, quantum, arena);
            (ulong current, WarpCoreCLRStoppedCommands.Command startedCommand) = BeginWordCommand(request, state, arena, admission, inputs, scalars,
                controller, controllerRelease, recovery, deadline.Token);
            command = startedCommand;
            started = true;
            await WarpCoreCLRWorkerProtocol.WriteAsync(process.StandardInput.BaseStream, key, WarpCoreCLRWorkerProtocol.Execute, current, request, deadline.Token).ConfigureAwait(false);
            WarpCoreCLRWorkerProtocol.Frame frame = await WarpCoreCLRWorkerProtocol.ReadAsync(process.StandardOutput.BaseStream, key, current, deadline.Token).ConfigureAwait(false);
            if (frame.Kind != WarpCoreCLRWorkerProtocol.Executed) { throw new InvalidDataException("Worker quantum result missing."); }
            (uint[] returnedState, uint[] returnedArena) = WarpCoreCLRWorkerWords.ReadResponse(frame.Payload, request, state.Length, arena.Length);
            CommitResponse(layout, state, arena, depth, returnedState, returnedArena, command, recovery, frame.Payload, deadline.Token);
        }
        catch (Exception error)
        {
            Exception cause = error;
            if (started)
            {
                cause = await StopOwnedCommandAsync(error, state, arena, command).ConfigureAwait(false);
            }
            throw WordFailure(cause, started ? command : null, cancellationToken);
        }
        finally { storage?.Dispose(); transaction?.Dispose(); buffers?.Dispose(); }
    }

    private async Task<Exception> StopOwnedCommandAsync(Exception failure, uint[] state, uint[] arena,
        WarpCoreCLRStoppedCommands.Command? command)
    {
        WarpCoreCLRWordTransaction.Quarantine(state, arena);
        Exception cause = failure;
        try { await DisposeAsync().ConfigureAwait(false); }
        catch (Exception cleanupFailure) when (cleanupFailure is WarpHostException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { cause = new AggregateException(failure, cleanupFailure); }
        if (command is not null && HasSuccessfulContainment && (command.Admission is not null || command.Controller is not null)) { ConfirmStopped(command); }
        return cause;
    }

    private static Exception WordFailure(Exception cause, WarpCoreCLRStoppedCommands.Command? command, CancellationToken cancellationToken)
    {
        Exception failure = cancellationToken.IsCancellationRequested ? new OperationCanceledException("The owned native transaction was stopped.", cause, cancellationToken) :
            new WarpHostException("WRPCORECLR3002", "The bounded CoreCLR word transaction failed; no returned word buffer was committed.", cause);
        if (command is not null) { WarpCoreCLRStoppedCommands.AttachFailure(failure, command); }
        return failure;
    }

    private (ulong Sequence, WarpCoreCLRStoppedCommands.Command Command) BeginWordCommand(byte[] request, uint[] state,
        uint[] arena, WarpCoreCLRCommandAdmission? admission, uint[][] inputs, uint[] scalars,
        WarpCoreCLRControllerAdmission? controller, bool controllerRelease, WarpCoreCLRQuarantineRecovery? recovery, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (faulted) { throw new WarpHostException("WRPCORECLR3002", "The CoreCLR worker is quarantined."); }
            if (sequence == ulong.MaxValue) { throw new WarpHostException("WRPCORECLR3002", "The CoreCLR transaction namespace exhausted."); }
            cancellationToken.ThrowIfCancellationRequested();
            ulong current = sequence + 1;
            WarpCoreCLRStoppedCommands.Command command = Started(this, current, request, state, arena, IrHash,
                admission, inputs, scalars, controller, controllerRelease, recovery);
            sequence = current;
            return (current, command);
        }
    }

    private void CommitResponse(WarpLogicalMachineLayout layout, uint[] state, uint[] arena, int depth,
        uint[] returnedState, uint[] returnedArena, WarpCoreCLRStoppedCommands.Command command,
        WarpCoreCLRQuarantineRecovery? recovery, byte[] response, CancellationToken cancellationToken)
    {
        ValidateResult(layout, state, returnedState, depth);
        if (command.Controller is not null)
        { probes?.BeforeControllerResultPublication?.Invoke(Array.AsReadOnly(returnedState), Array.AsReadOnly(returnedArena)); }
        probes?.BeforeResultPublication?.Invoke();
        lock (sync)
        {
            if (faulted) { throw new WarpHostException("WRPCORECLR3002", "A stopping CoreCLR worker cannot publish returned words."); }
            cancellationToken.ThrowIfCancellationRequested();
            recovery?.ObserveGeneratedCompletion(state, returnedState, RegistryAuthority);
            (WarpCoreCLRGenerationTransition? transition, WarpCoreCLRControllerCheckpoint? checkpoint) = ObserveGenerationCommit(command, returnedState, returnedArena, response);
            returnedState.CopyTo(state, 0); returnedArena.CopyTo(arena, 0);
            Committed(command, transition, checkpoint);
        }
    }

    private async Task CompilePlanAsync(byte[] plan, string irHash, WarpCoreCLRWorkerTestHooks? probes, CancellationToken cancellationToken)
    {
        byte[] request = new byte[checked(plan.Length + 64)];
        Encoding.ASCII.GetBytes(irHash).CopyTo(request, 0); plan.CopyTo(request, 64);
        await WarpCoreCLRWorkerProtocol.WriteAsync(process.StandardInput.BaseStream, key, WarpCoreCLRWorkerProtocol.Compile, 1, request, cancellationToken).ConfigureAwait(false);
        WarpCoreCLRWorkerProtocol.Frame frame = await WarpCoreCLRWorkerProtocol.ReadAsync(process.StandardOutput.BaseStream, key, 1, cancellationToken).ConfigureAwait(false);
        if (frame.Kind != 8 || frame.Payload.Length != 0) { throw new InvalidDataException("Worker plan admission boundary missing."); }
        probes?.PlanAdmitted?.Invoke();
        frame = await WarpCoreCLRWorkerProtocol.ReadAsync(process.StandardOutput.BaseStream, key, 1, cancellationToken).ConfigureAwait(false);
        if (frame.Kind == 7 && frame.Payload.Length == 4)
        {
            int descendant = BitConverter.ToInt32(frame.Payload);
            RegisterInheritedProcess(descendant, cancellationToken);
            frame = await WarpCoreCLRWorkerProtocol.ReadAsync(process.StandardOutput.BaseStream, key, 1, cancellationToken).ConfigureAwait(false);
        }
        if (frame.Kind != WarpCoreCLRWorkerProtocol.JitEntering || frame.Payload.Length != 16) { throw new InvalidDataException("Worker JIT boundary missing."); }
        Guid module = new(frame.Payload); probes?.JitEntering?.Invoke(module);
        frame = await WarpCoreCLRWorkerProtocol.ReadAsync(process.StandardOutput.BaseStream, key, 1, cancellationToken).ConfigureAwait(false);
        if (frame.Kind != WarpCoreCLRWorkerProtocol.Compiled || frame.Payload.Length != 17 || frame.Payload[16] != 1 ||
            new Guid(frame.Payload.AsSpan(0, 16)) != module)
        { throw new InvalidDataException("Worker native collectible module identity is invalid."); }
        CompiledModule = module; IsCollectible = true;
    }

    private void RegisterInheritedProcess(int descendant, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (faulted) { throw new WarpHostException("WRPCORECLR3002", "A stopping CoreCLR worker cannot admit inherited-process handles."); }
            cancellationToken.ThrowIfCancellationRequested();
            containment.RegisterDescendant(descendant);
        }
        probes?.InheritedChild?.Invoke(descendant);
    }

    private static void ValidateAliases(uint[][] inputs, uint[] scalars, uint[] state, uint[] arena)
    {
        if (scalars.Length != 0 && (ReferenceEquals(scalars, state) || ReferenceEquals(scalars, arena)) ||
            inputs.Any(input => input.Length != 0 && (ReferenceEquals(input, state) || ReferenceEquals(input, arena))))
        { throw new ArgumentException("Read-only invocation words must be disjoint from mutable state and arena.", nameof(inputs)); }
    }

    private static void ValidateResult(WarpLogicalMachineLayout layout, uint[] before, uint[] after, int depth)
    {
        WarpNativeMachineLaunch.ValidateReturnedStates(layout, after, 1, depth);
        if (after.Length != layout.GetStateWords(depth) || !layout.HasValidRuntimeHeader(after) ||
            after[WarpLogicalMachineLayout.StatusOffset] > WarpLogicalMachineLayout.Faulted || after[WarpLogicalMachineLayout.DepthOffset] == 0 ||
            after[WarpLogicalMachineLayout.DepthOffset] > layout.GetPhysicalFrameCapacity(depth) ||
            after[WarpLogicalMachineLayout.OwnerContextOffset] != before[WarpLogicalMachineLayout.OwnerContextOffset] ||
            after[WarpLogicalMachineLayout.NextActivationOffset] < before[WarpLogicalMachineLayout.NextActivationOffset])
        { throw new InvalidDataException("Worker returned an invalid logical state or replaced its frame owner identity."); }
    }
}
