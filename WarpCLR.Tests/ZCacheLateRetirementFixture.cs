using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Architecture;

internal sealed class ZCacheLateRetirementFixture : IAsyncDisposable
{
    private static readonly JsonSerializerOptions EvidenceJson = new() { WriteIndented = true };
    private readonly TaskCompletionSource publication = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource releasePublication = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource signalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource lateContained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource succeeded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource quotaRecorded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<WarpCoreCLRContainmentEvidence> killed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim releaseContainment = new();
    private readonly ConcurrentQueue<WarpCoreCLRCleanupEvidence> rows = new();
    private WarpCoreCLRWorkerKernel? compiled;
    private WarpCoreCLRWorkerPidFd? heldIdentity;
    private int startedChild;
    private long startedTimestamp;
    private long killedTimestamp;

    private ZCacheLateRetirementFixture(WarpRuntimeModule module)
    {
        var probes = new WarpCoreCLRWorkerTestHooks
        {
            Started = RecordStarted, Killed = RecordKilled,
            CleanupMilestone = RecordCleanup, AfterContainmentSignal = HoldContainment,
        };
        Cache = new WarpJitCache(new(), PausePublicationAsync, probes);
        Compilation = Cache.GetOrCompileAsync(module, module.Entries[ManifestAssemblyFixture.MapEntryIdentity], CancellationToken.None);
    }

    internal WarpJitCache Cache { get; }
    internal Task<WarpCoreCLRWorkerLease> Compilation { get; }
    internal WarpCoreCLRWorkerKernel Kernel => Volatile.Read(ref compiled) ?? throw new InvalidOperationException("The actual child has not reached publication.");
    internal int StartedChild => Volatile.Read(ref startedChild);
    internal IReadOnlyList<WarpCoreCLRCleanupEvidence> CleanupRows => rows.ToArray();

    internal static async Task<ZCacheLateRetirementFixture> CreateAsync()
    {
        byte[] bytes = ManifestAssemblyFixture.ReadAssembly();
        WarpRuntimeModule module = WarpRuntimeModule.Load(bytes, new WarpModuleTrust([Convert.ToHexString(SHA256.HashData(bytes))]));
        var fixture = new ZCacheLateRetirementFixture(module);
        try
        {
            Task[] readiness = [fixture.publication.Task, fixture.Compilation];
            Task ready = await Task.WhenAny(readiness).ConfigureAwait(false);
            if (ReferenceEquals(ready, fixture.Compilation)) { _ = await fixture.JoinCompilationAsync().ConfigureAwait(false); }
            await Task.WhenAll([fixture.publication.Task]).ConfigureAwait(false);
            fixture.heldIdentity = WarpCoreCLRWorkerContainment.OpenPidFd(fixture.Kernel.ProcessId);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal void ReleasePublication() => releasePublication.TrySetResult();
    internal void ReleaseContainment() => releaseContainment.Set();
    internal bool HeldIdentityStopped => heldIdentity?.ObserveStopped() ?? false;
    internal Task WaitForSignalAsync() => signalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    internal Task WaitForLateContainmentAsync() => lateContained.Task.WaitAsync(TimeSpan.FromSeconds(5));
    internal Task WaitForSuccessEvidenceAsync() => succeeded.Task.WaitAsync(TimeSpan.FromSeconds(5));
    internal Task WaitForQuotaEvidenceAsync() => quotaRecorded.Task.WaitAsync(TimeSpan.FromSeconds(5));
    internal Task<WarpCoreCLRContainmentEvidence> ReadKillAsync() => killed.Task.WaitAsync(TimeSpan.FromSeconds(5));

    // Join the actual compilation with context-free awaits; retain its exact lease or fault.
    // The join neither acquires a second lease nor starts a second worker.
    internal async Task<WarpCoreCLRWorkerLease> JoinCompilationAsync()
    {
        Task<WarpCoreCLRWorkerLease>[] captured = [Compilation];
        WarpCoreCLRWorkerLease[] results = await Task.WhenAll(captured).ConfigureAwait(false);
        return results[0];
    }

    private Task PausePublicationAsync(WarpCoreCLRWorkerKernel actual)
    {
        Volatile.Write(ref compiled, actual);
        publication.TrySetResult();
        // The join follows this existing RCAA gate; it does not restart compilation.
        return Task.WhenAll([releasePublication.Task]);
    }

    private void RecordStarted(int process)
    {
        Volatile.Write(ref startedTimestamp, Stopwatch.GetTimestamp());
        Volatile.Write(ref startedChild, process);
    }

    private void RecordKilled(WarpCoreCLRContainmentEvidence evidence)
    {
        Volatile.Write(ref killedTimestamp, Stopwatch.GetTimestamp());
        killed.TrySetResult(evidence);
    }

    private void HoldContainment()
    {
        signalled.TrySetResult();
        releaseContainment.Wait();
    }

    private void RecordCleanup(WarpCoreCLRCleanupEvidence evidence)
    {
        rows.Enqueue(evidence);
        if (string.Equals(evidence.Milestone, "finished", StringComparison.Ordinal)) { finished.TrySetResult(); }
        if (string.Equals(evidence.Milestone, "late-contained-terminal-failure", StringComparison.Ordinal)) { lateContained.TrySetResult(); }
        if (string.Equals(evidence.Milestone, "succeeded", StringComparison.Ordinal)) { succeeded.TrySetResult(); }
        if (string.Equals(evidence.Milestone, "quota-exhausted", StringComparison.Ordinal)) { quotaRecorded.TrySetResult(); }
    }

    internal async Task WriteEvidenceAsync(TestContext context, string name)
    {
        string evidence = JsonSerializer.Serialize(new
        {
            Stopwatch.Frequency, quotaSeconds = 2,
            started = new { process = StartedChild, timestamp = Volatile.Read(ref startedTimestamp) },
            killed = new { evidence = await ReadKillAsync().ConfigureAwait(false), timestamp = Volatile.Read(ref killedTimestamp) },
            Compilation.Status, Kernel.IsClosed, Kernel.IsFaulted, Kernel.HasSuccessfulContainment,
            heldIdentityStopped = HeldIdentityStopped, rows = CleanupRows,
        }, EvidenceJson);
        string directory = Environment.GetEnvironmentVariable("WARP_CLEANUP_EVIDENCE") ?? context.TestRunDirectory!;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".json"), evidence).ConfigureAwait(false);
        context.WriteLine(evidence);
    }

    public async ValueTask DisposeAsync()
    {
        ReleasePublication();
        ReleaseContainment();
        try
        {
            try { await Cache.DisposeAsync().ConfigureAwait(false); }
            catch (WarpHostException error) when (string.Equals(error.Code, "WRPCORECLR3003", StringComparison.Ordinal)) { }
        }
        finally
        {
            try
            {
                try
                {
                    WarpCoreCLRWorkerLease lease = await JoinCompilationAsync().ConfigureAwait(false);
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (WarpHostException error) when (string.Equals(error.Code, "WRPCORECLR3003", StringComparison.Ordinal)) { }
            }
            finally
            {
                try
                {
                    if (StartedChild != 0) { await finished.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                }
                finally { heldIdentity?.Dispose(); releaseContainment.Dispose(); }
            }
        }
    }
}
