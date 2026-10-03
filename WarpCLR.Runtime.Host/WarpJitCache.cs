using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Runtime.Host;

public sealed class WarpJitCache : IAsyncDisposable
{
    private const string CacheSchema = "warp.jit-cache/0.2";
    private static readonly SearchValues<char> CacheIdentityCharacters = SearchValues.Create("0123456789ABCDEF");
    private readonly Lock sync = new();
    private readonly Dictionary<string, CacheEntry> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> recency = new();
    private readonly WarpJitCacheOptions options;
    private readonly CancellationTokenSource compilationLifetime = new();
    private Task? shutdown;
    private bool shutdownRequested;
    private long compilationCount;
    private long memoryHitCount;
    private long diskHitCount;

    public WarpJitCache(WarpJitCacheOptions? options = null)
    {
        this.options = options ?? new WarpJitCacheOptions();
        WarpJitCacheOptions.Validate(this.options);
        if (this.options.DirectoryPath is not null)
        {
            Directory.CreateDirectory(this.options.DirectoryPath);
        }
    }

    public WarpJitCacheStatistics Statistics
    {
        get
        {
            lock (sync)
            {
                RemoveFailedEntries();
                return new WarpJitCacheStatistics(compilationCount, memoryHitCount, diskHitCount, entries.Count);
            }
        }
    }

    internal Task<CoreCLRResumableKernel> GetOrCompileAsync(WarpRuntimeModule module, WarpRuntimeEntry entry, CancellationToken cancellationToken)
    {
        string key = GetKey(module, entry);
        return GetOrCompileAsync(key, token => Task.FromResult(CompileCoreCLR(key, entry, token)), cancellationToken);
    }

