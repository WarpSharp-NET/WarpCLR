using System.Runtime.InteropServices;
using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableCollectiveDriver
{
    internal uint Run(bool parallel = false)
    {
        Assert.AreEqual(0u, Control(nameof(WarpPortableCollectiveServices.BeginRun), Arena[60]));
        while (Arena[Descriptor + 25] < Plan.Levels)
        {
            RunPhase(parallel);
            Assert.AreEqual(0u, Control(nameof(WarpPortableCollectiveServices.AdvancePhase)));
        }
        return Control(nameof(WarpPortableCollectiveServices.FinishRun));
    }

    private void RunPhase(bool parallel)
    {
        uint phase = Arena[Descriptor + 15] + Arena[Descriptor + 25] * 2;
        while (Arena[Descriptor + 26] < Arena[phase + 1])
        {
            var batch = new List<Job>(residents);
            for (int resident = 0; resident < residents && Arena[Descriptor + 26] < Arena[phase + 1]; resident++)
            {
                uint worker = workerCursor++ % Arena[Descriptor + 4];
                uint member = Arena[Arena[Descriptor + 17] + worker];
                Assert.AreEqual(0u, Control(nameof(WarpPortableCollectiveServices.TryAcquireNode), worker, member, run));
                uint entry = Arena[Descriptor + 18] + worker * 8;
                batch.Add(new(worker, Arena[entry + 3], run++));
            }
            if (batch.Count > 1) { Interlocked.Add(ref simultaneousJobs, batch.Count); }
            ComputeBatch(batch, parallel);
            for (int i = batch.Count - 1; i >= 0; i--)
            {
                Job job = batch[i];
                Assert.AreEqual(0u, Control(nameof(WarpPortableCollectiveServices.CompleteNode), job.Worker, Generation, job.Token, job.Run));
            }
        }
    }

    private void ComputeBatch(List<Job> batch, bool parallel)
    {
        if (!generated)
        {
            for (int i = batch.Count - 1; i >= 0; i--)
            {
                Job job = batch[i];
                Assert.AreEqual(0u, Execute(nameof(WarpPortableCollectiveServices.ComputeNode), [Descriptor, job.Worker, Generation, job.Token]));
            }
            return;
        }
        Invocation[] invocations = new Invocation[batch.Count];
        for (int i = 0; i < batch.Count; i++)
        {
            Job job = batch[i];
            invocations[i] = CreateInvocation(nameof(WarpPortableCollectiveServices.ComputeNode), [Descriptor, job.Worker, Generation, job.Token]);
        }
        if (parallel)
        {
            Parallel.For(0, invocations.Length, i =>
            {
                while (invocations[i].State[0] == 0) { Tick(invocations[i]); }
                Assert.AreEqual(0u, Result(invocations[i]));
            });
            return;
        }
        while (invocations.Any(static invocation => invocation.State[0] == 0))
        {
            for (int i = invocations.Length - 1; i >= 0; i--)
            {
                if (invocations[i].State[0] == 0) { Tick(invocations[i]); }
            }
        }
        foreach (ref readonly Invocation invocation in invocations.AsSpan()) { Assert.AreEqual(0u, Result(invocation)); }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Job(uint Worker, uint Token, uint Run);
}
