using System.Collections.Immutable;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledRemoteSourceAdapter
{
    private ImmutableArray<WarpCoreCLRPreparedCleanup> PrepareCleanup(WarpCompiledPausedCensus census,
        WarpCompiledWorkerTicket ticket, uint token)
    {
        var cleanup = ImmutableArray.CreateBuilder<WarpCoreCLRPreparedCleanup>(census.Grant is null ? 3 : 2);
        uint word = checked(context.Scheduler + WarpPortableSchedulerLayout.ControllerOwner);
        if (census.Grant is null)
        {
            cleanup.Add(new(compareExchange, WarpCoreCLRCleanupPurpose.AcquireFreeController, 0, Banks([word, 0, token]), []));
        }
        uint[] state = context.RemoteSourceState(ticket);
        WarpCompiledSourceLocation location = context.Plan.Location(state);
        cleanup.Add(new(disposeCensus, WarpCoreCLRCleanupPurpose.StoppedFault, 0,
            Banks([context.Scheduler, token, census.Dispatch, census.Epoch, ticket.Worker, ticket.Generation,
                checked((uint)location.Function), location.CilOffset, location.Instruction, state[WarpLogicalMachineLayout.LogicalDepthOffset]]), []));
        cleanup.Add(new(compareExchange, WarpCoreCLRCleanupPurpose.ReleaseCapturedController, token, Banks([word, token, 0]), []));
        return cleanup.MoveToImmutable();
    }

    private async Task ExecuteCleanupAsync(WarpCoreCLRPreparedCleanup binding, WarpCompiledRemotePreparedRun run,
        WarpCoreCLRQuarantineRecovery recovery)
    {
        uint[][] inputs = CleanupInputs(binding.Purpose, run);
        uint[] state = binding.Lease.Layout.CreateInitialState(32, 100_000_000);
        for (int attempt = 0; attempt < 1_000_000; attempt++)
        {
            await binding.Lease.ExecuteQuarantineQuantumAsync(inputs, [], 0, state, 32,
                Math.Max(4096, binding.Lease.Layout.MaximumBlockCost), context.Arena, recovery, CancellationToken.None).ConfigureAwait(false);
            if (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Completed) { return; }
        }
        throw new InvalidOperationException("Admitted generated stopped-census cleanup exhausted its finite operational budget.");
    }

    private uint[][] CleanupInputs(WarpCoreCLRCleanupPurpose purpose, WarpCompiledRemotePreparedRun run)
    {
        uint word = checked(context.Scheduler + WarpPortableSchedulerLayout.ControllerOwner);
        uint token = run.Cleanup.First(binding => binding.Purpose == WarpCoreCLRCleanupPurpose.ReleaseCapturedController).ExpectedResult;
        return purpose switch
        {
            WarpCoreCLRCleanupPurpose.AcquireFreeController => Banks([word, 0, token]),
            WarpCoreCLRCleanupPurpose.ReleaseCapturedController => Banks([word, token, 0]),
            WarpCoreCLRCleanupPurpose.StoppedFault => CensusInputs(run, token),
            _ => throw new InvalidOperationException("The source adapter did not admit this cleanup purpose."),
        };
    }

    private uint[][] CensusInputs(WarpCompiledRemotePreparedRun run, uint token)
    {
        uint[] state = context.RemoteSourceState(run.Ticket);
        WarpCompiledSourceLocation location = context.Plan.Location(state);
        return Banks([context.Scheduler, token, run.Census.Dispatch, run.Census.Epoch, run.Ticket.Worker, run.Ticket.Generation,
            checked((uint)location.Function), location.CilOffset, location.Instruction, state[WarpLogicalMachineLayout.LogicalDepthOffset]]);
    }

    private static uint[][] Banks(ReadOnlySpan<uint> words)
    {
        uint[][] inputs = new uint[words.Length][];
        for (int index = 0; index < inputs.Length; index++) { inputs[index] = [words[index]]; }
        return inputs;
    }
}
