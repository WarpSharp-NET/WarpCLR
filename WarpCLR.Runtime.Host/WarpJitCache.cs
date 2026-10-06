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

public sealed partial class WarpJitCache : IAsyncDisposable
{
    private const string CacheSchema = "warp.jit-cache/raw-utf16-length-delimited/0.3";
    private static readonly SearchValues<char> CacheIdentityCharacters = SearchValues.Create("0123456789ABCDEF");
    private readonly Lock sync = new();
    private readonly Dictionary<string, CacheEntry> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> recency = new();
    private readonly WarpJitCacheOptions options;
    private readonly Func<WarpCoreCLRWorkerKernel, Task>? beforeCorePublication;
    private readonly WarpCoreCLRWorkerTestHooks? coreProbes;
    private readonly CancellationTokenSource compilationLifetime = new();
    private Task? shutdown;
    private bool shutdownRequested;
    private long compilationCount;
    private long memoryHitCount;
    private long diskHitCount;

    public WarpJitCache(WarpJitCacheOptions? options = null) : this(options, null, null)
    {
    }

    internal WarpJitCache(WarpJitCacheOptions? options,
        Func<WarpCoreCLRWorkerKernel, Task>? beforeCorePublication, WarpCoreCLRWorkerTestHooks? coreProbes)
    {
        this.options = options ?? new WarpJitCacheOptions();
        this.beforeCorePublication = beforeCorePublication;
        this.coreProbes = coreProbes;
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

    internal async Task<WarpCoreCLRWorkerLease> GetOrCompileAsync(WarpRuntimeModule module, WarpRuntimeEntry entry, CancellationToken cancellationToken)
    {
        string key = GetKey(module, entry);
        for (;;)
        {
            WarpCoreCLRWorkerKernel kernel = await GetOrCompileAsync(key,
                token => CompileCoreCLRAsync(key, entry, token), cancellationToken).ConfigureAwait(false);
            lock (sync)
            {
                ObjectDisposedException.ThrowIf(shutdownRequested, this);
                if (entries.TryGetValue(key, out CacheEntry? current) && current.Compilation.IsCompletedSuccessfully &&
                    coreByKey.TryGetValue(key, out WarpCoreCLRWorkerKernel? actual) && ReferenceEquals(actual, kernel))
                {
                    WarpCoreCLRWorkerLease? lease = kernel.TryAcquireLease();
                    if (lease is not null) { return lease; }
                    entries.Remove(key); recency.Remove(current.Recency);
                    coreByKey.Remove(key); _ = RecordRetirementAsync(kernel);
                }
            }
        }
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

    public async ValueTask DisposeAsync()
    {
        try { await ShutdownOwnedAsync().ConfigureAwait(false); }
        finally { compilationLifetime.Dispose(); }
    }

    internal void RequireReady()
    {
        lock (sync) { ObjectDisposedException.ThrowIf(shutdownRequested, this); }
    }

    internal const string ShutdownSemantics = "warp.coreclr-cache-shutdown/first-snapshot-bounded-complete-fault-aggregate-owned-late-child-stop-propagate-cleanup-failure/0.5";

    internal ValueTask ShutdownOwnedAsync()
    {
        lock (sync)
        {
            if (shutdown is null)
            {
                shutdownRequested = true;
                Task[] pending = entries.Values.Select(value => value.Compilation).ToArray();
                var deadline = new CancellationTokenSource(options.CoreCLR.CleanupTimeout);
                Task cancellation = compilationLifetime.CancelAsync();
                Task[] disposals = coreWorkers.Select(worker => worker.DisposeAsync().AsTask()).Concat(retirements).ToArray();
                shutdown = ShutdownCoreAsync(Task.WhenAll(pending.Concat(disposals).Append(cancellation)), deadline);
            }
            return new ValueTask(shutdown);
        }
    }

    private async Task ShutdownCoreAsync(Task completion, CancellationTokenSource deadline)
    {
        using (deadline)
        {
            try { await completion.WaitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException error) when (deadline.IsCancellationRequested)
            {
                throw new WarpHostException("WRPCORECLR3003", "JIT cache shutdown exhausted its separate cleanup quota; remaining work is quarantined.", error);
            }
            catch (Exception) when (completion.Exception is { } failures && failures.Flatten().InnerExceptions.Count > 1 &&
                !failures.Flatten().InnerExceptions.All(ExpectedShutdownFailure))
            {
                // Await selects one fault. Preserve the complete captured aggregate
                // when an expected fault could otherwise mask a compiler bug.
                throw failures.Flatten();
            }
            catch (Exception error) when (ExpectedShutdownFailure(error) &&
                (completion.Exception is null || completion.Exception.Flatten().InnerExceptions.All(ExpectedShutdownFailure)))
            {
                // Every captured fault is an admitted resource/cancellation outcome.
                // Unexpected compiler faults remain part of the shared first result.
            }
            finally
            {
                lock (sync) { entries.Clear(); recency.Clear(); coreWorkers.Clear(); coreByKey.Clear(); retirements.Clear(); }
                compilationLifetime.Dispose();
            }
        }
    }

    private static bool ExpectedShutdownFailure(Exception error) =>
        error is WarpHostException hostError && !string.Equals(hostError.Code, "WRPCORECLR3003", StringComparison.Ordinal) ||
        error is OperationCanceledException or IOException or ObjectDisposedException or PlatformNotSupportedException or
            CoreCLRCompilationResourceException or WarpCompilationResourceException;

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
                if (typeof(T) == typeof(WarpCoreCLRWorkerKernel)) { AdmitCoreWorker(); }
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
                    if (compilationToken.IsCancellationRequested && result is WarpCoreCLRWorkerKernel worker)
                    { await worker.DisposeAsync().ConfigureAwait(false); }
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

    private string GetKey(WarpRuntimeModule module, WarpRuntimeEntry entry)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteKeyPrefix(writer, module, entry);
            WarpRawIdentity.WriteString(writer, WarpCoreCLRWorkerProtocol.Version);
            WarpRawIdentity.WriteString(writer, WarpCoreCLRWorkerCleanupAttempt.SemanticId);
            WarpRawIdentity.WriteString(writer, WarpCoreCLRWorkerContainment.SemanticId);
            WarpRawIdentity.WriteString(writer, WarpCoreCLRBinaryPlanCodec.Version);
            WarpRawIdentity.WriteString(writer, WarpCoreCLRControllerAdmission.Version);
            WarpRawIdentity.WriteString(writer, WarpCoreCLRRecoveryCatalog.Version);
            WarpRawIdentity.WriteString(writer, ShutdownSemantics);
            WarpRawIdentity.WriteString(writer, nameof(WarpBackendKind.CoreCLR));
            WarpRawIdentity.WriteString(writer, RuntimeInformation.FrameworkDescription);
            WarpRawIdentity.WriteString(writer, RuntimeInformation.ProcessArchitecture.ToString());
            WarpRawIdentity.WriteString(writer, typeof(CoreCLRResumableKernel).Assembly.ManifestModule.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture));
            WarpRawIdentity.WriteString(writer, typeof(WarpJitCache).Assembly.ManifestModule.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture));
            string worker = options.CoreCLR.WorkerAssemblyPath ?? Path.Combine(AppContext.BaseDirectory, "WarpCLR.CoreCLR.Worker.dll");
            writer.Write(WarpCoreCLRWorkerIdentity.DeploymentDigest(Path.GetDirectoryName(worker)!));
            string host = WarpCoreCLRWorkerProcess.GetDotnetHost(options.CoreCLR);
            WarpRawIdentity.WriteString(writer, host);
            using var hostFile = new FileStream(host, FileMode.Open, FileAccess.Read, FileShare.Read);
            writer.Write(SHA256.HashData(hostFile));
        }

        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private static string GetNativeKey(WarpRuntimeModule module, WarpRuntimeEntry entry, WarpNativeTarget target, string toolchainIdentity)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteKeyPrefix(writer, module, entry);
            WarpRawIdentity.WriteString(writer, "gpu.resumable-native/0.1");
            WarpRawIdentity.WriteString(writer, target.Backend.ToString());
            WarpRawIdentity.WriteString(writer, target.CacheIdentity);
            WarpRawIdentity.WriteString(writer, target.Architecture);
            WarpRawIdentity.WriteString(writer, target.DeviceIdentity);
            WarpRawIdentity.WriteString(writer, target.RuntimeIdentity);
            WarpRawIdentity.WriteString(writer, toolchainIdentity);
            WarpRawIdentity.WriteString(writer, typeof(WarpPortableMachineEmitter).Assembly.ManifestModule.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture));
            WarpRawIdentity.WriteString(writer, typeof(WarpNativeToolchain).Assembly.ManifestModule.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private static void WriteKeyPrefix(BinaryWriter writer, WarpRuntimeModule module, WarpRuntimeEntry entry)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(entry);
        WarpRawIdentity.WriteString(writer, CacheSchema);
        WarpRawIdentity.WriteString(writer, module.AssemblyHash);
        WarpRawIdentity.WriteString(writer, module.ManifestHash);
        WarpRawIdentity.WriteString(writer, entry.Identity);
        WarpRawIdentity.WriteString(writer, entry.GraphHash);
        WarpRawIdentity.WriteString(writer, entry.IrHash);
        WarpRawIdentity.WriteString(writer, module.ProfileId);
        WarpRawIdentity.WriteString(writer, WarpRuntimeAbi.Version);
        WarpRawIdentity.WriteString(writer, WarpRuntimeAbi.SafepointPolicy);
        WarpRawIdentity.WriteString(writer, WarpLogicalMachineLayout.Version);
    }

    private async Task<WarpNativeImage> CompileNativeAsync(string key, WarpRuntimeEntry entry,
        WarpNativeTarget target, string toolchainIdentity, Func<CancellationToken, Task<WarpNativeImage>> compile,
        CancellationToken compilationToken)
    {
        compilationToken.ThrowIfCancellationRequested();
        byte[] canonicalPlan = WarpCoreCLRBinaryPlanCodec.Serialize(entry.Kernel);
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

            if (coreByKey.TryGetValue(node.Value, out WarpCoreCLRWorkerKernel? worker))
            { coreByKey.Remove(node.Value); _ = RecordRetirementAsync(worker); }
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
            if (coreByKey.TryGetValue(key, out WarpCoreCLRWorkerKernel? worker))
            { coreByKey.Remove(key); _ = RecordRetirementAsync(worker); }
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
