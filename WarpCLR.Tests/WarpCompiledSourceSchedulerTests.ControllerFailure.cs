using System.Diagnostics;
using System.Globalization;
using WarpCLR.Compiler;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StoppedPreparedControllerTransitionDisposesThroughItsAuthenticatedCensus(bool redispatch)
    {
        Assert.IsTrue(OperatingSystem.IsLinux(), "This actual owned-process stop witness requires the recorded Linux environment.");
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 5, 2, [Enumerable.Repeat(1u, 5).ToArray()]);
        RemoteModules modules = await RemoteModules.CreateAsync(context, new()).ConfigureAwait(false);
        await using var ownedModules = modules.ConfigureAwait(false);
        RemoteGenerationModules transitions = await RemoteGenerationModules.CreateAsync(context,
            new WarpCoreCLRWorkerOptions { QuantumTimeout = TimeSpan.FromSeconds(1) }).ConfigureAwait(false);
        await using var ownedTransitions = transitions.ConfigureAwait(false);
        var adapter = new WarpCompiledRemoteSourceAdapter(context, modules.Source, modules.CompareExchange, modules.Census, modules.Publication, modules.Stopped);
        await DriveRemoteAsync(context, adapter, false).ConfigureAwait(false);
        Assert.AreEqual(1u, context.Result(0, context.Dispatch)[0]);
        uint dispatch = context.Dispatch, epoch = context.Epoch;
        uint[][] original = Enumerable.Range(0, 5).Select(worker => context.MachineState((uint)worker).ToArray()).ToArray();
        WarpCoreCLRWorkerLease failed = redispatch ? transitions.Dispatch : transitions.Collection;
        await StopOwnedNativeWorkerAsync(failed).ConfigureAwait(false);
        WarpHostException failure = await Assert.ThrowsAsync<WarpHostException>(() => redispatch
            ? adapter.BeginDispatchAsync(failed, [Enumerable.Repeat(2u, 5).ToArray()])
            : adapter.RequestCollectionAsync(failed)).ConfigureAwait(false);
        WarpCoreCLRStoppedCommands.Command command = WarpCoreCLRStoppedCommands.FromFailure(failure)!;
        Assert.IsNotNull(command);
        Assert.IsTrue(command.Stopped);
        Assert.IsNull(command.Admission, "A controller participant remains independent of source tickets.");
        Assert.IsNotNull(command.Controller);
        Assert.IsNotNull(command.Recovery);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.Mint(command, new object()));
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.GetCommittedGenerationTransition(command.State));
        AssertFailedControllerContext(context, original, dispatch, epoch);
        Assert.IsTrue(failed.IsFaulted);
        Assert.IsFalse(context.Plan.HasLocalSourceKernel);
        await Assert.ThrowsAsync<WarpHostException>(() => modules.CompareExchange.ExecuteManagedQuantumAsync(
            [[context.Scheduler + WarpPortableSchedulerLayout.ControllerOwner], [0], [1]], [], 0,
            modules.CompareExchange.Layout.CreateInitialState(32, 1000), 32, 4096, context.Arena, CancellationToken.None)).ConfigureAwait(false);
    }

    private static void AssertFailedControllerContext(WarpCompiledSourceContext context, uint[][] original, uint dispatch, uint epoch)
    {
        Assert.AreEqual(dispatch, context.Dispatch);
        Assert.AreEqual(epoch, context.Epoch);
        Assert.AreEqual(0u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.ControllerOwner]);
        Assert.AreEqual(WarpPortableSchedulerLayout.DisposedContext, context.State);
        Assert.AreEqual(1u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.OutputQuarantined]);
        Assert.IsNull(context.Controller.TryAcquire());
        Assert.IsNull(context.Claim(0));
        Assert.AreEqual(WarpPortableSchedulerLayout.Yield, context.RequestDisposal(), "Permanent quarantine forbids ordinary execution after authenticated disposal.");
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Result(0, dispatch));
        for (uint worker = 0; worker < 5; worker++) { CollectionAssert.AreEqual(original[worker], context.MachineState(worker).ToArray()); }
    }

    private static async Task StopOwnedNativeWorkerAsync(WarpCoreCLRWorkerLease lease)
    {
        // Stop only this already-admitted actual child so its next native RPC starts
        // and meets the real worker deadline. Production uses no such test probe.
        using var stop = new Process { StartInfo = new ProcessStartInfo("/bin/kill") { UseShellExecute = false } };
        stop.StartInfo.ArgumentList.Add("-STOP");
        stop.StartInfo.ArgumentList.Add(lease.ProcessId.ToString(CultureInfo.InvariantCulture));
        Assert.IsTrue(stop.Start());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await stop.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        Assert.AreEqual(0, stop.ExitCode);
    }
}
