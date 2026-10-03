using WarpCLR.IR;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpNativeExecutionProvider : IAsyncDisposable
{
    private readonly Lock sync = new();
    private readonly WarpRuntimeModule module;
    private readonly WarpJitCache jitCache;
    private readonly WarpNativeRuntimeOptions options;
    private readonly Func<IWarpNativeDriver> openDriver;
    private readonly Func<WarpNativeTarget, CancellationToken, Task<string>> identifyToolchain;
    private readonly Func<WarpLogicalMachineLayout, WarpNativeTarget, CancellationToken, Task<WarpNativeImage>> compileImage;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, Task<IWarpNativeModule>> modules = new(StringComparer.Ordinal);
    private Task<string>? toolchainIdentity;
    private Task? disposal;
    private IWarpNativeDriver? driver;
    private bool disposed;

    public WarpNativeExecutionProvider(WarpRuntimeModule module, WarpJitCache jitCache, WarpBackendKind backend, WarpNativeRuntimeOptions options)
        : this(module, jitCache, options, CreateDriverFactory(backend, options), CreateToolchain(options))
    {
    }

    private WarpNativeExecutionProvider(WarpRuntimeModule module, WarpJitCache jitCache, WarpNativeRuntimeOptions options,
        Func<IWarpNativeDriver> openDriver, WarpNativeToolchain toolchain)
        : this(module, jitCache, options, openDriver, toolchain.GetMachineToolchainIdentityAsync, toolchain.CompileMachineAsync)
    {
    }

    internal WarpNativeExecutionProvider(WarpRuntimeModule module, WarpJitCache jitCache, WarpNativeRuntimeOptions options,
        Func<IWarpNativeDriver> openDriver, Func<WarpNativeTarget, CancellationToken, Task<string>> identifyToolchain,
        Func<WarpLogicalMachineLayout, WarpNativeTarget, CancellationToken, Task<WarpNativeImage>> compileImage)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(jitCache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(openDriver);
        ArgumentNullException.ThrowIfNull(identifyToolchain);
        ArgumentNullException.ThrowIfNull(compileImage);
        WarpNativeRuntimeOptions.Validate(options);
        this.module = module;
        this.jitCache = jitCache;
        this.options = options;
        this.openDriver = openDriver;
        this.identifyToolchain = identifyToolchain;
        this.compileImage = compileImage;
    }

    public async Task<IWarpNativeModule> GetOrCompileAsync(WarpRuntimeEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        if (!module.Entries.TryGetValue(entry.Identity, out WarpRuntimeEntry? bound) || !ReferenceEquals(entry, bound))
        {
            throw new WarpHostException("WRPRUNTIME1004", "The native entry is not part of this context's verified module.");
        }

        Task<IWarpNativeModule> task;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            RemoveFailedModules();
            if (modules.TryGetValue(entry.IrHash, out task!))
            {
                jitCache.RecordNativeModuleMemoryHit();
            }
            else
            {
                if (modules.Count >= options.MaximumCachedModules)
                {
                    throw new WarpHostException("WRPRUNTIME1002", "The context's resident native-module cache cannot admit another module.");
                }

                CancellationToken contextToken = lifetime.Token;
                task = Task.Run(() => LoadAsync(entry, contextToken));
                modules.Add(entry.IrHash, task);
            }
        }

        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (task.IsFaulted || task.IsCanceled)
        {
            lock (sync)
            {
                if (modules.TryGetValue(entry.IrHash, out Task<IWarpNativeModule>? current) && ReferenceEquals(current, task))
                {
                    modules.Remove(entry.IrHash);
                }
            }

            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            disposal ??= DisposeCoreAsync();
            return new ValueTask(disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Task[] pending;
        lock (sync)
        {
            disposed = true;
            pending = toolchainIdentity is null ? [.. modules.Values] : [.. modules.Values, toolchainIdentity];
        }

        Task drain = Task.WhenAll(pending);
        try
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            await drain.ConfigureAwait(false);
        }
        catch (Exception error) when (error is WarpHostException or OperationCanceledException or IOException or
            ObjectDisposedException or PlatformNotSupportedException or WarpCompilationResourceException)
        {
            // Expected operational faults were delivered to dispatch waiters; programming faults still escape disposal.
            if (drain.Exception is { } faults && faults.InnerExceptions.Any(exception => exception is not WarpHostException and
                not OperationCanceledException and not IOException and not ObjectDisposedException and not PlatformNotSupportedException and
                not WarpCompilationResourceException))
            {
                throw faults;
            }
        }
        finally
        {
            try { driver?.Dispose(); }
            finally
            {
                lock (sync)
                {
                    driver = null;
                    modules.Clear();
                    toolchainIdentity = null;
                }

                pending = [];
                drain = Task.CompletedTask;
                lifetime.Dispose();
            }
        }
    }

    private async Task<IWarpNativeModule> LoadAsync(WarpRuntimeEntry entry, CancellationToken contextToken)
    {
        contextToken.ThrowIfCancellationRequested();
        IWarpNativeDriver native;
        Task<string> identity;
        lock (sync)
        {
            ThrowIfShutdown(contextToken);
            native = driver ??= openDriver();
            identity = toolchainIdentity ??= Task.Run(() => identifyToolchain(native.Target, contextToken));
        }

        WarpNativeTarget target = native.Target;
        string toolIdentity;
        try { toolIdentity = await identity.WaitAsync(contextToken).ConfigureAwait(false); }
        catch when (identity.IsFaulted || identity.IsCanceled)
        {
            lock (sync)
            {
                if (ReferenceEquals(toolchainIdentity, identity)) { toolchainIdentity = null; }
            }

            throw;
        }
        WarpNativeImage image = await jitCache.GetOrCompileNativeAsync(module, entry, target, toolIdentity,
            token => compileImage(entry.Layout, target, token), contextToken).ConfigureAwait(false);
        lock (sync)
        {
            ThrowIfShutdown(contextToken);
            return native.Load(image);
        }
    }

    private void ThrowIfShutdown(CancellationToken contextToken)
    {
        contextToken.ThrowIfCancellationRequested();
        if (disposed) { throw new OperationCanceledException("The native context is shutting down.", contextToken); }
    }

    private void RemoveFailedModules()
    {
        string[] failed = modules.Where(pair => pair.Value.IsFaulted || pair.Value.IsCanceled).Select(pair => pair.Key).ToArray();
        foreach (string key in failed)
        {
            _ = modules[key].Exception;
            modules.Remove(key);
        }
    }

    private static Func<IWarpNativeDriver> CreateDriverFactory(WarpBackendKind backend, WarpNativeRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return () => backend switch
        {
            WarpBackendKind.NVPTX => WarpCudaNativeDriver.Open(options.DeviceOrdinal, options.DriverLibraryPath),
            WarpBackendKind.AMDGPU => WarpHipNativeDriver.Open(options.DeviceOrdinal, options.DriverLibraryPath),
            WarpBackendKind.SPIRV => WarpOpenClNativeDriver.Open(options.DeviceOrdinal, options.DriverLibraryPath),
            _ => throw new InvalidOperationException("The registered backend has no native driver factory."),
        };
    }

    private static WarpNativeToolchain CreateToolchain(WarpNativeRuntimeOptions options) => new(new WarpNativeToolchainOptions
    {
        LlvmAssembler = options.LlvmAssemblerPath,
        LlvmCodeGenerator = options.LlvmCodeGeneratorPath,
        LlvmLinker = options.LlvmLinkerPath,
        SpirVTranslator = options.SpirVTranslatorPath,
        SpirVValidator = options.SpirVValidatorPath,
        ProcessTimeout = options.CompilationTimeout,
    });
}
