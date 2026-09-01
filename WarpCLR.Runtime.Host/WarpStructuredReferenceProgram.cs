using System.Collections.ObjectModel;

namespace WarpCLR.Runtime.Host;

public sealed class WarpStructuredReferenceStage
{
    private readonly Action<int> body;

    public WarpStructuredReferenceStage(
        string identity,
        int workItemCount,
        Action<int> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentOutOfRangeException.ThrowIfNegative(workItemCount);
        ArgumentNullException.ThrowIfNull(body);

        Identity = identity;
        WorkItemCount = workItemCount;
        this.body = body;
    }

    public string Identity { get; }

    public int WorkItemCount { get; }

    internal void Execute(int workItem) => body(workItem);
}

public sealed class WarpStructuredReferenceProgram
{
    private readonly ReadOnlyCollection<WarpStructuredReferenceStage> stages;

    public WarpStructuredReferenceProgram(
        IEnumerable<WarpStructuredReferenceStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);

        WarpStructuredReferenceStage[] stageArray = stages.ToArray();
        if (stageArray.Length == 0)
        {
            throw new ArgumentException(
                "A structured reference program requires a stage.",
                nameof(stages));
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (WarpStructuredReferenceStage? stage in stageArray)
        {
            ArgumentNullException.ThrowIfNull(stage);
            if (!identities.Add(stage.Identity))
            {
                throw new ArgumentException(
                    $"Structured stage '{stage.Identity}' is not unique.",
                    nameof(stages));
            }
        }

        this.stages = Array.AsReadOnly(stageArray);
    }

    public IReadOnlyList<WarpStructuredReferenceStage> Stages => stages;
}
