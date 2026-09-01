using System.Runtime.ExceptionServices;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

public sealed class WarpStructuredExecutionException : Exception
{
    internal WarpStructuredExecutionException(
        string stageIdentity,
        int workItemIndex,
        Exception innerException)
        : base(
            $"Structured stage '{stageIdentity}' failed at work item {workItemIndex}.",
            innerException)
    {
        StageIdentity = stageIdentity;
        WorkItemIndex = workItemIndex;
    }

    public string Code => "WRPHOST1100";

    public string StageIdentity { get; }

    public int WorkItemIndex { get; }
}

public sealed class WarpStructuredReferenceSession
{
    public WarpStructuredReferenceSession(
        WarpBackendKind backend,
        WarpDevelopmentExecutionMode mode,
        int maximumConcurrency = -1)
    {
        if (!WarpBackendCatalog.Required.Contains(backend))
        {
            throw Error("WRPHOST1002", $"Backend '{backend}' is not registered.");
        }

        if (maximumConcurrency == 0 || maximumConcurrency < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        }

        if (mode == WarpDevelopmentExecutionMode.CoreClrReference &&
            backend != WarpBackendKind.CpuReference)
        {
            throw Error(
                "WRPHOST1101",
                "CoreCLR reference execution requires the CPU reference backend.");
        }

        if (mode is not WarpDevelopmentExecutionMode.CoreClrReference and
            not WarpDevelopmentExecutionMode.SemanticEmulation)
        {
            throw Error("WRPHOST1003", $"Development execution mode '{mode}' is not registered.");
        }

        Backend = backend;
        Mode = mode;
        MaximumConcurrency = maximumConcurrency;
    }

    public WarpBackendKind Backend { get; }

    public WarpDevelopmentExecutionMode Mode { get; }

    public int MaximumConcurrency { get; }

    public void Execute(
        WarpStructuredReferenceProgram program,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);

        foreach (WarpStructuredReferenceStage stage in program.Stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteStage(stage);
        }
    }

    private void ExecuteStage(WarpStructuredReferenceStage stage)
    {
        if (stage.WorkItemCount == 0)
        {
            return;
        }

        var failures = new ExceptionDispatchInfo?[stage.WorkItemCount];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaximumConcurrency,
        };

        Parallel.For(
            0,
            stage.WorkItemCount,
            options,
            workItem =>
            {
                try
                {
                    stage.Execute(workItem);
                }
                catch (Exception exception)
                {
                    failures[workItem] = ExceptionDispatchInfo.Capture(exception);
                }
            });

        for (int workItem = 0; workItem < failures.Length; workItem++)
        {
            if (failures[workItem] is not { } failure)
            {
                continue;
            }

            throw new WarpStructuredExecutionException(
                stage.Identity,
                workItem,
                failure.SourceException);
        }
    }

    private static WarpHostException Error(string code, string message) =>
        new(code, message);
}
