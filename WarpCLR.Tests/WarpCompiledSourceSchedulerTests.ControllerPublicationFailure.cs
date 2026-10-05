using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StoppedPublicationUsesIndependentEmergencyAfterExactReceiptApplication(bool redispatch)
    {
        Assert.IsTrue(OperatingSystem.IsLinux());
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 5, 2, [Enumerable.Repeat(1u, 5).ToArray()]);
        RemoteModules modules = await RemoteModules.CreateAsync(context, new(),
            new WarpCoreCLRWorkerOptions { QuantumTimeout = TimeSpan.FromSeconds(1) }).ConfigureAwait(false);
        await using var ownedModules = modules.ConfigureAwait(false);
        RemoteGenerationModules transitions = await RemoteGenerationModules.CreateAsync(context).ConfigureAwait(false);
        await using var ownedTransitions = transitions.ConfigureAwait(false);
        var adapter = new WarpCompiledRemoteSourceAdapter(context, modules.Source, modules.CompareExchange, modules.Census, modules.Publication, modules.Stopped);
        await DriveRemoteAsync(context, adapter, false).ConfigureAwait(false);
        uint dispatch = context.Dispatch, epoch = context.Epoch;
        uint[][] before = Enumerable.Range(0, 5).Select(worker => context.MachineState((uint)worker).ToArray()).ToArray();
        Assert.AreNotEqual(modules.Publication.ProcessId, modules.CompareExchange.ProcessId);
        Assert.AreNotEqual(modules.Publication.CompiledModule, modules.CompareExchange.CompiledModule);
        await StopOwnedNativeWorkerAsync(modules.Publication).ConfigureAwait(false);
        WarpHostException failure = await Assert.ThrowsAsync<WarpHostException>(() => redispatch
            ? adapter.BeginDispatchAsync(transitions.Dispatch, [Enumerable.Repeat(2u, 5).ToArray()])
            : adapter.RequestCollectionAsync(transitions.Collection)).ConfigureAwait(false);
        WarpCoreCLRStoppedCommands.Command command = WarpCoreCLRStoppedCommands.FromFailure(failure)!;
        Assert.IsNotNull(command); Assert.IsTrue(command.Stopped); Assert.IsTrue(command.ControllerRelease);
        Assert.IsNotNull(command.Controller); Assert.IsNotNull(command.Recovery);
        Assert.IsTrue(command.Controller.Receipt!.Applied);
        Assert.AreEqual(redispatch ? dispatch + 1 : dispatch, context.Dispatch);
        Assert.AreEqual(redispatch ? epoch : epoch + 1, context.Epoch);
        Assert.AreEqual(WarpPortableSchedulerLayout.DisposedContext, context.State);
        Assert.AreEqual(0u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.ControllerOwner]);
        Assert.AreEqual(1u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.OutputQuarantined]);
        Assert.IsNull(context.Claim(0)); Assert.IsNull(context.Controller.TryAcquire());
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Result(0, context.Dispatch));
        Assert.IsTrue(modules.Publication.IsFaulted); Assert.IsFalse(modules.CompareExchange.IsFaulted);
        Assert.IsFalse(context.Plan.HasLocalSourceKernel);
        for (uint worker = 0; worker < 5; worker++)
        {
            if (!redispatch) { CollectionAssert.AreEqual(before[worker], context.MachineState(worker).ToArray()); }
            else
            {
                Assert.AreEqual(WarpLogicalMachineLayout.Runnable, context.MachineState(worker)[WarpLogicalMachineLayout.StatusOffset]);
                Assert.AreEqual(0u, context.MachineState(worker)[WarpLogicalMachineLayout.ResultOffset]);
            }
        }
    }
}
