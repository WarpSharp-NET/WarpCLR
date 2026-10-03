using WarpCLR.IR;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpNativeExecutionProvider : IAsyncDisposable
{
    private readonly Lock sync = new();
    private readonly WarpBackendKind backend;
    private readonly WarpNativeRuntimeOptions options;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, Lazy<Task<IWarpNativeModule>>> modules = new(StringComparer.Ordinal);
    private IWarpNativeDriver? driver;
    private bool disposed;

    public WarpNativeExecutionProvider(WarpBackendKind backend, WarpNativeRuntimeOptions options)
    {
        this.backend = backend;
        this.options = options;
    }

    public async Task<IWarpNativeModule> GetOrCompileAsync(WarpRuntimeEntry entry, CancellationToken cancellationToken)
    {
        Lazy<Task<IWarpNativeModule>> compilation;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!modules.TryGetValue(entry.IrHash, out compilation!))
            {
                if (modules.Count >= options.MaximumCachedModules)
                {
                    throw new WarpHostException("WRPRUNTIME1002", "The context's resident native-module cache cannot admit another compilation.");
                }

                compilation = new Lazy<Task<IWarpNativeModule>>(() => CompileAsync(entry), LazyThreadSafetyMode.ExecutionAndPublication);
                modules.Add(entry.IrHash, compilation);
            }
        }

        Task<IWarpNativeModule> task = compilation.Value;
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (task.IsFaulted)
        {
            lock (sync)
            {
                if (modules.TryGetValue(entry.IrHash, out Lazy<Task<IWarpNativeModule>>? current) && ReferenceEquals(current, compilation))
                {
                    modules.Remove(entry.IrHash);
                }
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task<IWarpNativeModule>[] pending;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pending = modules.Values.Where(value => value.IsValueCreated).Select(value => value.Value).ToArray();
        }

        await lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WarpHostException or OperationCanceledException or IOException)
        {
            // Compilation faults were delivered to their waiters; all owned driver objects are still released.
        }
        finally
        {
            driver?.Dispose();
            lifetime.Dispose();
        }
    }

    private async Task<IWarpNativeModule> CompileAsync(WarpRuntimeEntry entry)
    {
        IWarpNativeDriver native;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            driver ??= backend switch
            {
                WarpBackendKind.NVPTX => WarpCudaNativeDriver.Open(options.DeviceOrdinal, options.DriverLibraryPath),
                WarpBackendKind.AMDGPU => WarpHipNativeDriver.Open(options.DeviceOrdinal, options.DriverLibraryPath),
                WarpBackendKind.SPIRV => WarpOpenClNativeDriver.Open(options.DeviceOrdinal, options.DriverLibraryPath),
                _ => throw new ArgumentOutOfRangeException(nameof(backend)),
            };
            native = driver;
        }

        var toolchain = new WarpNativeToolchain(new WarpNativeToolchainOptions
        {
            LlvmAssembler = options.LlvmAssemblerPath,
            LlvmCodeGenerator = options.LlvmCodeGeneratorPath,
            LlvmLinker = options.LlvmLinkerPath,
            SpirVTranslator = options.SpirVTranslatorPath,
            SpirVValidator = options.SpirVValidatorPath,
            ProcessTimeout = options.CompilationTimeout,
        });
        WarpNativeImage image = await toolchain.CompileMachineAsync(entry.Layout, native.Target, lifetime.Token).ConfigureAwait(false);
        image.RequireProductionAdmission();
        lifetime.Token.ThrowIfCancellationRequested();
        return native.Load(image);
    }
}
