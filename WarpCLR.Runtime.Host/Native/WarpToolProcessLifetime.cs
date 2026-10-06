using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpToolProcessLifetime : IDisposable
{
    private readonly Process process;
    private readonly Lock gate = new();
    private readonly TaskCompletionSource retirement = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WarpToolProcessOwnedExecution? execution;
    private Task<string>? termination;
    private bool disposeRequested;
    private bool processRetiring;
    private Exception? retirementFailure;

    public WarpToolProcessLifetime(Process process)
    {
        this.process = process;
    }

    public Process Process => process;

    internal Action? BeforeTermination { get; init; }
    internal Action? BeforeReaderCancellation { get; init; }
    internal Task Retirement => retirement.Task;
    internal WarpToolProcessOwnedExecution RetainedExecution => execution ?? throw new InvalidOperationException("No tool execution owns this process yet.");

    internal WarpToolProcessOwnedExecution BeginExecution()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposeRequested, this);
            if (execution is not null) { throw new InvalidOperationException("The process already has its exact execution owner."); }
            execution = new(OnExecutionRetired, BeforeReaderCancellation);
            return execution;
        }
    }

    public Task<string> TerminateAsync()
    {
        Task<string> actual;
        lock (gate)
        {
            if (termination is not null) { return termination; }
            ObjectDisposedException.ThrowIf(processRetiring, this);
            termination = actual = Task.Run(TerminateCore);
        }
        actual.ConfigureAwait(false).GetAwaiter().OnCompleted(() => OnTerminationCompleted(actual));
        return actual;
    }

    private string TerminateCore()
    {
        try
        {
            BeforeTermination?.Invoke();
            if (process.HasExited) { return "Root already exited; remaining descendants cannot be identified from this root."; }
            process.Kill(entireProcessTree: true);
            return "Tree termination requested; descendant exit is not confirmed.";
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException or AggregateException)
        { return "Tree termination failed: " + error; }
    }

    private void OnTerminationCompleted(Task actual)
    {
        _ = actual.Exception;
        _ = TryRetireProcess();
    }

    public void Dispose()
    {
        WarpToolProcessOwnedExecution? retainedExecution;
        lock (gate) { disposeRequested = true; retainedExecution = execution; }
        // Request retirement outside the gate; only the producer can seal registration.
        retainedExecution?.Dispose();
        Exception? failure = TryRetireProcess();
        if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    private void OnExecutionRetired() => _ = TryRetireProcess();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Deferred process disposal must record any failure in the observed retirement Task; synchronous Dispose rethrows the same failure while completion callbacks cannot throw unobserved exceptions.")]
    private Exception? TryRetireProcess()
    {
        lock (gate)
        {
            if (processRetiring) { return retirementFailure; }
            if (!disposeRequested || (termination is not null && !termination.IsCompleted) ||
                (execution is not null && !execution.ResourcesRetired)) { return null; }
            processRetiring = true;
        }
        try { process.Dispose(); retirement.TrySetResult(); return null; }
        catch (Exception failure)
        {
            lock (gate) { retirementFailure = failure; }
            retirement.TrySetException(failure);
            _ = retirement.Task.Exception;
            return failure;
        }
    }
}
