using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableCollectiveDriver
{
    private Invocation? paused;

    internal uint PauseNode(uint worker)
    {
        Assert.IsTrue(generated);
        Assert.IsNull(paused);
        uint entry = Arena[Descriptor + 18] + worker * 8;
        paused = CreateInvocation(nameof(WarpPortableCollectiveServices.ComputeNode), [Descriptor, worker, Generation, Arena[entry + 3]]);
        paused.Machine.Core.ExecuteManagedQuantum(paused.Inputs, [], 0, paused.State, 16, paused.Machine.Layout.MaximumBlockCost, Arena);
        Interlocked.Increment(ref generatedQuanta);
        return paused.State[0];
    }

    internal uint ResumePausedNode()
    {
        Assert.IsNotNull(paused);
        while (paused.State[0] == 0) { Tick(paused); }
        uint result = Result(paused);
        paused = null;
        return result;
    }

    internal void StopPausedNode()
    {
        Assert.IsNotNull(paused);
        paused = null;
    }
}
