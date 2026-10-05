using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using WarpCLR.Compiler;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableMetadataUtf16Cases
{
    private static readonly string[] Collisions = ["\uD800", "\uD801", "\uDC00", "\uDC01", "\uFFFD"];

    internal static void SnapshotValuesAndCharactersPreserveEveryUtf16Unit()
    {
        Assert.HasCount(1, Collisions.Select(value => Convert.ToHexString(JsonSerializer.SerializeToUtf8Bytes(value))).Distinct(StringComparer.Ordinal));
        Assert.HasCount(1, Collisions.Select(value => Convert.ToHexString(Encoding.UTF8.GetBytes(value))).Distinct(StringComparer.Ordinal));
        Assert.HasCount(Collisions.Length, Collisions.Select(WarpPortableSnapshotIdentity.Hash).Distinct(StringComparer.Ordinal));
        string allUnits = new(Enumerable.Range(0, 65536).Select(value => (char)value).ToArray());
        using JsonDocument snapshot = JsonDocument.Parse(WarpPortableSnapshotIdentity.Serialize(allUnits));
        uint[] captured = snapshot.RootElement.GetProperty("Value").EnumerateArray().Select(unit => unit.GetUInt32()).ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(0, 65536).Select(value => (uint)value).ToArray(), captured);
        foreach (string text in Collisions.Append("\uD800\uDC00\0\uDC01\uFFFD"))
        {
            CollectionAssert.AreEqual(text.Select(unit => (uint)unit).ToArray(), WarpPortableSnapshotIdentity.CodeUnits(text));
        }
        foreach (string text in Collisions)
        {
            using JsonDocument character = JsonDocument.Parse(WarpPortableSnapshotIdentity.Serialize(text[0]));
            Assert.AreEqual((uint)text[0], character.RootElement.GetProperty("Value").GetUInt32());
        }
        Assert.AreNotEqual(WarpPortableSnapshotIdentity.Hash(""), WarpPortableSnapshotIdentity.Hash("\0"), StringComparer.Ordinal);
        Assert.AreNotEqual(WarpPortableSnapshotIdentity.Hash("\uD800\uDC00"), WarpPortableSnapshotIdentity.Hash("\uDC00\uD800"), StringComparer.Ordinal);
    }

    internal static void SnapshotDictionaryKeysAreInjectiveBeforeJsonEncoding()
    {
        string[] keys = [.. Collisions, "", "\0", "u16:D800", "\uD800\uDC00", "\uDC00\uD800", "\0\uD800\0"];
        Dictionary<string, string> values = keys.ToDictionary(key => key, key => key, StringComparer.Ordinal);
        using JsonDocument snapshot = JsonDocument.Parse(WarpPortableSnapshotIdentity.Serialize(values));
        JsonElement map = snapshot.RootElement.GetProperty("Value");
        Assert.HasCount(keys.Length, map.EnumerateObject().ToArray());
        foreach (string key in keys)
        {
            CollectionAssert.AreEqual(key.Select(unit => (uint)unit).ToArray(), map.GetProperty(WarpPortableSnapshotIdentity.DictionaryKey(key))
                .EnumerateArray().Select(unit => unit.GetUInt32()).ToArray());
        }
        Assert.HasCount(65536, Enumerable.Range(0, 65536).Select(unit => WarpPortableSnapshotIdentity.DictionaryKey(((char)unit).ToString()))
            .Distinct(StringComparer.Ordinal));
        Dictionary<char, string> characters = Collisions.ToDictionary(text => text[0], text => text);
        using JsonDocument characterMap = JsonDocument.Parse(WarpPortableSnapshotIdentity.Serialize(characters));
        Assert.HasCount(Collisions.Length, characterMap.RootElement.GetProperty("Value").EnumerateObject().ToArray());
    }

    internal static void BinarySnapshotUsesExactLengthAndLittleEndianUnits()
    {
        foreach (string text in Collisions.Concat(["", "\0", "\uD800\uDC00\0\uDC01\uFFFD"]))
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) { WarpPortableSnapshotIdentity.Write(writer, text); }
            byte[] bytes = stream.ToArray();
            Assert.HasCount(4 + text.Length * 2, bytes);
            Assert.AreEqual((uint)text.Length, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
            for (int index = 0; index < text.Length; index++)
            {
                Assert.AreEqual((ushort)text[index], BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4 + index * 2)));
            }
            CollectionAssert.AreEqual(bytes.AsSpan(4).ToArray(), WarpPortableSnapshotIdentity.RawUtf16Bytes(text));
        }
    }

    internal static void RuntimeHeapDigestDistinguishesRawTypeNamesWithoutChangingStorage()
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        uint[]? baseline = null;
        foreach (string raw in Collisions)
        {
            var schema = new WarpPortableHeapSchema([new(1, "captured-type:" + raw, WarpPortableHeapLayout.Class, 2, [], staticWords: 1)]);
            uint[] arena = schema.CreateArena(717, 128, 4, 8, 1, 128);
            hashes.Add(Convert.ToHexString(arena.AsSpan((int)WarpPortableHeapLayout.SchemaHash, 8).ToArray()
                .SelectMany(word => BitConverter.GetBytes(word)).ToArray()));
            arena.AsSpan((int)WarpPortableHeapLayout.SchemaHash, 8).Clear();
            if (baseline is null) { baseline = arena; }
            else { CollectionAssert.AreEqual(baseline, arena); }
        }
        Assert.HasCount(Collisions.Length, hashes);
    }

    internal static void ManifestUtf8RejectsReplacementAndPreservesActualTextBytes()
    {
        foreach (string text in Collisions.Where(text => text[0] != 0xFFFD))
        {
            WarpVerificationException bytes = Assert.ThrowsExactly<WarpVerificationException>(() => WarpManifestUtf8Text.Bytes("{\"name\":\"" + text + "\"}"));
            WarpVerificationException count = Assert.ThrowsExactly<WarpVerificationException>(() => WarpManifestUtf8Text.ByteCount(text));
            Assert.AreEqual("WRPMAN1001", bytes.Code, StringComparer.Ordinal);
            Assert.AreEqual("WRPMAN1001", count.Code, StringComparer.Ordinal);
        }
        foreach (string text in new[] { "{\"raw\":\"\\uD800\"}", "\uD800\uDC00", "\uFFFD" })
        {
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(text), WarpManifestUtf8Text.Bytes(text));
            Assert.AreEqual(Encoding.UTF8.GetByteCount(text), WarpManifestUtf8Text.ByteCount(text));
        }
    }
}
