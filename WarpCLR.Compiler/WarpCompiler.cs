using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal sealed class WarpCompiler
{
    private readonly WarpIntegerMapLowerer lowerer;

    public WarpCompiler()
        : this(new WarpIntegerMapLowerer())
    {
    }

    public WarpCompiler(WarpIntegerMapLowerer lowerer)
    {
        ArgumentNullException.ThrowIfNull(lowerer);
        this.lowerer = lowerer;
    }

    public WarpCompilation Compile(
        WarpIntegerMapKernel verifiedKernel,
        IEnumerable<IWarpBackendCompiler> backendCompilers)
    {
        ArgumentNullException.ThrowIfNull(verifiedKernel);
        ArgumentNullException.ThrowIfNull(backendCompilers);

        Dictionary<WarpBackendKind, IWarpBackendCompiler> compilers = backendCompilers
            .ToDictionary(compiler => compiler.Backend);

        WarpBackendKind[] missing = WarpBackendCatalog.Required
            .Where(backend => !compilers.ContainsKey(backend))
            .ToArray();

        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                $"The compiler set is missing required backends: {string.Join(", ", missing)}.");
        }

        WarpBackendKind[] unsupported = compilers.Keys
            .Where(backend => !WarpBackendCatalog.Required.Contains(backend))
            .ToArray();

        if (unsupported.Length != 0)
        {
            throw new InvalidOperationException(
                $"The compiler set contains unregistered backends: {string.Join(", ", unsupported)}.");
        }

        ValidateBackendContracts(compilers);

        WarpControlFlowKernel kernel = lowerer.Lower(verifiedKernel);
        var artifacts = new Dictionary<WarpBackendKind, WarpBackendArtifact>();

        foreach (WarpBackendKind backend in WarpBackendCatalog.Required)
        {
            WarpBackendArtifact artifact = compilers[backend].Compile(kernel);
            if (artifact.Backend != backend)
            {
                throw new InvalidOperationException(
                    $"The {backend} compiler returned an artifact for {artifact.Backend}.");
            }

            WarpArtifactFormat requiredFormat = WarpArtifactFormatCatalog.ForBackend(backend);
            if (artifact.Format != requiredFormat)
            {
                throw new InvalidOperationException(
                    $"The {backend} compiler returned {artifact.Format} instead of {requiredFormat}.");
            }

            if (!string.Equals(
                    artifact.EntryPoint,
                    WarpDeviceAbi.GetEntryPoint(kernel),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The {backend} compiler returned entry point '{artifact.EntryPoint}'.");
            }

            artifacts.Add(backend, artifact);
        }

        return new WarpCompilation(kernel, artifacts);
    }

    private static void ValidateBackendContracts(
        IReadOnlyDictionary<WarpBackendKind, IWarpBackendCompiler> compilers)
    {
        WarpBackendContract expected = WarpProfileCatalog.BackendContract;
        foreach (WarpBackendKind backend in WarpBackendCatalog.Required)
        {
            WarpBackendContract actual = compilers[backend].Contract
                ?? throw new InvalidOperationException(
                    $"The {backend} compiler did not declare a backend contract.");
            if (actual.ExactlyMatches(expected))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"The {backend} compiler does not implement the exact portable " +
                $"WarpCLR contract: {DescribeContractMismatch(expected, actual)}.");
        }
    }

    private static string DescribeContractMismatch(
        WarpBackendContract expected,
        WarpBackendContract actual)
    {
        var differences = new List<string>();
        if (!string.Equals(expected.ProfileId, actual.ProfileId, StringComparison.Ordinal))
        {
            differences.Add(
                $"profile '{actual.ProfileId}' was declared instead of '{expected.ProfileId}'");
        }

        if (!actual.Instructions.SequenceEqual(expected.Instructions))
        {
            differences.Add(
                $"instructions [{string.Join(", ", actual.Instructions)}] do not equal " +
                $"[{string.Join(", ", expected.Instructions)}]");
        }

        if (!actual.ControlFlow.SequenceEqual(expected.ControlFlow))
        {
            differences.Add(
                $"control flow [{string.Join(", ", actual.ControlFlow)}] does not equal " +
                $"[{string.Join(", ", expected.ControlFlow)}]");
        }

        if (!actual.Reductions.SequenceEqual(expected.Reductions))
        {
            differences.Add(
                $"reductions [{string.Join(", ", actual.Reductions)}] do not equal " +
                $"[{string.Join(", ", expected.Reductions)}]");
        }

        return string.Join("; ", differences);
    }
}
