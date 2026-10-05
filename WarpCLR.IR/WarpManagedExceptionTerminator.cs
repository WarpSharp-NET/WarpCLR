namespace WarpCLR.IR;

internal sealed class WarpManagedExceptionTerminator : WarpBlockTerminator
{
    internal WarpManagedExceptionTerminator(int context, int objectId, int generation, int resultWordCount = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(context);
        ArgumentOutOfRangeException.ThrowIfNegative(objectId);
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        ArgumentOutOfRangeException.ThrowIfNegative(resultWordCount);
        WarpCompilationAdmission.Require("<managed-exception>", WarpCompilationResourceKind.ValueSlots,
            resultWordCount, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
        Context = context;
        ObjectId = objectId;
        Generation = generation;
        ResultWordCount = resultWordCount;
    }

    internal int Context { get; }
    internal int ObjectId { get; }
    internal int Generation { get; }
    internal int ResultWordCount { get; }
}
