using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using WarpCLR.Backend.CoreCLR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerProcess : IAsyncDisposable
{
    private readonly Lock sync = new();
    private readonly Process process;
    private readonly int processId;
    private readonly CancellationToken lifetimeToken;
    private readonly WarpCoreCLRWorkerContainmentLease containment;
    private readonly WarpCoreCLRWorkerCleanupAttempt cleanup;
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    private readonly WarpCoreCLRWorkerOptions options;
    private readonly CancellationTokenSource lifetime = new();
    private readonly WarpCoreCLRAsyncGate transactions = new();
    private Task? disposal;
    private ulong sequence = 1;
    private volatile bool faulted;
    private volatile bool closed;
    private bool lifetimeDisposed;
    private WarpCoreCLRWorkerTestHooks? probes;

    private WarpCoreCLRWorkerProcess(Process process, WarpCoreCLRWorkerContainmentLease containment, WarpCoreCLRWorkerOptions options,
        WarpCoreCLRWorkerTestHooks? probes)
    {
        this.process = process; processId = process.Id; this.containment = containment; this.options = options;
        lifetimeToken = lifetime.Token; this.probes = probes;
        cleanup = new(options.CleanupTimeout, DisposeCore, RecordCleanup);
        cleanup.Prepare();
    }

    internal bool IsClosed => closed;
    internal bool HasSuccessfulContainment => closed && cleanup.Completion.IsCompletedSuccessfully;
    internal int ProcessId => processId;
    internal bool IsFaulted => faulted;
    internal string IrHash { get; private set; } = string.Empty;
    internal Guid CompiledModule { get; private set; }
    internal bool IsCollectible { get; private set; }

    internal static async Task<WarpCoreCLRWorkerProcess> CompileAsync(byte[] plan, string irHash,
        WarpCoreCLRWorkerOptions options, CancellationToken cancellationToken, WarpCoreCLRWorkerTestHooks? probes = null)
    {
        WarpCoreCLRWorkerOptions.Validate(options);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.CompilationTimeout);
        string assembly = options.WorkerAssemblyPath ?? Path.Combine(AppContext.BaseDirectory, "WarpCLR.CoreCLR.Worker.dll");
        if (!string.Equals(Path.GetFileName(assembly), "WarpCLR.CoreCLR.Worker.dll", StringComparison.Ordinal))
        { throw new InvalidDataException("The worker launch must name its authenticated deployed worker artifact."); }
        byte[] deployment = WarpCoreCLRWorkerIdentity.DeploymentDigest(Path.GetDirectoryName(assembly)!);
        ValidateDependencies(assembly);
        WarpCoreCLRWorkerProcess worker = Start(assembly, options, probes);
        worker.probes = probes;
        probes?.Started?.Invoke(worker.ProcessId);
        try
        {
            byte[] bootstrap = new byte[65]; worker.key.CopyTo(bootstrap, 0); deployment.CopyTo(bootstrap, 32);
            bootstrap[64] = probes?.Probe ?? 0;
            await worker.process.StandardInput.BaseStream.WriteAsync(bootstrap, deadline.Token).ConfigureAwait(false);
            await worker.process.StandardInput.BaseStream.FlushAsync(deadline.Token).ConfigureAwait(false);
            WarpCoreCLRWorkerProtocol.Frame hello = await WarpCoreCLRWorkerProtocol.ReadAsync(worker.process.StandardOutput.BaseStream, worker.key, 0, deadline.Token).ConfigureAwait(false);
            if (hello.Kind != WarpCoreCLRWorkerProtocol.Hello) { throw new InvalidDataException("Worker handshake missing."); }
            WarpCoreCLRWorkerIdentity.ValidateHello(hello.Payload, deployment, worker.ProcessId, WarpCoreCLRWorkerIdentity.ReadWorkerModule(assembly), GetDotnetHost(options));
            worker.containment.RegisterPrivateGroup(worker.process);
            worker.IrHash = irHash;
            await worker.CompilePlanAsync(plan, irHash, probes, deadline.Token).ConfigureAwait(false);
            return worker;
        }
        catch (Exception error)
        {
            await worker.DisposeAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw new WarpHostException("WRPCORECLR3001", "The bounded CoreCLR worker compilation failed; no module was admitted.", error);
        }
    }

    public ValueTask DisposeAsync()
    {
        long request = Stopwatch.GetTimestamp();
        lock (sync)
        {
            faulted = true;
            if (disposal is null) { disposal = cleanup.Completion; cleanup.Start(request); }
            if (closed && !lifetimeDisposed) { lifetimeDisposed = true; lifetime.Dispose(); }
            cleanup.Dispose();
            return new(disposal);
        }
    }

    private void RecordCleanup(string milestone) => probes?.CleanupMilestone?.Invoke(new(processId, milestone, Stopwatch.GetTimestamp(), Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread));

    private void DisposeCore(WarpCoreCLRWorkerCleanupAttempt attempt)
    {
        try
        {
            SignalAndSealStoppedCensus(attempt);
            RecordCleanup("cancel-start");
            lifetime.Cancel();
            RecordCleanup("cancel-end");
            RecordCleanup("pipes-start");
            process.StandardOutput.Dispose(); process.StandardError.Dispose();
            RecordCleanup("pipes-end");
            RecordCleanup("input-start");
            // IPC writes bytes directly to this pipe. Disposing the unused
            // StreamWriter would flush a killed child's broken pipe instead.
            process.StandardInput.BaseStream.Dispose();
            RecordCleanup("input-end");
            RecordCleanup("exit-start");
            int milliseconds = (int)Math.Clamp(Math.Ceiling(attempt.Remaining.TotalMilliseconds), 0, int.MaxValue);
            if (milliseconds > 0 && !process.WaitForExit(milliseconds)) { throw new TimeoutException("The terminated CoreCLR child did not exit within the shared cleanup quota."); }
            if (milliseconds == 0) { RecordCleanup("exit-observe-held-identities-only"); }
            RecordCleanup("exit-end");
            RecordCleanup("group-exit-start");
            containment.WaitStopped(attempt);
            RecordCleanup("group-exit-end");
            lock (sync) { closed = true; }
            RetireClosedUnownedCommands(this);
            RecordCleanup("contained");
            process.Dispose(); containment.Dispose();
        }
        finally
        {
            lock (sync) { if (!lifetimeDisposed) { lifetimeDisposed = true; lifetime.Dispose(); } }
            RecordCleanup("finished");
        }
    }

    private void SignalAndSealStoppedCensus(WarpCoreCLRWorkerCleanupAttempt attempt)
    {
        if (attempt.Remaining <= TimeSpan.Zero) { throw new TimeoutException("The original cleanup quota expired before its sole containment signal could start."); }
        RecordCleanup("kill-start");
        containment.Kill(process, probes);
        RecordCleanup("kill-end");
        RecordCleanup("census-seal-start");
        containment.SealStoppedCensus(attempt);
        RecordCleanup("census-seal-end");
        probes?.AfterContainmentSignal?.Invoke();
    }

    private static WarpCoreCLRWorkerProcess Start(string assembly, WarpCoreCLRWorkerOptions options, WarpCoreCLRWorkerTestHooks? probes)
    {
        WarpCoreCLRWorkerContainment.Preflight(probes);
        string host = GetDotnetHost(options);
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(assembly);
        foreach (string name in start.Environment.Keys.Where(name => name is not null &&
            (name.StartsWith("CORECLR_", StringComparison.Ordinal) || name.StartsWith("COR_", StringComparison.Ordinal) ||
             name is "DOTNET_STARTUP_HOOKS" or "DOTNET_ADDITIONAL_DEPS" or "DOTNET_SHARED_STORE")).ToArray())
        { start.Environment.Remove(name); }
        start.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(host);
        if (probes?.Environment is { } environment)
        { foreach ((string name, string value) in environment) { start.Environment[name] = value; } }
        Process child = Process.Start(start) ?? throw new IOException("CoreCLR worker could not start.");
        WarpCoreCLRWorkerContainmentLease? lease = null;
        try
        {
            lease = WarpCoreCLRWorkerContainment.Attach(child);
            return new WarpCoreCLRWorkerProcess(child, lease, options, probes);
        }
        catch
        {
            if (lease is not null) { lease.Kill(child, probes); lease.Dispose(); }
            else if (!child.HasExited) { child.Kill(); }
            child.Dispose(); throw;
        }
    }

    internal static string GetDotnetHost(WarpCoreCLRWorkerOptions options) => options.DotnetHostPath ??
        Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));

    private static void ValidateDependencies(string assembly)
    {
        string directory = Path.GetDirectoryName(assembly)!;
        foreach (string loaded in new[] { typeof(CoreCLRResumableKernel).Assembly.Location, typeof(IR.WarpControlFlowKernel).Assembly.Location })
        {
            using var expected = new FileStream(loaded, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var actual = new FileStream(Path.Combine(directory, Path.GetFileName(loaded)), FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(expected), SHA256.HashData(actual)))
            { throw new InvalidDataException("Worker dependency differs from the admitted parent module."); }
        }
    }
}
