using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    [TestMethod]
    public async Task RemoteGenerationReceiptsPreserveRootedOutputsAndResetPrimitiveState()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Identity), 5, 2, [new uint[5], new uint[5], new uint[5]]);
        context.QueueEntryAllocation(0, ObjectType(context));
        RemoteModules modules = await RemoteModules.CreateAsync(context, new()).ConfigureAwait(false);
        await using var ownedModules = modules.ConfigureAwait(false);
        RemoteGenerationModules transitions = await RemoteGenerationModules.CreateAsync(context).ConfigureAwait(false);
        await using var ownedTransitions = transitions.ConfigureAwait(false);
        var adapter = new WarpCompiledRemoteSourceAdapter(context, modules.Source, modules.CompareExchange, modules.Census, modules.Publication, modules.Stopped);
        await DriveRemoteAsync(context, adapter, false).ConfigureAwait(false);
        uint priorDispatch = context.Dispatch;
        uint[] owner = context.Result(0, priorDispatch);
        Assert.AreNotEqual(0u, owner[1]);
        Assert.AreEqual(0u, InvokeTrustedHeap(context, nameof(WarpPortableHeapServices.AcquireRoot),
            [.. owner, WarpPortableHeapLayout.StrongRoot, 0, 0, 0]));
        uint root = context.Arena[WarpPortableHeapLayout.Result], generation = context.Arena[WarpPortableHeapLayout.Result + 1];
        uint epoch = context.Epoch;
        Assert.AreEqual(0u, await adapter.RequestCollectionAsync(transitions.Collection).ConfigureAwait(false));
        Assert.AreEqual(epoch + 1, context.Epoch);
        Assert.AreEqual(0u, await adapter.RequestCollectionAsync(transitions.Collection).ConfigureAwait(false));
        Assert.AreEqual(epoch + 1, context.Epoch, "An authenticated repeated request has zero epoch delta.");
        Assert.AreEqual(0u, context.AdvanceCollection());
        Assert.AreEqual(1u, SlotState(context, owner[1]));
        Assert.AreEqual(0u, await adapter.BeginDispatchAsync(transitions.Dispatch, [new uint[5], new uint[5], new uint[5]]).ConfigureAwait(false));
        Assert.AreEqual(priorDispatch + 1, context.Dispatch);
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, context.MachineState(0)[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(0u, context.MachineState(0)[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual((uint)context.Plan.MaximumSteps, context.MachineState(0)[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        Assert.AreEqual(0u, await adapter.RequestCollectionAsync(transitions.Collection).ConfigureAwait(false));
        Assert.AreEqual(0u, context.AdvanceCollection());
        Assert.AreEqual(1u, SlotState(context, owner[1]));
        await DriveRemoteAsync(context, adapter, true).ConfigureAwait(false);
        for (uint worker = 0; worker < 5; worker++) { Assert.AreEqual(0u, context.ReleaseOutput(worker, priorDispatch)); }
        Assert.AreEqual(1u, SlotState(context, owner[1]), "The separate precise host root retains the old object.");
        Assert.AreEqual(0u, InvokeTrustedHeap(context, nameof(WarpPortableHeapServices.ReleaseRoot), [root, generation]));
        Assert.AreEqual(0u, await adapter.RequestCollectionAsync(transitions.Collection).ConfigureAwait(false));
        Assert.AreEqual(0u, context.AdvanceCollection());
        Assert.AreEqual(0u, SlotState(context, owner[1]));
        await DriveRemoteAsync(context, adapter, false).ConfigureAwait(false);
        CollectionAssert.AreEqual(new uint[3], context.Result(0, context.Dispatch));
        Assert.IsFalse(context.Plan.HasLocalSourceKernel);
    }

    private static uint SlotState(WarpCompiledSourceContext context, uint slot) =>
        context.Arena[context.Arena[WarpPortableHeapLayout.SlotStart] + (slot - 1) * WarpPortableHeapLayout.SlotWords + WarpPortableHeapLayout.SlotState];

    private static uint InvokeTrustedHeap(WarpCompiledSourceContext context, string service, uint[] arguments) =>
        WarpCompiledRuntimeServices.Run(WarpCompiledWordService.Create(typeof(WarpPortableHeapServices), service),
            context.Arena, arguments, context.Plan.Quantum);

    private static async Task DriveRemoteAsync(WarpCompiledSourceContext context, WarpCompiledRemoteSourceAdapter adapter, bool waitForOldOutput)
    {
        for (int attempt = 0; context.State == WarpPortableSchedulerLayout.Active && attempt < 100000; attempt++)
        {
            if (waitForOldOutput && Enumerable.Range(0, checked((int)context.Plan.Workers)).All(worker =>
                    context.WorkerState((uint)worker) == WarpPortableSchedulerLayout.WaitingOutput)) { return; }
            WarpCompiledWorkerTicket? ticket = context.Claim((uint)attempt % context.Plan.Residents);
            if (ticket is null) { continue; }
            uint status;
            if (!context.HasRuntimeHelper(ticket) && context.MachineState(ticket.Worker)[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable)
            { status = context.CommitRetainedSource(ticket); }
            else
            {
                if (context.HasRuntimeHelper(ticket)) { context.Execute(ticket); }
                else { Assert.IsNull(await adapter.ExecuteAsync(await adapter.PrepareAsync(ticket).ConfigureAwait(false)).ConfigureAwait(false)); }
                status = context.Commit(ticket);
            }
            Assert.IsTrue(status == 0 || waitForOldOutput && status == WarpPortableSchedulerLayout.NeedOutputRelease);
        }
        Assert.AreEqual(WarpPortableSchedulerLayout.CompletedContext, context.State);
    }

    private sealed class RemoteGenerationModules : IAsyncDisposable
    {
        private readonly WarpCoreCLRWorkerKernel collection;
        private readonly WarpCoreCLRWorkerKernel dispatch;
        private RemoteGenerationModules(WarpCoreCLRWorkerKernel collection, WarpCoreCLRWorkerKernel dispatch)
        {
            this.collection = collection; this.dispatch = dispatch;
            Collection = collection.TryAcquireLease()!; Dispatch = dispatch.TryAcquireLease()!;
        }
        internal WarpCoreCLRWorkerLease Collection { get; }
        internal WarpCoreCLRWorkerLease Dispatch { get; }
        internal static async Task<RemoteGenerationModules> CreateAsync(WarpCompiledSourceContext context, WarpCoreCLRWorkerOptions? options = null)
        {
            WarpCoreCLRWorkerKernel preparedCollection = await WarpCoreCLRWorkerKernel.CompileAsync(
                context.Plan.Services.Scheduler(nameof(WarpPortableSchedulerServices.RequestCollection)).Layout, options ?? new(), CancellationToken.None).ConfigureAwait(false);
            try
            {
                WarpCoreCLRWorkerKernel preparedDispatch = await WarpCoreCLRWorkerKernel.CompileAsync(
                    context.Plan.Services.Scheduler(nameof(WarpPortableSchedulerServices.BeginDispatch)).Layout, options ?? new(), CancellationToken.None).ConfigureAwait(false);
                return new(preparedCollection, preparedDispatch);
            }
            catch { await preparedCollection.DisposeAsync().ConfigureAwait(false); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            await Dispatch.DisposeAsync().ConfigureAwait(false); await Collection.DisposeAsync().ConfigureAwait(false);
            await dispatch.DisposeAsync().ConfigureAwait(false); await collection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
