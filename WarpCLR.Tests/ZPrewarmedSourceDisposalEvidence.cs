using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal sealed class ZPrewarmedSourceDisposalEvidence : IAsyncDisposable
{
    private const int RoleCount = 5;
    private static readonly JsonSerializerOptions EvidenceJson = new() { WriteIndented = true };
    private readonly ConcurrentQueue<(int ProcessId, long Timestamp)> started = new();
    private readonly ConcurrentQueue<(WarpCoreCLRContainmentEvidence Identity, long Timestamp)> killed = new();
    private readonly ConcurrentQueue<WarpCoreCLRCleanupEvidence> cleanup = new();
    private readonly ConcurrentQueue<(int ProcessId, bool Stopped, long Timestamp)> heldObservations = new();
    private readonly ConcurrentDictionary<int, int> cleanupThreadOwners = new();
    private readonly ConcurrentDictionary<int, long> signalled = new();
    private readonly ConcurrentDictionary<int, WarpCoreCLRCleanupEvidence> quotas = new();
    private readonly ConcurrentDictionary<int, WarpCoreCLRCleanupEvidence> lateContainment = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource> finished = new();
    private readonly ConcurrentDictionary<int, WarpCoreCLRWorkerPidFd> held = new();
    private readonly TaskCompletionSource allSignalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource allQuotas = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource allLateContainment = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim releaseContainment = new();
    private Task<WarpCompiledPrewarmedSourceModules>? preparation;
    private Task? firstDisposal;
    private WarpCoreCLRWorkerLease[] roles = [];

    internal IReadOnlyList<(int ProcessId, long Timestamp)> Started => started.ToArray();
    internal IReadOnlyList<(WarpCoreCLRContainmentEvidence Identity, long Timestamp)> Killed => killed.ToArray();
    internal IReadOnlyDictionary<int, long> Signalled => new Dictionary<int, long>(signalled);
    internal IReadOnlyList<WarpCoreCLRCleanupEvidence> CleanupRows => cleanup.ToArray();
    internal bool ObserveHeldIdentitiesStopped()
    {
        bool allStopped = held.Count == RoleCount;
        foreach ((int process, WarpCoreCLRWorkerPidFd identity) in held)
        {
            bool stopped = identity.ObserveStopped();
            heldObservations.Enqueue((process, stopped, Stopwatch.GetTimestamp()));
            allStopped &= stopped;
        }
        return allStopped;
    }

    internal async Task<WarpCompiledPrewarmedSourceModules> PrepareAsync(WarpCompiledSourceContext context)
    {
        var probes = new WarpCoreCLRWorkerTestHooks
        {
            Started = RecordStarted, Killed = RecordKilled,
            CleanupMilestone = RecordCleanup, AfterContainmentSignal = HoldContainment,
        };
        preparation = WarpCompiledPrewarmedSourceModules.CreateAsync(context, new(), probes: probes);
        WarpCompiledPrewarmedSourceModules modules = await preparation.ConfigureAwait(false);
        roles = Roles(modules);
        foreach (WarpCoreCLRWorkerLease role in roles)
        {
            WarpCoreCLRWorkerPidFd identity = WarpCoreCLRWorkerContainment.OpenPidFd(role.ProcessId);
            if (!held.TryAdd(role.ProcessId, identity))
            { identity.Dispose(); throw new InvalidOperationException("A real prewarmed role reused another child's held identity."); }
        }
        return modules;
    }

    internal static WarpCoreCLRWorkerLease[] Roles(WarpCompiledPrewarmedSourceModules modules) =>
        [modules.Source, modules.CompareExchange, modules.Census, modules.PublicationRelease, modules.DisposeController];

    internal void RecordDisposal(Task disposal) => firstDisposal = disposal;
    internal void ReleaseContainment() => releaseContainment.Set();
    internal Task WaitForAllSignalsAsync() => allSignalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    internal Task WaitForAllQuotasAsync() => allQuotas.Task.WaitAsync(TimeSpan.FromSeconds(5));
    internal Task WaitForAllLateContainmentAsync() => allLateContainment.Task.WaitAsync(TimeSpan.FromSeconds(5));

    private void RecordStarted(int process)
    {
        started.Enqueue((process, Stopwatch.GetTimestamp()));
        finished.TryAdd(process, new(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    private void RecordKilled(WarpCoreCLRContainmentEvidence identity) => killed.Enqueue((identity, Stopwatch.GetTimestamp()));

    private void RecordCleanup(WarpCoreCLRCleanupEvidence row)
    {
        cleanup.Enqueue(row);
        if (string.Equals(row.Milestone, "census-seal-end", StringComparison.Ordinal)) { cleanupThreadOwners[row.ThreadId] = row.ProcessId; }
        if (string.Equals(row.Milestone, "finished", StringComparison.Ordinal) && finished.TryGetValue(row.ProcessId, out TaskCompletionSource? completion))
        { completion.TrySetResult(); }
        if (string.Equals(row.Milestone, "quota-exhausted", StringComparison.Ordinal))
        {
            quotas.TryAdd(row.ProcessId, row);
            if (quotas.Count == RoleCount) { allQuotas.TrySetResult(); }
        }
        if (string.Equals(row.Milestone, "late-contained-terminal-failure", StringComparison.Ordinal))
        {
            lateContainment.TryAdd(row.ProcessId, row);
            if (lateContainment.Count == RoleCount) { allLateContainment.TrySetResult(); }
        }
    }

    private void HoldContainment()
    {
        if (!cleanupThreadOwners.TryGetValue(Environment.CurrentManagedThreadId, out int process))
        { throw new InvalidOperationException("The held cleanup probe has no actual same-thread sealed child census milestone."); }
        signalled.TryAdd(process, Stopwatch.GetTimestamp());
        if (signalled.Count == RoleCount) { allSignalled.TrySetResult(); }
        releaseContainment.Wait();
    }

    public async ValueTask DisposeAsync()
    {
        // Release every actual private cleanup thread even if preparation or an assertion failed.
        ReleaseContainment();
        try { await ObserveDisposalAsync().ConfigureAwait(false); }
        finally
        {
            try
            {
                await Task.WhenAll(finished.Values.Select(static completion => completion.Task.WaitAsync(TimeSpan.FromSeconds(5))))
                    .ConfigureAwait(false);
            }
            finally
            {
                foreach (WarpCoreCLRWorkerPidFd identity in held.Values) { identity.Dispose(); }
                releaseContainment.Dispose();
            }
        }
    }

    // Join the original preparation; do not compile another set of five child modules.
    // Context-free awaits preserve the exact prepared module object and original faults.
    private static async Task<WarpCompiledPrewarmedSourceModules> JoinPreparationAsync(
        Task<WarpCompiledPrewarmedSourceModules>[] captured)
    {
        WarpCompiledPrewarmedSourceModules[] results = await Task.WhenAll(captured).ConfigureAwait(false);
        return results[0];
    }

    private async Task ObserveDisposalAsync()
    {
        if (preparation is null) { return; }
        try
        {
            WarpCompiledPrewarmedSourceModules modules = await JoinPreparationAsync([preparation]).ConfigureAwait(false);
            Task disposal = modules.DisposeAsync().AsTask();
            firstDisposal ??= disposal;
            await disposal.ConfigureAwait(false);
        }
        catch (WarpHostException error) when (string.Equals(error.Code, "WRPCORECLR3003", StringComparison.Ordinal)) { }
        catch (AggregateException error) when (error.Flatten().InnerExceptions.All(static failure =>
            failure is WarpHostException host && string.Equals(host.Code, "WRPCORECLR3003", StringComparison.Ordinal))) { }
    }

    internal async Task WriteEvidenceAsync(TestContext context, string name)
    {
        Exception[] failures = firstDisposal?.Exception?.Flatten().InnerExceptions.ToArray() ?? [];
        string evidence = JsonSerializer.Serialize(new
        {
            semantics = WarpCompiledPrewarmedSourceModules.DisposalSemantics, Stopwatch.Frequency,
            originalCleanupQuotaSeconds = 2, evidenceGuardSeconds = 5,
            preparationStatus = preparation?.Status, disposalStatus = firstDisposal?.Status,
            sourceOrIssuerAuthorityCreated = false,
            roles = roles.Select(static (role, ordinal) => new
            { ordinal, role.ProcessId, role.CompiledModule, role.IsCollectible, role.IsFaulted, role.IsReleased }),
            started = Started.Select(static row => new { row.ProcessId, row.Timestamp }),
            killed = Killed.Select(static row => new { row.Identity, row.Timestamp }),
            signalled = Signalled.Select(static row => new { ProcessId = row.Key, Timestamp = row.Value }),
            heldIdentities = heldObservations.Select(static row => new { row.ProcessId, row.Stopped, row.Timestamp }),
            cleanup = CleanupRows,
            failureCount = failures.Length, distinctFailureObjectCount = failures.Distinct<Exception>(ReferenceEqualityComparer.Instance).Count(),
            failures = failures.Select(static (failure, ordinal) => new
            { ordinal, type = failure.GetType().FullName, code = (failure as WarpHostException)?.Code, failure.Message }),
        }, EvidenceJson);
        string directory = Environment.GetEnvironmentVariable("WARP_CLEANUP_EVIDENCE") ?? context.TestRunDirectory!;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".json"), evidence).ConfigureAwait(false);
        context.WriteLine(evidence);
    }
}