    internal Task<WarpNativeImage> GetOrCompileNativeAsync(
        WarpRuntimeModule module,
        WarpRuntimeEntry entry,
        WarpNativeTarget target,
        string toolchainIdentity,
        Func<CancellationToken, Task<WarpNativeImage>> compile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolchainIdentity);
        ArgumentNullException.ThrowIfNull(compile);
        string key = GetNativeKey(module, entry, target, toolchainIdentity);
        return GetOrCompileAsync(key, token => CompileNativeAsync(key, entry, target, toolchainIdentity, compile, token), cancellationToken);
    }

    internal void RecordNativeModuleMemoryHit()
    {
        lock (sync) { memoryHitCount++; }
    }

    public ValueTask DisposeAsync() => ShutdownOwnedAsync();

    internal void RequireReady()
    {
        lock (sync) { ObjectDisposedException.ThrowIf(shutdownRequested, this); }
    }

    internal ValueTask ShutdownOwnedAsync()
    {
        lock (sync)
        {
            shutdown ??= ShutdownCoreAsync();
            return new ValueTask(shutdown);
        }
    }

    private async Task ShutdownCoreAsync()
    {
        Task[] pending;
        lock (sync)
        {
            shutdownRequested = true;
            pending = entries.Values.Select(value => value.Compilation).ToArray();
        }

        Task drain = Task.WhenAll(pending);
        try
        {
            await compilationLifetime.CancelAsync().ConfigureAwait(false);
            await drain.ConfigureAwait(false);
        }
        catch (Exception error) when (error is WarpHostException or OperationCanceledException or IOException or
            ObjectDisposedException or PlatformNotSupportedException or CoreCLRCompilationResourceException or WarpCompilationResourceException)
        {
            if (drain.Exception is { } faults && faults.InnerExceptions.Any(exception => exception is not WarpHostException and
                not OperationCanceledException and not IOException and not ObjectDisposedException and not PlatformNotSupportedException and
                not CoreCLRCompilationResourceException and not WarpCompilationResourceException))
            {
                throw faults;
            }
        }
        finally
        {
            lock (sync)
            {
                entries.Clear();
                recency.Clear();
            }

            pending = [];
            drain = Task.CompletedTask;
            compilationLifetime.Dispose();
        }
    }

    private async Task<T> GetOrCompileAsync<T>(string key, Func<CancellationToken, Task<T>> compile, CancellationToken cancellationToken)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        CacheEntry cacheEntry;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(shutdownRequested, this);
            RemoveFailedEntries();
            if (entries.TryGetValue(key, out CacheEntry? existing))
            {
                cacheEntry = existing;
                recency.Remove(cacheEntry.Recency);
                recency.AddLast(cacheEntry.Recency);
                memoryHitCount++;
            }
            else
            {
                EvictCompletedEntries();
                int compiling = entries.Values.Count(value => !value.Compilation.IsCompleted);
                if (compiling >= options.MaximumConcurrentCompilations)
                {
                    throw new WarpHostException("WRPRUNTIME1002", "The JIT compilation admission limit is exhausted.");
                }

                CancellationToken compilationToken = compilationLifetime.Token;
                Task<object> compilation = Task.Run(async () =>
                {
                    compilationToken.ThrowIfCancellationRequested();
                    T result = await compile(compilationToken).ConfigureAwait(false);
                    compilationToken.ThrowIfCancellationRequested();
                    return (object)result;
                }, compilationToken);
                cacheEntry = new CacheEntry(compilation, recency.AddLast(key));
                entries.Add(key, cacheEntry);
            }
        }

        Task<object> task = cacheEntry.Compilation;
        try
        {
            // Cancelling one waiter never cancels a compilation shared by other dispatches.
            return (T)await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (task.IsFaulted || task.IsCanceled)
        {
            lock (sync)
            {
                if (entries.TryGetValue(key, out CacheEntry? current) && ReferenceEquals(current, cacheEntry))
                {
                    entries.Remove(key);
                    recency.Remove(cacheEntry.Recency);
                }
            }

            throw;
        }
    }

    private static string GetKey(WarpRuntimeModule module, WarpRuntimeEntry entry)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteKeyPrefix(writer, module, entry);
            writer.Write("coreclr.resumable-native/0.1");
            writer.Write(nameof(WarpBackendKind.CoreCLR));
            writer.Write(RuntimeInformation.FrameworkDescription);
            writer.Write(RuntimeInformation.ProcessArchitecture.ToString());
            writer.Write(typeof(CoreCLRResumableKernel).Assembly.ManifestModule.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private static string GetNativeKey(WarpRuntimeModule module, WarpRuntimeEntry entry, WarpNativeTarget target, string toolchainIdentity)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteKeyPrefix(writer, module, entry);
            writer.Write("gpu.resumable-native/0.1");
            writer.Write(target.Backend.ToString());
            writer.Write(target.CacheIdentity);
            writer.Write(target.Architecture);
            writer.Write(target.DeviceIdentity);
            writer.Write(target.RuntimeIdentity);
            writer.Write(toolchainIdentity);
            writer.Write(typeof(WarpPortableMachineEmitter).Assembly.ManifestModule.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture));
            writer.Write(typeof(WarpNativeToolchain).Assembly.ManifestModule.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private static void WriteKeyPrefix(BinaryWriter writer, WarpRuntimeModule module, WarpRuntimeEntry entry)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(entry);
        writer.Write(CacheSchema);
        writer.Write(module.AssemblyHash);
        writer.Write(module.ManifestHash);
        writer.Write(entry.Identity);
        writer.Write(entry.GraphHash);
        writer.Write(entry.IrHash);
        writer.Write(module.ProfileId);
        writer.Write(WarpRuntimeAbi.Version);
        writer.Write(WarpRuntimeAbi.SafepointPolicy);
        writer.Write(WarpLogicalMachineLayout.Version);
    }

    private CoreCLRResumableKernel CompileCoreCLR(string key, WarpRuntimeEntry entry, CancellationToken compilationToken)
    {
        compilationToken.ThrowIfCancellationRequested();
        byte[] canonicalPlan = WarpCoreCLRPlanCodec.Serialize(entry.Kernel);
        ValidateDiskPlan(key, canonicalPlan);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(entry.Layout);
        compilationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            compilationCount++;
        }

        PersistPlan(key, canonicalPlan);
        return compiled;
    }

    private async Task<WarpNativeImage> CompileNativeAsync(string key, WarpRuntimeEntry entry,
        WarpNativeTarget target, string toolchainIdentity, Func<CancellationToken, Task<WarpNativeImage>> compile,
        CancellationToken compilationToken)
    {
        compilationToken.ThrowIfCancellationRequested();
        byte[] canonicalPlan = WarpCoreCLRPlanCodec.Serialize(entry.Kernel);
        ValidateDiskPlan(key, canonicalPlan);
        // Cache lifetime is independent of each context/dispatch waiter. Caller-supplied caches are not shut down by contexts.
        WarpNativeImage image = await compile(compilationToken).ConfigureAwait(false);
        compilationToken.ThrowIfCancellationRequested();
        image.RequireProductionAdmission();
        if (image.Target != target || !string.Equals(image.ToolchainIdentity, toolchainIdentity, StringComparison.Ordinal) ||
            image.MachineLayout is null || !string.Equals(WarpIrHash.Compute(image.MachineLayout.Kernel), entry.IrHash, StringComparison.Ordinal))
        {
            throw new WarpHostException("WRPNATIVE2006", "The native compilation changed its toolchain, target, or verified logical closure.");
        }

        lock (sync) { compilationCount++; }
        PersistPlan(key, canonicalPlan);
        return image;
    }

    private void EvictCompletedEntries()
    {
        while (entries.Count >= options.MaximumMemoryEntries)
        {
            LinkedListNode<string>? node = recency.First;
            while (node is not null)
            {
                CacheEntry candidate = entries[node.Value];
                if (candidate.Compilation.IsCompleted)
                {
                    break;
                }

                node = node.Next;
            }

            if (node is null)
            {
                throw new WarpHostException("WRPRUNTIME1002", "The JIT cache is full of active compilations.");
            }

            entries.Remove(node.Value);
            recency.Remove(node);
        }
    }

    private void RemoveFailedEntries()
    {
        string[] failed = entries.Where(pair => pair.Value.Compilation.IsFaulted || pair.Value.Compilation.IsCanceled)
            .Select(pair => pair.Key).ToArray();
        foreach (string key in failed)
        {
            CacheEntry entry = entries[key];
            _ = entry.Compilation.Exception;
            entries.Remove(key);
            recency.Remove(entry.Recency);
        }
    }

    private void ValidateDiskPlan(string key, byte[] canonicalPlan)
    {
        if (options.DirectoryPath is null)
        {
            return;
        }

        string path = Path.Combine(options.DirectoryPath, key + ".wrcache");
        try
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) { return; }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (MatchesCanonicalSnapshot(stream, canonicalPlan))
            {
                lock (sync)
                {
                    diskHitCount++;
                }
            }
        }
        catch (IOException)
        {
            // The caller-authorized closure, not an untrusted cache file, remains the compilation input.
        }
        catch (UnauthorizedAccessException)
        {
            // A cache is optional; inability to read it cannot alter program semantics.
        }
    }

    internal static bool MatchesCanonicalSnapshot(Stream stream, ReadOnlySpan<byte> canonicalPlan)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (stream.Length != canonicalPlan.Length) { return false; }
        byte[] content = new byte[canonicalPlan.Length];
        try { stream.ReadExactly(content); }
        catch (EndOfStreamException) { return false; }
        return stream.ReadByte() == -1 && content.AsSpan().SequenceEqual(canonicalPlan);
    }

    private void PersistPlan(string key, byte[] plan)
    {
        if (options.DirectoryPath is null || plan.LongLength > options.MaximumDiskBytes)
        {
            return;
        }

        string directory = options.DirectoryPath;
        string temporaryPath = Path.Combine(directory, key + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp");
        try
        {
            using (var lease = new FileStream(Path.Combine(directory, ".warp-cache.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                if (!TryAdmitDiskPlan(directory, key, plan.LongLength)) { return; }

                using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(plan);
                    output.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, Path.Combine(directory, key + ".wrcache"), overwrite: true);
            }
        }
        catch (IOException)
        {
            // Concurrent processes may hold the lease; the fully compiled in-memory module is still usable.
        }
        catch (UnauthorizedAccessException)
        {
            // Cache storage is never an execution authority or a semantic dependency.
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private bool TryAdmitDiskPlan(string directory, string key, long incomingBytes)
    {
        if (!TryCollectDiskEntries(directory, out List<DiskCacheFile> files, out int entryCount)) { return false; }
        string destinationName = key + ".wrcache";
        string destination = Path.Combine(directory, destinationName);
        if (File.Exists(destination) && File.GetAttributes(destination).HasFlag(FileAttributes.ReparsePoint)) { return false; }
        UInt128 bytes = 0;
        foreach (ref readonly DiskCacheFile file in CollectionsMarshal.AsSpan(files)) { bytes += (UInt128)file.Length; }
        UInt128 availableBytes = (UInt128)(options.MaximumDiskBytes - incomingBytes);
        int additionalEntry = files.Any(file => string.Equals(file.File.Name, destinationName, StringComparison.Ordinal)) ? 0 : 1;
        foreach (DiskCacheFile file in files.OrderBy(file => file.File.LastWriteTimeUtc).ThenBy(file => file.File.Name, StringComparer.Ordinal))
        {
            if (bytes <= availableBytes && entryCount <= options.MaximumDiskEntries - additionalEntry) { break; }
            file.File.Delete();
            bytes -= (UInt128)file.Length;
            entryCount--;
            if (string.Equals(file.File.Name, destinationName, StringComparison.Ordinal)) { additionalEntry = 1; }
        }

        return bytes <= availableBytes && entryCount <= options.MaximumDiskEntries - additionalEntry;
    }

    private bool TryCollectDiskEntries(string directory, out List<DiskCacheFile> files, out int entryCount)
    {
        files = [];
        entryCount = 0;
        foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if (++entryCount > options.MaximumDiskEntries) { return false; }
            if (entry is FileInfo file && IsOwnedCacheName(file.Name) && !file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                files.Add(new DiskCacheFile(file, file.Length));
            }
        }

        return true;
    }

    private static bool IsOwnedCacheName(string name) => name.Length == 72 && name.EndsWith(".wrcache", StringComparison.Ordinal) &&
        name.AsSpan(0, 64).IndexOfAnyExcept(CacheIdentityCharacters) < 0;

    private static void DeleteTemporaryFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* Abandoned unique temporary files are not eligible cache entries. */ }
        catch (UnauthorizedAccessException) { /* A revoked cache directory cannot prevent cleanup. */ }
    }

    private sealed record CacheEntry(Task<object> Compilation, LinkedListNode<string> Recency);

    private sealed record DiskCacheFile(FileInfo File, long Length);
}
