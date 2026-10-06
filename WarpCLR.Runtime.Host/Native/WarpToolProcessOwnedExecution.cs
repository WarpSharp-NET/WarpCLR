namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpToolProcessOwnedExecution : IDisposable
{
    internal const string SemanticId = "warp.tool-process/actual-task-owned-cancellation-sources-sealed-deferred-resource-retirement/0.1";
    private readonly Lock gate = new();
    private readonly CancellationTokenSource stopReaders = new();
    private readonly CancellationTokenSource stopRootWait = new();
    private readonly CancellationTokenRegistration? readerProbe;
    private readonly Action retiredCallback;
    private readonly HashSet<Task> tasks = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Task> pending = new(ReferenceEqualityComparer.Instance);
    private readonly List<Exception> observedFaults = [];
    private readonly TaskCompletionSource retirement = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource cleanupDeadline = new();
    private bool cleanupStarted;
    private Task? readerCancellation;
    private Task? rootCancellation;
    private bool sealedRegistration;
    private bool disposalRequested;
    private bool retiring;
    private bool resourcesRetired;

    internal WarpToolProcessOwnedExecution(Action retiredCallback, Action? beforeReaderCancellation)
    {
        this.retiredCallback = retiredCallback;
        if (beforeReaderCancellation is not null) { readerProbe = stopReaders.Token.Register(beforeReaderCancellation); }
    }

    internal CancellationToken ReaderToken => stopReaders.Token;
    internal CancellationToken RootWaitToken => stopRootWait.Token;
    internal CancellationToken CleanupToken => cleanupStarted ? cleanupDeadline.Token : throw new InvalidOperationException("The original cleanup deadline has not started.");
    internal Task Retirement => retirement.Task;
    internal bool ResourcesRetired { get { lock (gate) { return resourcesRetired; } } }
    internal IReadOnlyList<Task> RetainedTasks { get { lock (gate) { return tasks.ToArray(); } } }
    internal IReadOnlyList<Exception> ObservedFaults { get { lock (gate) { return observedFaults.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray(); } } }
    internal Task ReaderCancellationTask => readerCancellation ?? throw new InvalidOperationException("Reader cancellation has not started.");

    internal CancellationToken StartCleanupDeadline(TimeSpan timeout)
    {
        lock (gate)
        {
            if (sealedRegistration || retiring || cleanupStarted)
            { throw new InvalidOperationException("The execution owns exactly one original cleanup deadline."); }
            cleanupStarted = true;
            cleanupDeadline.CancelAfter(timeout);
            return cleanupDeadline.Token;
        }
    }

    internal Task CancelReadersAsync()
    {
        Task cancellation;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(retiring, this);
            cancellation = readerCancellation ??= stopReaders.CancelAsync();
        }
        return Own(cancellation);
    }

    internal Task CancelRootWaitAsync()
    {
        Task cancellation;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(retiring, this);
            cancellation = rootCancellation ??= stopRootWait.CancelAsync();
        }
        return Own(cancellation);
    }

    internal TTask Own<TTask>(TTask task) where TTask : Task
    {
        ArgumentNullException.ThrowIfNull(task);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(retiring, this);
            if (!tasks.Add(task)) { return task; }
            pending.Add(task);
        }
        if (task.IsCompleted) { ObserveCompletion(task); }
        else { task.ConfigureAwait(false).GetAwaiter().OnCompleted(() => ObserveCompletion(task)); }
        return task;
    }

    // Disposal requests retirement; pending actual tasks retain their resources.
    public void Dispose()
    {
        bool retire;
        lock (gate)
        {
            disposalRequested = true;
            retire = BeginRetirementIfReady();
        }
        if (retire) { RetireResources(); }
    }

    internal void Seal()
    {
        lock (gate) { sealedRegistration = true; }
        Dispose();
    }

    private void ObserveCompletion(Task task)
    {
        // The callback observes the actual task. It does not create an unowned observer Task.
        bool retire;
        lock (gate)
        {
            if (task.Exception is { } fault) { observedFaults.AddRange(fault.Flatten().InnerExceptions); }
            pending.Remove(task);
            retire = BeginRetirementIfReady();
        }
        if (retire) { RetireResources(); }
    }

    private bool BeginRetirementIfReady()
    {
        if (!sealedRegistration || !disposalRequested || retiring || pending.Count != 0) { return false; }
        retiring = true;
        return true;
    }

    private void RetireResources()
    {
        var failures = new List<Exception>();
        if (readerProbe is { } probe) { ReleaseResource(probe, failures); }
        ReleaseResource(stopReaders, failures);
        ReleaseResource(stopRootWait, failures);
        ReleaseResource(cleanupDeadline, failures);
        lock (gate) { observedFaults.AddRange(failures); resourcesRetired = true; }
        if (failures.Count == 0) { retirement.TrySetResult(); }
        else
        {
            retirement.TrySetException(failures);
            _ = retirement.Task.Exception;
        }
        retiredCallback();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Deferred completion callbacks must attempt every resource release and retain every failure in the observed retirement Task; an escaping callback would strand the remaining owned resources.")]
    private static void ReleaseResource(IDisposable resource, List<Exception> failures)
    {
        try { resource.Dispose(); }
        catch (Exception failure) { failures.Add(failure); }
    }
}
