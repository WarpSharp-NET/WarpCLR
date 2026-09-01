using System.Buffers;
using System.Text;
using System.Text.Json;

namespace WarpCLR.Verifier;

internal sealed record WarpManifestEntryData(
    string Type,
    string Method,
    IReadOnlyList<WarpParameterRole> ParameterRoles,
    IReadOnlyList<string> Capabilities,
    string GraphHash);

internal sealed record WarpManifestData(
    string Contract,
    string Producer,
    string ProducerVersion,
    IReadOnlyList<WarpManifestEntryData> Entries);

internal static class WarpManifestParser
{
    private const string SupportedContract = "warpcil/0.1";

    public static WarpManifestData Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json.ToArray());
            JsonElement root = document.RootElement;
            RequireObjectProperties(
                root,
                "contract",
                "producer",
                "producerVersion",
                "entries",
                "hostImports",
                "extensions");

            string contract = RequireString(root, "contract");
            string producer = RequireString(root, "producer");
            string producerVersion = RequireString(root, "producerVersion");
            if (!string.Equals(contract, SupportedContract, StringComparison.Ordinal))
            {
                throw Error("WRPCIL2002", $"Manifest contract '{contract}' is not supported.");
            }

            if (string.IsNullOrWhiteSpace(producer) || string.IsNullOrWhiteSpace(producerVersion))
            {
                throw Error("WRPCIL2001", "Manifest producer data cannot be empty.");
            }

            RequireEmptyArray(root, "hostImports");
            RequireEmptyArray(root, "extensions");

            JsonElement entryArray = root.GetProperty("entries");
            if (entryArray.ValueKind != JsonValueKind.Array || entryArray.GetArrayLength() == 0)
            {
                throw Error("WRPCIL2001", "The manifest must contain at least one entry.");
            }

            var entries = new List<WarpManifestEntryData>(entryArray.GetArrayLength());
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entryElement in entryArray.EnumerateArray())
            {
                WarpManifestEntryData entry = ParseEntry(entryElement);
                string identity = $"{entry.Type}.{entry.Method}";
                if (!identities.Add(identity))
                {
                    throw Error("WRPCIL2001", $"Manifest entry '{identity}' is duplicated.");
                }

                entries.Add(entry);
            }

            var manifest = new WarpManifestData(contract, producer, producerVersion, entries);
            byte[] canonical = WriteCanonical(manifest);
            if (!json.SequenceEqual(canonical))
            {
                throw Error("WRPCIL2001", "The embedded manifest is not canonical UTF-8 JSON.");
            }

            return manifest;
        }
        catch (WarpVerificationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Error("WRPCIL2001", $"The embedded manifest is invalid JSON. {exception.Message}");
        }
    }

    private static WarpManifestEntryData ParseEntry(JsonElement element)
    {
        RequireObjectProperties(
            element,
            "type",
            "method",
            "parameterRoles",
            "capabilities",
            "graphHash");

        string type = RequireString(element, "type");
        string method = RequireString(element, "method");
        string graphHash = RequireString(element, "graphHash");
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(method))
        {
            throw Error("WRPCIL2001", "Manifest entry identities cannot be empty.");
        }

        if (!IsUppercaseSha256(graphHash))
        {
            throw Error("WRPCIL2001", "A manifest graph hash must be an uppercase SHA-256 value.");
        }

        JsonElement roleArray = element.GetProperty("parameterRoles");
        if (roleArray.ValueKind != JsonValueKind.Array || roleArray.GetArrayLength() == 0)
        {
            throw Error("WRPCIL2001", "A manifest entry must declare parameter roles.");
        }

        var roles = new List<WarpParameterRole>(roleArray.GetArrayLength());
        bool foundScalar = false;
        foreach (JsonElement roleElement in roleArray.EnumerateArray())
        {
            string role = roleElement.ValueKind == JsonValueKind.String
                ? roleElement.GetString()!
                : throw Error("WRPCIL2001", "A parameter role must be a string.");

            if (string.Equals(role, "input", StringComparison.Ordinal))
            {
                if (foundScalar)
                {
                    throw Error("WRPCIL2001", "Input parameters must precede scalar parameters.");
                }

                roles.Add(WarpParameterRole.InputBuffer);
            }
            else if (string.Equals(role, "scalar", StringComparison.Ordinal))
            {
                foundScalar = true;
                roles.Add(WarpParameterRole.Scalar);
            }
            else
            {
                throw Error("WRPCIL2001", $"Parameter role '{role}' is not supported.");
            }
        }

        if (!roles.Contains(WarpParameterRole.InputBuffer))
        {
            throw Error("WRPCIL2001", "A manifest entry must declare an input buffer.");
        }

        JsonElement capabilityArray = element.GetProperty("capabilities");
        if (capabilityArray.ValueKind != JsonValueKind.Array)
        {
            throw Error("WRPCIL2001", "Entry capabilities must be an array.");
        }

        string[] capabilities = capabilityArray
            .EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String
                ? value.GetString()!
                : throw Error("WRPCIL2001", "A capability identifier must be a string."))
            .ToArray();

        return new WarpManifestEntryData(type, method, roles, capabilities, graphHash);
    }

    private static byte[] WriteCanonical(WarpManifestData manifest)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("contract", manifest.Contract);
            writer.WriteString("producer", manifest.Producer);
            writer.WriteString("producerVersion", manifest.ProducerVersion);
            writer.WriteStartArray("entries");
            foreach (WarpManifestEntryData entry in manifest.Entries)
            {
                writer.WriteStartObject();
                writer.WriteString("type", entry.Type);
                writer.WriteString("method", entry.Method);
                writer.WriteStartArray("parameterRoles");
                foreach (WarpParameterRole role in entry.ParameterRoles)
                {
                    writer.WriteStringValue(role == WarpParameterRole.InputBuffer ? "input" : "scalar");
                }

                writer.WriteEndArray();
                writer.WriteStartArray("capabilities");
                foreach (string capability in entry.Capabilities)
                {
                    writer.WriteStringValue(capability);
                }

                writer.WriteEndArray();
                writer.WriteString("graphHash", entry.GraphHash);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("hostImports");
            writer.WriteEndArray();
            writer.WriteStartArray("extensions");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void RequireObjectProperties(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Error("WRPCIL2001", "A manifest object has an invalid JSON type.");
        }

        string[] actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw Error("WRPCIL2001", "A manifest object has missing, extra, or unordered properties.");
        }
    }

    private static string RequireString(JsonElement parent, string name)
    {
        JsonElement value = parent.GetProperty(name);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw Error("WRPCIL2001", $"Manifest property '{name}' must be a string.");
    }

    private static void RequireEmptyArray(JsonElement parent, string name)
    {
        JsonElement value = parent.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 0)
        {
            throw Error("WRPCIL2002", $"Manifest property '{name}' must be empty for profile 0.1.");
        }
    }

    private static bool IsUppercaseSha256(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (char character in value)
        {
            bool digit = character is >= '0' and <= '9';
            bool uppercaseHex = character is >= 'A' and <= 'F';
            if (!digit && !uppercaseHex)
            {
                return false;
            }
        }

        return true;
    }

    private static WarpVerificationException Error(string code, string message) => new(code, message);
}
