using WarpCLR.Compiler;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingWarmControllerModulesRejectBeforeClaimOrChildCommand(bool redispatch)
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 5, 2, [Enumerable.Repeat(1u, 5).ToArray()]);
        RemoteModules modules = await RemoteModules.CreateAsync(context, new()).ConfigureAwait(false);
        await using var ownedModules = modules.ConfigureAwait(false);
        RemoteGenerationModules transitions = await RemoteGenerationModules.CreateAsync(context).ConfigureAwait(false);
        await using var ownedTransitions = transitions.ConfigureAwait(false);
        var adapter = new WarpCompiledRemoteSourceAdapter(context, modules.Source, modules.CompareExchange, modules.Census);
        await DriveRemoteAsync(context, adapter, false).ConfigureAwait(false);
        uint[] before = (uint[])context.Arena.Clone();
        uint[][] states = Enumerable.Range(0, 5).Select(worker => context.MachineState((uint)worker).ToArray()).ToArray();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => redispatch
            ? adapter.BeginDispatchAsync(transitions.Dispatch, [Enumerable.Repeat(2u, 5).ToArray()])
            : adapter.RequestCollectionAsync(transitions.Collection)).ConfigureAwait(false);
        CollectionAssert.AreEqual(before, context.Arena);
        for (uint worker = 0; worker < 5; worker++) { CollectionAssert.AreEqual(states[worker], context.MachineState(worker).ToArray()); }
        Assert.AreEqual(WarpPortableSchedulerLayout.CompletedContext, context.State);
        Assert.AreEqual(1u, context.Result(0, context.Dispatch)[0]);
        Assert.IsFalse(transitions.Dispatch.IsFaulted); Assert.IsFalse(transitions.Collection.IsFaulted);
        Assert.IsFalse(context.Plan.HasLocalSourceKernel);
    }
}
