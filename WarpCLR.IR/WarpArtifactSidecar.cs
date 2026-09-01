using System.Buffers;
using System.Text.Json;

namespace WarpCLR.IR;

public sealed record WarpArtifactSidecar(
    string Profile,
    string DeviceAbi,
    string Entry,
    WarpBackendKind Backend,
    WarpArtifactFormat Format,
    WarpConformanceStatus Conformance,
    string ManifestHash,
    string AssemblyHash,
    string GraphHash,
    string IrHash,
    string ModuleHash,
    string ModulePath);

public static class WarpArtifactSidecarCodec
{
    public static byte[] Serialize(WarpArtifactSidecar sidecar)
    {
        Validate(sidecar);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("profile", sidecar.Profile);
            writer.WriteString("deviceAbi", sidecar.DeviceAbi);
            writer.WriteString("entry", sidecar.Entry);
            writer.WriteString("backend", GetBackendName(sidecar.Backend));
            writer.WriteString("format", GetFormatName(sidecar.Format));
            writer.WriteString("conformance", GetConformanceName(sidecar.Conformance));
            writer.WriteString("manifestHash", sidecar.ManifestHash);
            writer.WriteString("assemblyHash", sidecar.AssemblyHash);
            writer.WriteString("graphHash", sidecar.GraphHash);
            writer.WriteString("irHash", sidecar.IrHash);
            writer.WriteString("moduleHash", sidecar.ModuleHash);
            writer.WriteString("modulePath", sidecar.ModulePath);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static WarpArtifactSidecar Deserialize(ReadOnlySpan<byte> json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json.ToArray());
            JsonElement root = document.RootElement;
            RequireProperties(
                root,
                "profile",
                "deviceAbi",
                "entry",
                "backend",
                "format",
                "conformance",
                "manifestHash",
                "assemblyHash",
                "graphHash",
                "irHash",
                "moduleHash",
                "modulePath");

            var sidecar = new WarpArtifactSidecar(
                RequireString(root, "profile"),
                RequireString(root, "deviceAbi"),
                RequireString(root, "entry"),
                ParseBackend(RequireString(root, "backend")),
                ParseFormat(RequireString(root, "format")),
                ParseConformance(RequireString(root, "conformance")),
                RequireString(root, "manifestHash"),
                RequireString(root, "assemblyHash"),
                RequireString(root, "graphHash"),
                RequireString(root, "irHash"),
                RequireString(root, "moduleHash"),
                RequireString(root, "modulePath"));

            byte[] canonical = Serialize(sidecar);
            if (!json.SequenceEqual(canonical))
            {
                throw new InvalidDataException("The artifact sidecar is not canonical UTF-8 JSON.");
            }

            return sidecar;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The artifact sidecar is not valid JSON.", exception);
        }
    }

    public static void Validate(WarpArtifactSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        if (!string.Equals(sidecar.Profile, WarpProfileCatalog.ProfileId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The artifact sidecar has an unsupported profile.");
        }

        if (!string.Equals(sidecar.DeviceAbi, WarpDeviceAbi.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The artifact sidecar has an unsupported device ABI.");
        }

        if (string.IsNullOrWhiteSpace(sidecar.Entry))
        {
            throw new InvalidDataException("The artifact entry identity cannot be empty.");
        }
        if (WarpArtifactFormatCatalog.ForBackend(sidecar.Backend) != sidecar.Format)
        {
            throw new InvalidDataException("The artifact format does not match its backend.");
        }

        if (sidecar.Conformance != WarpConformanceStatus.DevelopmentNonconforming)
        {
            throw new InvalidDataException("The artifact conformance state is not registered.");
        }

        RequireHash(sidecar.ManifestHash, "manifest");
        RequireHash(sidecar.AssemblyHash, "assembly");
        RequireHash(sidecar.GraphHash, "graph");
        RequireHash(sidecar.IrHash, "IR");
        RequireHash(sidecar.ModuleHash, "module");
        RequireFileName(sidecar.ModulePath);
    }

    public static string GetBackendName(WarpBackendKind backend) => backend switch
    {
        WarpBackendKind.CpuReference => "cpu",
        WarpBackendKind.Nvidia => "nvidia",
        WarpBackendKind.Amd => "amd",
        WarpBackendKind.Intel => "intel",
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "The backend is not registered."),
    };

    public static string GetFormatName(WarpArtifactFormat format) => format switch
    {
        WarpArtifactFormat.CpuExpressionPlan => "cpu-expression-plan",
        WarpArtifactFormat.NvidiaPtx => "nvidia-ptx",
        WarpArtifactFormat.AmdLlvmIr => "amd-llvm-ir",
        WarpArtifactFormat.IntelSpirvLlvmIr => "intel-spirv-llvm-ir",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The artifact format is not registered."),
    };

    private static string GetConformanceName(WarpConformanceStatus status) => status switch
    {
        WarpConformanceStatus.DevelopmentNonconforming => "development-nonconforming",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "The conformance state is not registered."),
    };

    private static WarpBackendKind ParseBackend(string value) => value switch
    {
        "cpu" => WarpBackendKind.CpuReference,
        "nvidia" => WarpBackendKind.Nvidia,
        "amd" => WarpBackendKind.Amd,
        "intel" => WarpBackendKind.Intel,
        _ => throw new InvalidDataException($"Artifact backend '{value}' is not registered."),
    };

    private static WarpArtifactFormat ParseFormat(string value) => value switch
    {
        "cpu-expression-plan" => WarpArtifactFormat.CpuExpressionPlan,
        "nvidia-ptx" => WarpArtifactFormat.NvidiaPtx,
        "amd-llvm-ir" => WarpArtifactFormat.AmdLlvmIr,
        "intel-spirv-llvm-ir" => WarpArtifactFormat.IntelSpirvLlvmIr,
        _ => throw new InvalidDataException($"Artifact format '{value}' is not registered."),
    };

    private static WarpConformanceStatus ParseConformance(string value) => value switch
    {
        "development-nonconforming" => WarpConformanceStatus.DevelopmentNonconforming,
        _ => throw new InvalidDataException($"Artifact conformance state '{value}' is not registered."),
    };

    private static void RequireProperties(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The artifact sidecar root must be an object.");
        }

        string[] actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The artifact sidecar has missing, extra, or unordered properties.");
        }
    }

    private static string RequireString(JsonElement parent, string name)
    {
        JsonElement value = parent.GetProperty(name);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException($"Artifact sidecar property '{name}' must be a string.");
    }

    private static void RequireHash(string value, string name)
    {
        if (value is null || value.Length != 64 || value.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
        {
            throw new InvalidDataException($"The artifact {name} hash is not an uppercase SHA-256 value.");
        }
    }

    private static void RequireFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value is "." or ".." ||
            value.IndexOfAny(['/', '\\', ':']) >= 0)
        {
            throw new InvalidDataException("The artifact module path must be one safe file name.");
        }
    }
}
