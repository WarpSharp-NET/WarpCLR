using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest discovers and instantiates this internal fixture through reflection.")]
internal sealed class ZPrewarmedSourceDisposalTests
{
    [TestMethod]
    public async Task PrewarmedFiveChildDisposalKeepsEveryDistinctFailureAfterLateContainment()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The actual-child identity gate requires Linux held pidfds and private process groups."); }
        WarpCompiledSourceContext context = CreateContext();
        var evidence = new ZPrewarmedSourceDisposalEvidence();
        try { await ExerciseHeldChildrenAsync(context, evidence).ConfigureAwait(false); }
        finally
        {
            try { await evidence.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                await evidence.WriteEvidenceAsync(TestContext,
                    nameof(PrewarmedFiveChildDisposalKeepsEveryDistinctFailureAfterLateContainment)).ConfigureAwait(false);
            }
        }
    }

    private static async Task ExerciseHeldChildrenAsync(WarpCompiledSourceContext context, ZPrewarmedSourceDisposalEvidence evidence)
    {
        WarpCompiledPrewarmedSourceModules modules = await evidence.PrepareAsync(context).ConfigureAwait(false);
        WarpCoreCLRWorkerLease[] roles = ZPrewarmedSourceDisposalEvidence.Roles(modules);
        AssertPreparedChildren(context, modules, roles, evidence);
        uint[] beforeSource = context.MachineState(0).ToArray();
        Task disposal = modules.DisposeAsync().AsTask();
        evidence.RecordDisposal(disposal);
        Task[] callers = [modules.DisposeAsync().AsTask(), modules.DisposeAsync().AsTask()];
        foreach (Task caller in callers) { Assert.AreSame(disposal, caller); }
        await evidence.WaitForAllSignalsAsync().ConfigureAwait(false);
        AssertKilledIdentities(roles, evidence);
        AggregateException failure = await Assert.ThrowsExactlyAsync<AggregateException>(() => JoinCapturedAsync([disposal])).ConfigureAwait(false);
        await evidence.WaitForAllQuotasAsync().ConfigureAwait(false);
        Exception[] originals = AssertCompleteDistinctFailures(failure);
        AssertDeniedSource(context, modules, roles, beforeSource);
        await AssertStableFirstOutcomeAsync(modules, disposal, callers, failure, originals).ConfigureAwait(false);
        evidence.ReleaseContainment();
        await evidence.WaitForAllLateContainmentAsync().ConfigureAwait(false);
        Assert.IsTrue(evidence.ObserveHeldIdentitiesStopped());
        AssertPrivateAttempts(roles, evidence);
        AssertDeniedSource(context, modules, roles, beforeSource);
        await AssertStableFirstOutcomeAsync(modules, disposal, callers, failure, originals).ConfigureAwait(false);
    }

    // Join the already captured five-child disposal without invoking disposal again.
    // The assertions still compare the original task and every original failure object.
    private static Task JoinCapturedAsync(Task[] captured) => Task.WhenAll(captured);

    private static void AssertPreparedChildren(WarpCompiledSourceContext context, WarpCompiledPrewarmedSourceModules modules,
        WarpCoreCLRWorkerLease[] roles, ZPrewarmedSourceDisposalEvidence evidence)
    {
        Assert.HasCount(5, roles);
        Assert.AreEqual(5, roles.Select(static role => role.ProcessId).Distinct().Count());
        Assert.AreEqual(5, roles.Select(static role => role.CompiledModule).Distinct().Count());
        Assert.HasCount(5, evidence.Started);
        foreach (WarpCoreCLRWorkerLease role in roles)
        {
            Assert.HasCount(1, evidence.Started.Where(row => row.ProcessId == role.ProcessId).ToArray());
            Assert.IsGreaterThan(1, role.ProcessId); Assert.AreNotEqual(Guid.Empty, role.CompiledModule);
            Assert.IsTrue(role.IsCollectible); Assert.IsFalse(role.IsFaulted); Assert.IsFalse(role.IsReleased);
        }
        Assert.IsFalse(evidence.ObserveHeldIdentitiesStopped());
        Assert.AreSame(modules, context.RequirePreparedSourceModules());
        Assert.IsFalse(context.Plan.HasLocalSourceKernel); Assert.AreEqual(0u, context.WorkerQuanta(0));
    }

    private static void AssertKilledIdentities(WarpCoreCLRWorkerLease[] roles, ZPrewarmedSourceDisposalEvidence evidence)
    {
        Assert.HasCount(5, evidence.Killed); Assert.HasCount(5, evidence.Signalled);
        foreach (WarpCoreCLRWorkerLease role in roles)
        {
            var rows = evidence.Killed.Where(row => row.Identity.ChildProcess == role.ProcessId).ToArray();
            Assert.HasCount(1, rows);
            WarpCoreCLRContainmentEvidence identity = rows[0].Identity;
            Assert.AreEqual(Environment.ProcessId, identity.ParentProcess);
            Assert.AreEqual(role.ProcessId, identity.ChildGroup); Assert.AreEqual(role.ProcessId, identity.RegisteredPrivateGroup);
            Assert.AreNotEqual(identity.ParentGroup, identity.RegisteredPrivateGroup);
            Assert.AreEqual(9, identity.Signal); Assert.AreEqual(0, identity.SignalResult); Assert.AreEqual(0, identity.SignalError);
            Assert.IsTrue(identity.GroupSignalled); Assert.IsTrue(identity.StableHandle);
        }
    }

    private static Exception[] AssertCompleteDistinctFailures(AggregateException failure)
    {
        Exception[] errors = failure.Flatten().InnerExceptions.ToArray();
        Assert.HasCount(5, errors, "Stop and lease views must retain five child failures, without duplicating the same object.");
        Assert.AreEqual(5, errors.Distinct<Exception>(ReferenceEqualityComparer.Instance).Count());
        foreach (Exception error in errors)
        {
            Assert.IsInstanceOfType<WarpHostException>(error);
            Assert.AreEqual("WRPCORECLR3003", ((WarpHostException)error).Code, StringComparer.Ordinal);
        }
        return errors;
    }

    private static void AssertDeniedSource(WarpCompiledSourceContext context, WarpCompiledPrewarmedSourceModules modules,
        WarpCoreCLRWorkerLease[] roles, uint[] beforeSource)
    {
        Assert.IsNull(context.Claim(0));
        Assert.ThrowsExactly<InvalidOperationException>(() => context.RequirePreparedSourceModules());
        Assert.ThrowsExactly<InvalidOperationException>(() => modules.Validate(context));
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Result(0, context.Dispatch));
        Assert.AreEqual(0u, context.WorkerQuanta(0)); Assert.IsFalse(context.Plan.HasLocalSourceKernel);
        CollectionAssert.AreEqual(beforeSource, context.MachineState(0).ToArray());
        Assert.IsTrue(roles.All(static role => role.IsFaulted && role.IsReleased));
    }

    private static async Task AssertStableFirstOutcomeAsync(WarpCompiledPrewarmedSourceModules modules, Task original,
        Task[] callers, AggregateException failure, Exception[] originalErrors)
    {
        Assert.IsTrue(original.IsFaulted); Assert.IsFalse(original.IsCompletedSuccessfully);
        Assert.AreSame(original, modules.DisposeAsync().AsTask());
        foreach (Task caller in callers.Append(modules.DisposeAsync().AsTask()))
        {
            Assert.AreSame(original, caller);
            AggregateException repeated = await Assert.ThrowsExactlyAsync<AggregateException>(() => JoinCapturedAsync([caller])).ConfigureAwait(false);
            Assert.AreSame(failure, repeated);
            Exception[] errors = AssertCompleteDistinctFailures(repeated);
            for (int i = 0; i < errors.Length; i++) { Assert.AreSame(originalErrors[i], errors[i]); }
        }
    }

    private static void AssertPrivateAttempts(WarpCoreCLRWorkerLease[] roles, ZPrewarmedSourceDisposalEvidence evidence)
    {
        foreach (WarpCoreCLRWorkerLease role in roles)
        {
            WarpCoreCLRCleanupEvidence[] rows = evidence.CleanupRows.Where(row => row.ProcessId == role.ProcessId).ToArray();
            foreach (string name in new[] { "request", "core-start", "deadline-watch-start", "kill-start", "census-seal-end", "quota-exhausted", "finished" })
            { Assert.HasCount(1, rows.Where(row => string.Equals(row.Milestone, name, StringComparison.Ordinal)).ToArray()); }
            Assert.IsTrue(rows.Where(static row => row.Milestone is "core-start" or "deadline-watch-start" or "kill-start")
                .All(static row => !row.ThreadPool));
            Assert.IsFalse(rows.Any(static row => string.Equals(row.Milestone, "succeeded", StringComparison.Ordinal)));
            Assert.IsTrue(rows.Any(static row => string.Equals(row.Milestone, "late-contained-terminal-failure", StringComparison.Ordinal)));
            long request = rows.Single(static row => string.Equals(row.Milestone, "request", StringComparison.Ordinal)).Timestamp;
            long quota = rows.Single(static row => string.Equals(row.Milestone, "quota-exhausted", StringComparison.Ordinal)).Timestamp;
            Assert.IsTrue(Stopwatch.GetElapsedTime(request, quota) >= TimeSpan.FromSeconds(2));
        }
    }

    private static WarpCompiledSourceContext CreateContext()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Guest).GetMethod(nameof(Guest.AddOne))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed);
        var plan = new WarpCompiledSourcePlan(graph, schema, program, 1, 1, 16, 100000, 1, new());
        WarpCompiledSourceEvidence.Capture(plan);
        uint roots = checked(plan.Workers * ((uint)plan.RootTupleCount + 1 + (uint)plan.ResultRootWords.Length) + 4);
        uint[] arena = plan.TypeSchema.CreateArena(WarpLogicalOwnerNamespace.Next(), 1024, 64, roots, plan.Workers, 1024);
        return new(plan, arena, [[7]]);
    }

    private static class Guest
    {
        public static int AddOne(int value) => value + 1;
    }

    public TestContext TestContext { get; set; } = null!;
}
