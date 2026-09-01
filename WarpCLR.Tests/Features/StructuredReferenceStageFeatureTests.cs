using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Features;

[TestClass]
public sealed class StructuredReferenceStageFeatureTests
{
    [TestMethod]
    [FourBackends]
    public void Stage_join_precedes_dependent_work(WarpBackendKind backend)
    {
        var values = new int[257];
        var observedIncompleteValue = 0;
        var program = new WarpStructuredReferenceProgram(
        [
            new(
                "produce",
                values.Length,
                index => values[index] = index + 1),
            new(
                "consume",
                values.Length,
                index =>
                {
                    if (values[index] != index + 1)
                    {
                        Interlocked.Exchange(ref observedIncompleteValue, 1);
                    }

                    values[index] *= 2;
                }),
        ]);

        CreateSession(backend).Execute(program);

        Assert.AreEqual(0, observedIncompleteValue);
        CollectionAssert.AreEqual(
            Enumerable.Range(1, values.Length).Select(value => value * 2).ToArray(),
            values);
    }

    [TestMethod]
    [FourBackends]
    public void Each_parallel_work_item_executes_once(WarpBackendKind backend)
    {
        var counts = new int[1025];
        var program = new WarpStructuredReferenceProgram(
        [
            new(
                "count",
                counts.Length,
                index => Interlocked.Increment(ref counts[index])),
        ]);

        CreateSession(backend).Execute(program);

        Assert.IsTrue(counts.All(count => count == 1));
    }

    [TestMethod]
    [FourBackends]
    public void Lowest_failed_work_item_is_authoritative(WarpBackendKind backend)
    {
        var nextStageCount = 0;
        var program = new WarpStructuredReferenceProgram(
        [
            new(
                "fault",
                16,
                index =>
                {
                    if (index is 7 or 3)
                    {
                        throw new InvalidOperationException($"failure-{index}");
                    }
                }),
            new(
                "must-not-run",
                1,
                _ => Interlocked.Increment(ref nextStageCount)),
        ]);

        WarpStructuredExecutionException exception = Assert.ThrowsExactly<
            WarpStructuredExecutionException>(
                () => CreateSession(backend).Execute(program));

        Assert.AreEqual("WRPHOST1100", exception.Code);
        Assert.AreEqual("fault", exception.StageIdentity);
        Assert.AreEqual(3, exception.WorkItemIndex);
        Assert.AreEqual("failure-3", exception.InnerException?.Message);
        Assert.AreEqual(0, nextStageCount);
    }

    [TestMethod]
    [FourBackends]
    public void Empty_stage_preserves_the_next_stage(WarpBackendKind backend)
    {
        var value = 0;
        var program = new WarpStructuredReferenceProgram(
        [
            new("empty", 0, _ => value = -1),
            new("next", 1, _ => value = 17),
        ]);

        CreateSession(backend, maximumConcurrency: 1).Execute(program);

        Assert.AreEqual(17, value);
    }

    private static WarpStructuredReferenceSession CreateSession(
        WarpBackendKind backend,
        int maximumConcurrency = -1) => new(
            backend,
            backend == WarpBackendKind.CpuReference
                ? WarpDevelopmentExecutionMode.CoreClrReference
                : WarpDevelopmentExecutionMode.SemanticEmulation,
            maximumConcurrency);
}
