using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

public sealed class WarpJitCache
{
    private const string CacheSchema = "warp.jit-cache/0.1";
    private readonly Lock sync = new();
    private readonly Dictionary<string, CacheEntry> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> recency = new();
    private readonly WarpJitCacheOptions options;
    private long compilationCount;
    private long memoryHitCount;
    private long diskHitCount;

    public WarpJitCache(WarpJitCacheOptions? options = null)
    {
        this.options = options ?? new WarpJitCacheOptions();
        this.options.Validate();
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
                return new WarpJitCacheStatistics(compilationCount, memoryHitCount, diskHitCount, entries.Count);
            }
        }
    }

    internal async Task<CoreCLRResumableKernel> GetOrCompileAsync(WarpRuntimeModule module, WarpRuntimeEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string key = GetKey(module, entry);
        CacheEntry cacheEntry;
        lock (sync)
        {
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
                int compiling = entries.Values.Count(value => !value.Compilation.IsValueCreated || !value.Compilation.Value.IsCompleted);
                if (compiling >= options.MaximumConcurrentCompilations)
                {
                    throw new WarpHostException("WRPRUNTIME1002", "The JIT compilation admission limit is exhausted.");
                }

                var compilation = new Lazy<Task<CoreCLRResumableKernel>>(() => Task.Run(() => Compile(key, entry)), LazyThreadSafetyMode.ExecutionAndPublication);
                cacheEntry = new CacheEntry(compilation, recency.AddLast(key));
                entries.Add(key, cacheEntry);
            }
        }

        Task<CoreCLRResumableKernel> task = cacheEntry.Compilation.Value;
        try
        {
            // Cancelling one waiter never cancels a compilation shared by other dispatches.
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (task.IsFaulted)
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
            writer.Write(CacheSchema);
            writer.Write(module.AssemblyHash);
            writer.Write(module.ManifestHash);
            writer.Write(entry.GraphHash);
            writer.Write(entry.IrHash);
            writer.Write(module.ProfileId);
            writer.Write(WarpRuntimeAbi.Version);
            writer.Write(WarpRuntimeAbi.SafepointPolicy);
            writer.Write(WarpLogicalMachineLayout.Version);
            writer.Write("coreclr.resumable-native/0.1");
            writer.Write(nameof(WarpBackendKind.CoreCLR));
            writer.Write(RuntimeInformation.FrameworkDescription);
            writer.Write(RuntimeInformation.ProcessArchitecture.ToString());
            writer.Write(typeof(CoreCLRResumableKernel).Assembly.ManifestModule.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private CoreCLRResumableKernel Compile(string key, WarpRuntimeEntry entry)
    {
        byte[] canonicalPlan = WarpCoreCLRPlanCodec.Serialize(entry.Kernel);
        ValidateDiskPlan(key, canonicalPlan);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(entry.Layout);
        lock (sync)
        {
            compilationCount++;
        }

        PersistPlan(key, canonicalPlan);
        return compiled;
    }

    private void EvictCompletedEntries()
    {
        while (entries.Count >= options.MaximumMemoryEntries)
        {
            LinkedListNode<string>? node = recency.First;
            while (node is not null)
            {
                CacheEntry candidate = entries[node.Value];
                if (candidate.Compilation.IsValueCreated && candidate.Compilation.Value.IsCompleted)
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

    private void ValidateDiskPlan(string key, byte[] canonicalPlan)
    {
        if (options.DirectoryPath is null)
        {
            return;
        }

        string path = Path.Combine(options.DirectoryPath, key + ".wrcache");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length != canonicalPlan.LongLength)
            {
                return;
            }

            byte[] content = new byte[canonicalPlan.Length];
            stream.ReadExactly(content);
            if (content.AsSpan().SequenceEqual(canonicalPlan))
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
                FileInfo[] files = new DirectoryInfo(directory).GetFiles("*.wrcache");
                long bytes = files.Sum(file => file.Length);
                foreach (FileInfo file in files.OrderBy(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal))
                {
                    if (bytes <= options.MaximumDiskBytes - plan.LongLength)
                    {
                        break;
                    }

                    long length = file.Length;
                    file.Delete();
                    bytes -= length;
                }

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
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Abandoned unique temporary files are not eligible cache entries.
            }
            catch (UnauthorizedAccessException)
            {
                // A revoked cache directory cannot prevent dispatch cleanup.
            }
        }
    }

    private sealed record CacheEntry(Lazy<Task<CoreCLRResumableKernel>> Compilation, LinkedListNode<string> Recency);
}
