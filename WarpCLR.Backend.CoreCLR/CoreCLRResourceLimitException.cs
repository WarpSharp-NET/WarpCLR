using System.Diagnostics.CodeAnalysis;

namespace WarpCLR.Backend.CoreCLR;

[SuppressMessage("Design", "CA1032:Implement standard exception constructors",
    Justification = "Resource faults require a structured kind, limit, and worker diagnostics.")]
[SuppressMessage("Roslynator", "RCS1194:Implement exception constructors",
    Justification = "Resource faults require a structured kind, limit, and worker diagnostics.")]
public sealed class CoreCLRResourceLimitException : Exception
{
    public CoreCLRResourceLimitException(
        CoreCLRResourceLimitKind kind,
        long limit,
        long stepsConsumed = 0,
        int depth = 0)
        : base(kind switch
        {
            CoreCLRResourceLimitKind.StepLimit =>
                $"The logical worker exceeded its portable execution budget of {limit} steps.",
            CoreCLRResourceLimitKind.CallDepth =>
                $"The logical worker exceeded its portable call depth of {limit} frames.",
            CoreCLRResourceLimitKind.StackExhausted =>
                "The native CLR worker stack cannot safely admit another compiled frame.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        })
    {
        Kind = kind;
        Limit = limit;
        StepsConsumed = stepsConsumed;
        Depth = depth;
    }

    public CoreCLRResourceLimitKind Kind { get; }

    public long Limit { get; }

    public long StepsConsumed { get; }

    public int Depth { get; }
}
