using WarpCLR.Backend.Cpu;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

public enum WarpDevelopmentExecutionMode
{
    SemanticEmulation,
}

public sealed class WarpDevelopmentSession
{
    private readonly WarpLoadedModule module;
    private readonly WarpBackendKind backend;
    private readonly WarpIntegerMapSemanticEmulator emulator = new();

    public WarpDevelopmentSession(
        WarpLoadedModule module,
        WarpBackendKind backend,
        WarpDevelopmentExecutionMode mode)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (!WarpBackendCatalog.Required.Contains(backend))
        {
            throw Error("WRPHOST1002", $"Backend '{backend}' is not registered.");
        }

        if (mode != WarpDevelopmentExecutionMode.SemanticEmulation)
        {
            throw Error("WRPHOST1003", $"Development execution mode '{mode}' is not registered.");
        }

        this.module = module;
        this.backend = backend;
        Mode = mode;
    }

    public WarpDevelopmentExecutionMode Mode { get; }

    public WarpBackendKind Backend => backend;

    public uint[] DispatchIntegerMap(
        string entryIdentity,
        IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint>? scalarArguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryIdentity);
        ArgumentNullException.ThrowIfNull(inputs);
        if (!module.Entries.TryGetValue(entryIdentity, out WarpLoadedEntry? entry))
        {
            throw Error("WRPHOST1001", $"Entry point '{entryIdentity}' is not loaded.");
        }

        ValidateArguments(entry, inputs, scalarArguments);
        WarpLoadedArtifact loadedArtifact = entry.Artifacts[backend];
        var artifact = new WarpBackendArtifact(
            backend,
            loadedArtifact.Sidecar.Format,
            WarpDeviceAbi.IntegerMapEntryPoint,
            loadedArtifact.Content);

        if (!string.Equals(
                artifact.ContentHash,
                loadedArtifact.Sidecar.ModuleHash,
                StringComparison.Ordinal))
        {
            throw Error("WRPHOST1000", "The selected module changed after package loading.");
        }

        return emulator.Execute(artifact, entry.Kernel, inputs, scalarArguments);
    }

    private static void ValidateArguments(
        WarpLoadedEntry entry,
        IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint>? scalarArguments)
    {
        if (inputs.Count != entry.InputBufferCount)
        {
            throw Error("WRPHOST1004", "The input buffer count does not match the entry point.");
        }

        int scalarCount = scalarArguments?.Count ?? 0;
        if (scalarCount != entry.ScalarArgumentCount)
        {
            throw Error("WRPHOST1004", "The scalar argument count does not match the entry point.");
        }

        int? length = null;
        foreach (uint[]? input in inputs)
        {
            if (input is null)
            {
                throw Error("WRPHOST1004", "An input buffer cannot be null.");
            }

            if (length.HasValue && input.Length != length.Value)
            {
                throw Error("WRPHOST1004", "All input buffers must have the same length.");
            }

            length = input.Length;
        }
    }

    private static WarpHostException Error(string code, string message) => new(code, message);
}
