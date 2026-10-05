using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WarpCLR.Verifier;

// Identity serialization operates on captured immutable compiler metadata. It
// never normalizes Unicode or repairs text after a lossy UTF8/JSON conversion.
internal static class WarpPortableSnapshotIdentity
{
    internal const string Semantics = "warp.captured-snapshot-identity/raw-utf16-unit-values-length-delimited-binary-injective-unit-dictionary-keys/0.1";
    private static readonly JsonSerializerOptions Options = CreateOptions();

    internal static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(new { Semantics, Value = value }, Options);

    internal static JsonElement Element(object value, Type inputType) => JsonSerializer.SerializeToElement(value, inputType, Options);

    internal static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Serialize(value)));

    internal static uint[] CodeUnits(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var units = new uint[value.Length];
        for (int index = 0; index < units.Length; index++) { units[index] = value[index]; }
        return units;
    }

    internal static byte[] RawUtf16Bytes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = new byte[checked(value.Length * 2)];
        for (int index = 0; index < value.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * 2), value[index]);
        }
        return bytes;
    }

    internal static void Write(BinaryWriter writer, string value)
    {
        ArgumentNullException.ThrowIfNull(writer); ArgumentNullException.ThrowIfNull(value);
        writer.Write(checked((uint)value.Length));
        foreach (char unit in value) { writer.Write((ushort)unit); }
    }

    internal static string DictionaryKey(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Create(checked(4 + value.Length * 4), value, static (destination, source) =>
        {
            "u16:".AsSpan().CopyTo(destination);
            const string hex = "0123456789ABCDEF";
            for (int index = 0; index < source.Length; index++)
            {
                int target = 4 + index * 4;
                destination[target] = hex[source[index] >> 12];
                destination[target + 1] = hex[source[index] >> 8 & 15];
                destination[target + 2] = hex[source[index] >> 4 & 15];
                destination[target + 3] = hex[source[index] & 15];
            }
        });
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new RawString()); options.Converters.Add(new RawCharacter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private sealed class RawString : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("Captured identity snapshots are write-only metadata, not source JSON admission.");

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (char unit in value) { writer.WriteNumberValue((uint)unit); }
            writer.WriteEndArray();
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WritePropertyName(DictionaryKey(value));
    }

    private sealed class RawCharacter : JsonConverter<char>
    {
        public override char Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("Captured identity snapshots are write-only metadata, not source JSON admission.");

        public override void Write(Utf8JsonWriter writer, char value, JsonSerializerOptions options) => writer.WriteNumberValue((uint)value);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, char value, JsonSerializerOptions options) =>
            writer.WritePropertyName(DictionaryKey(value.ToString()));
    }
}
