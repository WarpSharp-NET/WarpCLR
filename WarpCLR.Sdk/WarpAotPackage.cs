using System.Collections.ObjectModel;
using System.Security.Cryptography;
using WarpCLR.IR;

namespace WarpCLR.Sdk;

public sealed record WarpPackagedArtifact(
    string ModulePath,
    string SidecarPath,
    WarpArtifactSidecar Sidecar);

public sealed class WarpAotPackage
{
    internal WarpAotPackage(
        IEnumerable<WarpPackagedArtifact> artifacts,
        IDictionary<string, ReadOnlyMemory<byte>> files)
    {
        Artifacts = Array.AsReadOnly(artifacts.ToArray());
        Files = new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(
            new SortedDictionary<string, ReadOnlyMemory<byte>>(files, StringComparer.Ordinal));
    }

    public ReadOnlyCollection<WarpPackagedArtifact> Artifacts { get; }

    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Files { get; }

    public void WriteToDirectory(string directoryPath, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        string root = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(root);

        var targets = new List<(string Path, ReadOnlyMemory<byte> Content)>(Files.Count);
        foreach ((string relativePath, ReadOnlyMemory<byte> content) in Files)
        {
            string target = ResolveFile(root, relativePath);
            if (!overwrite && File.Exists(target))
            {
                throw new IOException($"Package file '{relativePath}' already exists.");
            }

            targets.Add((target, content));
        }

        foreach ((string target, ReadOnlyMemory<byte> content) in targets)
        {
            using FileStream stream = new(
                target,
                overwrite ? FileMode.Create : FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            stream.Write(content.Span);
        }
    }

    public void ValidateDirectory(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        string root = Path.GetFullPath(directoryPath);

        foreach (WarpPackagedArtifact artifact in Artifacts)
        {
            string modulePath = ResolveFile(root, artifact.ModulePath);
            string sidecarPath = ResolveFile(root, artifact.SidecarPath);
            if (!File.Exists(modulePath) || !File.Exists(sidecarPath))
            {
                throw new InvalidDataException("The AOT package is missing a required file.");
            }

            byte[] sidecarBytes = File.ReadAllBytes(sidecarPath);
            WarpArtifactSidecar sidecar = WarpArtifactSidecarCodec.Deserialize(sidecarBytes);
            if (sidecar != artifact.Sidecar)
            {
                throw new InvalidDataException("The AOT package sidecar does not match its package index.");
            }

            byte[] moduleBytes = File.ReadAllBytes(modulePath);
            string moduleHash = Convert.ToHexString(SHA256.HashData(moduleBytes));
            if (!string.Equals(moduleHash, sidecar.ModuleHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Package module '{artifact.ModulePath}' has a changed hash.");
            }
        }
    }

    private static string ResolveFile(string root, string relativePath)
    {
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!result.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A package path resolves outside its package directory.");
        }

        return result;
    }
}
