using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCompiledServiceContinuation
{
    internal WarpCompiledServiceContinuation(WarpCompiledWordService service, uint[][] inputs, uint[] state)
    {
        Service = service;
        Inputs = inputs;
        State = state;
    }

    internal WarpCompiledWordService Service { get; }
    internal uint[][] Inputs { get; }
    internal uint[] State { get; }
    internal uint Status => State[WarpLogicalMachineLayout.StatusOffset];
    internal uint Result => Status == WarpLogicalMachineLayout.Completed
        ? State[WarpLogicalMachineLayout.ResultOffset]
        : throw new InvalidOperationException("The compiled service has not completed.");
    internal uint Quanta { get; private set; }

    internal bool Resume(uint[] arena, int quantum)
    {
        if (Status != WarpLogicalMachineLayout.Runnable)
        {
            throw new InvalidOperationException("A completed or faulted service continuation cannot run again.");
        }
        Quanta = checked(Quanta + 1);
        Service.Resume(this, arena, quantum);
        if (Status == WarpLogicalMachineLayout.Faulted)
        {
            throw new InvalidOperationException("An admitted generated runtime service faulted.");
        }
        return Status == WarpLogicalMachineLayout.Completed;
    }
}
