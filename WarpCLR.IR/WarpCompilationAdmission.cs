using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public static class WarpCompilationAdmission
{
    public const int MaximumAssemblyBytes = 64 * 1024 * 1024;
    public const int MaximumSourceBytes = 64 * 1024 * 1024;
    public const int MaximumManifestEntries = 256;
    public const int MaximumManifestBytes = 4 * 1024 * 1024;
    public const int MaximumIdentityCharacters = 4096;
    public const int MaximumFunctionsPerEntry = 256;
    public const int MaximumBlocksPerEntry = 8192;
    public const int MaximumInstructionsPerEntry = 65536;
    public const int MaximumValueSlotsPerEntry = 131072;
    public const int MaximumOperandReferencesPerEntry = 1048576;
    public const int MaximumParametersPerBody = 1024;
    public const int MaximumLocalsPerBody = 4096;
    public const int MaximumEvaluationStackPerBody = 4096;
    public const int MaximumCilBytesPerBody = 1024 * 1024;
    public const int MaximumCilBytesPerEntry = 4 * 1024 * 1024;
    public const int MaximumVerifierWorkspaceSlotsPerEntry = 1048576;
    public const int MaximumModuleExpansionFactor = 16;

    public static void Validate(WarpControlFlowKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        Require(kernel.Name, WarpCompilationResourceKind.Functions, kernel.Functions.Count, MaximumFunctionsPerEntry);
        Require(kernel.Name, WarpCompilationResourceKind.Parameters,
            kernel.InputBufferCount + (long)kernel.ScalarArgumentCount, MaximumParametersPerBody);
        var usage = new WarpCompilationUsage(kernel.Name);
        usage.AddBody(kernel.Blocks, checked(kernel.ValueCount + (kernel.Execution?.Bodies[0].PrivateWordCount ?? 0)));
        foreach (WarpControlFlowFunction function in kernel.Functions)
        {
            Require(kernel.Name, WarpCompilationResourceKind.Parameters, function.ParameterCount, MaximumParametersPerBody);
            usage.AddBody(function.Blocks, checked(function.ValueCount + (kernel.Execution?.Bodies[function.Id + 1].PrivateWordCount ?? 0)));
        }
    }

    internal static void Require(string identity, WarpCompilationResourceKind resource, long requested, long limit)
    {
        if (requested > limit)
        {
            throw new WarpCompilationResourceException(identity, resource, requested, limit);
        }
    }

    internal static T[] Materialize<T>(IEnumerable<T> items, string identity, WarpCompilationResourceKind resource, int limit)
    {
        var result = new List<T>();
        foreach (T item in items)
        {
            Require(identity, resource, result.Count + 1L, limit);
            result.Add(item);
        }

        return result.ToArray();
    }

    internal static void ValidateDefinitions(string identity, ReadOnlyCollection<WarpBasicBlock> blocks, WarpControlFlowFunction[]? functions = null)
    {
        long definitions = 0;
        foreach (WarpBasicBlock block in blocks)
        {
            definitions += block.Parameters.Count + block.Instructions.Sum(instruction => (long)instruction.ResultWordCount);
            Require(identity, WarpCompilationResourceKind.ValueSlots, definitions, MaximumValueSlotsPerEntry);
        }

        var usage = new WarpCompilationUsage(identity);
        usage.AddBody(blocks, checked((int)definitions));
        if (functions is null)
        {
            return;
        }

        foreach (WarpControlFlowFunction function in functions)
        {
            usage.AddBody(function.Blocks, function.ValueCount);
        }
    }
}
