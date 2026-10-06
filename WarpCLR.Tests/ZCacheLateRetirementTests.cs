using System.Diagnostics.CodeAnalysis;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Architecture;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class ZCacheLateRetirementTests
{
    [TestMethod]
    public async Task LateCompiledWorkerRetirementFinishesBeforeShutdownCancellationEscapes()
    {
        RequireLinux();
        ZCacheLateRetirementFixture fixture = await ZCacheLateRetirementFixture.CreateAsync().ConfigureAwait(false);
        await using var owner = fixture.ConfigureAwait(false);
        AssertActualCompiledChild(fixture);
        Task shutdown = fixture.Cache.ShutdownOwnedAsync().AsTask();
        Assert.AreSame(shutdown, fixture.Cache.ShutdownOwnedAsync().AsTask());
        fixture.ReleasePublication();
        await fixture.WaitForSignalAsync().ConfigureAwait(false);
        AssertExactKilledChild(fixture, await fixture.ReadKillAsync().ConfigureAwait(false));
        Assert.IsFalse(fixture.Compilation.IsCompleted, "The captured compilation must still own its held child retirement.");
        Assert.IsFalse(shutdown.IsCompleted, "Cancellation alone cannot finish shutdown while the actual late child is held.");
        fixture.ReleaseContainment();
        await shutdown.ConfigureAwait(false);
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.JoinCompilationAsync()).ConfigureAwait(false);
        await fixture.WaitForSuccessEvidenceAsync().ConfigureAwait(false);
        Assert.IsTrue(fixture.Compilation.IsCanceled);
        Assert.IsTrue(fixture.Kernel.IsClosed); Assert.IsTrue(fixture.Kernel.HasSuccessfulContainment);
        Assert.IsTrue(fixture.HeldIdentityStopped); Assert.IsNull(fixture.Kernel.TryAcquireLease());
        Assert.IsTrue(shutdown.IsCompletedSuccessfully);
        Assert.AreEqual(0, fixture.Cache.Statistics.MemoryEntryCount);
        AssertSinglePrivateAttempt(fixture, expectSuccess: true);
        await fixture.WriteEvidenceAsync(TestContext, nameof(LateCompiledWorkerRetirementFinishesBeforeShutdownCancellationEscapes)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task LateCompiledWorkerRetirementKeepsFirstShutdownFailureAfterDeadline()
    {
        RequireLinux();
        ZCacheLateRetirementFixture fixture = await ZCacheLateRetirementFixture.CreateAsync().ConfigureAwait(false);
        await using var owner = fixture.ConfigureAwait(false);
        AssertActualCompiledChild(fixture);
        Task shutdown = fixture.Cache.ShutdownOwnedAsync().AsTask();
        Task[] callers = [fixture.Cache.ShutdownOwnedAsync().AsTask(), fixture.Cache.ShutdownOwnedAsync().AsTask()];
        foreach (Task caller in callers) { Assert.AreSame(shutdown, caller); }
        fixture.ReleasePublication();
        await fixture.WaitForSignalAsync().ConfigureAwait(false);
        AssertExactKilledChild(fixture, await fixture.ReadKillAsync().ConfigureAwait(false));
        WarpHostException failure = await Assert.ThrowsExactlyAsync<WarpHostException>(() => JoinCapturedAsync([shutdown])).ConfigureAwait(false);
        Assert.AreEqual("WRPCORECLR3003", failure.Code, StringComparer.Ordinal);
        await AssertSameFirstFailureAsync(fixture.Cache, shutdown, callers, failure).ConfigureAwait(false);
        WarpHostException retirementFailure = await Assert.ThrowsExactlyAsync<WarpHostException>(() => fixture.JoinCompilationAsync()).ConfigureAwait(false);
        Assert.AreEqual("WRPCORECLR3003", retirementFailure.Code, StringComparer.Ordinal);
        Assert.IsFalse(fixture.Compilation.IsCanceled, "The captured compilation must expose its actual failed retirement before cancellation.");
        await fixture.WaitForQuotaEvidenceAsync().ConfigureAwait(false);
        Assert.IsFalse(fixture.Kernel.HasSuccessfulContainment); Assert.IsNull(fixture.Kernel.TryAcquireLease());
        fixture.ReleaseContainment();
        await fixture.WaitForLateContainmentAsync().ConfigureAwait(false);
        Assert.IsTrue(fixture.HeldIdentityStopped); Assert.IsTrue(fixture.Kernel.IsClosed);
        Assert.IsFalse(fixture.Kernel.HasSuccessfulContainment);
        await AssertSameFirstFailureAsync(fixture.Cache, shutdown, callers, failure).ConfigureAwait(false);
        AssertSinglePrivateAttempt(fixture, expectSuccess: false);
        await fixture.WriteEvidenceAsync(TestContext, nameof(LateCompiledWorkerRetirementKeepsFirstShutdownFailureAfterDeadline)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task CachedWorkerCleanupFailurePropagatesBeforeTheFreshShutdownDeadline()
    {
        RequireLinux();
        ZCacheLateRetirementFixture fixture = await ZCacheLateRetirementFixture.CreateAsync().ConfigureAwait(false);
        await using var owner = fixture.ConfigureAwait(false);
        AssertActualCompiledChild(fixture);
        fixture.ReleasePublication();
        // The fixture retains this actual compilation-result lease and disposes
        // it in its finally path after releasing held containment.
        WarpCoreCLRWorkerLease lease = await fixture.JoinCompilationAsync().ConfigureAwait(false);
        Assert.AreEqual(fixture.Kernel.ProcessId, lease.ProcessId);
        Assert.AreEqual(fixture.Kernel.CompiledModule, lease.CompiledModule);
        Assert.IsFalse(lease.IsReleased); Assert.IsTrue(fixture.Compilation.IsCompletedSuccessfully);
        Task childDisposal = fixture.Kernel.DisposeAsync().AsTask();
        await fixture.WaitForSignalAsync().ConfigureAwait(false);
        AssertExactKilledChild(fixture, await fixture.ReadKillAsync().ConfigureAwait(false));
        WarpHostException originalFailure = await Assert.ThrowsExactlyAsync<WarpHostException>(() => JoinCapturedAsync([childDisposal])).ConfigureAwait(false);
        Assert.AreEqual("WRPCORECLR3003", originalFailure.Code, StringComparer.Ordinal);
        await fixture.WaitForQuotaEvidenceAsync().ConfigureAwait(false);
        Assert.IsFalse(fixture.Kernel.HasSuccessfulContainment);
        Task shutdown = fixture.Cache.ShutdownOwnedAsync().AsTask();
        Task[] callers = [fixture.Cache.ShutdownOwnedAsync().AsTask(), fixture.Cache.ShutdownOwnedAsync().AsTask()];
        WarpHostException cacheFailure = await Assert.ThrowsExactlyAsync<WarpHostException>(() => JoinCapturedAsync([shutdown])).ConfigureAwait(false);
        Assert.AreSame(originalFailure, cacheFailure, "The fresh group snapshot must propagate its already failed real child cleanup.");
        await AssertSameFirstFailureAsync(fixture.Cache, shutdown, callers, originalFailure).ConfigureAwait(false);
        fixture.ReleaseContainment();
        await fixture.WaitForLateContainmentAsync().ConfigureAwait(false);
        Assert.IsTrue(fixture.HeldIdentityStopped); Assert.IsTrue(fixture.Kernel.IsClosed);
        Assert.IsFalse(fixture.Kernel.HasSuccessfulContainment);
        Assert.AreSame(childDisposal, fixture.Kernel.DisposeAsync().AsTask());
        WarpHostException lateFailure = await Assert.ThrowsExactlyAsync<WarpHostException>(() => JoinCapturedAsync([childDisposal])).ConfigureAwait(false);
        Assert.AreSame(originalFailure, lateFailure);
        await AssertSameFirstFailureAsync(fixture.Cache, shutdown, callers, originalFailure).ConfigureAwait(false);
        AssertSinglePrivateAttempt(fixture, expectSuccess: false);
        await fixture.WriteEvidenceAsync(TestContext, nameof(CachedWorkerCleanupFailurePropagatesBeforeTheFreshShutdownDeadline)).ConfigureAwait(false);
    }

    // Join the captured stop without invoking it again or adding a deadline.
    // Original-task and original-exception identity assertions remain on the captured work.
    private static Task JoinCapturedAsync(Task[] captured) => Task.WhenAll(captured);

    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The actual-child identity gate requires Linux held pidfds and private process groups."); }
    }

    private static void AssertActualCompiledChild(ZCacheLateRetirementFixture fixture)
    {
        Assert.IsGreaterThan(1, fixture.StartedChild);
        Assert.AreEqual(fixture.StartedChild, fixture.Kernel.ProcessId);
        Assert.IsTrue(fixture.Kernel.IsCollectible); Assert.AreNotEqual(Guid.Empty, fixture.Kernel.CompiledModule);
        Assert.IsFalse(fixture.Kernel.IsFaulted); Assert.IsFalse(fixture.Kernel.IsClosed);
        Assert.IsFalse(fixture.HeldIdentityStopped); Assert.IsFalse(fixture.Compilation.IsCompleted);
    }

    private static void AssertExactKilledChild(ZCacheLateRetirementFixture fixture, WarpCoreCLRContainmentEvidence evidence)
    {
        Assert.AreEqual(Environment.ProcessId, evidence.ParentProcess);
        Assert.AreEqual(fixture.StartedChild, evidence.ChildProcess);
        Assert.AreEqual(evidence.ChildProcess, evidence.RegisteredPrivateGroup);
        Assert.AreEqual(evidence.ChildProcess, evidence.ChildGroup);
        Assert.AreNotEqual(evidence.ParentProcess, evidence.ChildProcess);
        Assert.AreNotEqual(evidence.ParentGroup, evidence.RegisteredPrivateGroup);
        Assert.AreEqual(9, evidence.Signal); Assert.AreEqual(0, evidence.SignalResult); Assert.AreEqual(0, evidence.SignalError);
        Assert.IsTrue(evidence.GroupSignalled); Assert.IsTrue(evidence.StableHandle);
    }

    private static void AssertSinglePrivateAttempt(ZCacheLateRetirementFixture fixture, bool expectSuccess)
    {
        IReadOnlyList<WarpCoreCLRCleanupEvidence> rows = fixture.CleanupRows;
        Assert.HasCount(1, rows.Where(static row => string.Equals(row.Milestone, "request", StringComparison.Ordinal)).ToArray());
        Assert.HasCount(1, rows.Where(static row => string.Equals(row.Milestone, "kill-start", StringComparison.Ordinal)).ToArray());
        Assert.HasCount(1, rows.Where(static row => string.Equals(row.Milestone, "core-start", StringComparison.Ordinal)).ToArray());
        Assert.HasCount(1, rows.Where(static row => string.Equals(row.Milestone, "deadline-watch-start", StringComparison.Ordinal)).ToArray());
        Assert.IsTrue(rows.Where(static row => row.Milestone is "core-start" or "kill-start" or "deadline-watch-start").All(static row => !row.ThreadPool));
        Assert.AreEqual(expectSuccess, rows.Any(static row => string.Equals(row.Milestone, "succeeded", StringComparison.Ordinal)));
        if (!expectSuccess)
        {
            Assert.IsTrue(rows.Any(static row => string.Equals(row.Milestone, "quota-exhausted", StringComparison.Ordinal)));
            Assert.IsTrue(rows.Any(static row => string.Equals(row.Milestone, "late-contained-terminal-failure", StringComparison.Ordinal)));
        }
    }

    private static async Task AssertSameFirstFailureAsync(WarpJitCache cache, Task original, Task[] callers, WarpHostException failure)
    {
        Assert.IsTrue(original.IsFaulted);
        Assert.AreSame(original, cache.ShutdownOwnedAsync().AsTask());
        foreach (Task caller in callers)
        {
            Assert.AreSame(original, caller);
            WarpHostException repeat = await Assert.ThrowsExactlyAsync<WarpHostException>(() => JoinCapturedAsync([caller])).ConfigureAwait(false);
            Assert.AreSame(failure, repeat);
        }
        WarpHostException disposalFailure = await Assert.ThrowsExactlyAsync<WarpHostException>(() => cache.DisposeAsync().AsTask()).ConfigureAwait(false);
        Assert.AreSame(failure, disposalFailure);
    }

    public TestContext TestContext { get; set; } = null!;
}
