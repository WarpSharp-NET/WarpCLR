using System.Runtime.CompilerServices;

namespace WarpCLR.Backend.CoreCLR;

public sealed class CoreCLRExecutionBudget
{
    private int activeInvocation;
    private int callDepth;
    private long remainingSteps;

    public CoreCLRExecutionBudget(
        long maximumSteps,
        int maximumCallDepth,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumSteps, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCallDepth, 1);

        MaximumSteps = maximumSteps;
        MaximumCallDepth = maximumCallDepth;
        CancellationToken = cancellationToken;
        remainingSteps = maximumSteps;
    }

    public long MaximumSteps { get; }

    public int MaximumCallDepth { get; }

    public CancellationToken CancellationToken { get; }

    public long StepsConsumed => MaximumSteps - Interlocked.Read(ref remainingSteps);

    public int CurrentCallDepth => Volatile.Read(ref callDepth);

    public void Reset()
    {
        BeginInvocation();
        try
        {
            if (callDepth != 0)
            {
                throw new InvalidOperationException("An execution budget can only reset with an empty logical stack.");
            }

            Volatile.Write(ref remainingSteps, MaximumSteps);
        }
        finally
        {
            EndInvocation();
        }
    }

    // These methods are called by the emitted assembly, which cannot access internals.
    public void CheckNativeStack()
    {
        CancellationToken.ThrowIfCancellationRequested();
        try
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
        }
        catch (InsufficientExecutionStackException)
        {
            throw new CoreCLRResourceLimitException(
                CoreCLRResourceLimitKind.StackExhausted, 0, StepsConsumed, callDepth);
        }
    }

    public void Charge(int steps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        CancellationToken.ThrowIfCancellationRequested();
        if (remainingSteps < steps)
        {
            throw new CoreCLRResourceLimitException(
                CoreCLRResourceLimitKind.StepLimit, MaximumSteps, StepsConsumed, callDepth);
        }

        Volatile.Write(ref remainingSteps, remainingSteps - steps);
    }

    public void EnterCall()
    {
        CancellationToken.ThrowIfCancellationRequested();
        if (callDepth >= MaximumCallDepth)
        {
            throw new CoreCLRResourceLimitException(
                CoreCLRResourceLimitKind.CallDepth, MaximumCallDepth, StepsConsumed, callDepth);
        }

        callDepth++;
    }

    public void ExitCall() => callDepth--;

    internal void BeginInvocation()
    {
        if (Interlocked.CompareExchange(ref activeInvocation, 1, 0) != 0)
        {
            throw new InvalidOperationException("An execution budget cannot be shared by concurrent logical workers.");
        }
    }

    internal void EndInvocation()
    {
        callDepth = 0;
        Volatile.Write(ref activeInvocation, 0);
    }
}
