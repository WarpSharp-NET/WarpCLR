using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    public async Task ImmutableInputBindingRunsNativeBatchesAtBothQuantaAndKeepsTransportIdentity()
    {
        var kernel = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(typeof(TestKernels).GetMethod(nameof(TestKernels.Branch))!, 1)).ControlFlow;
        var layout = new WarpLogicalMachineLayout(kernel);
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var owner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        uint[] input = Enumerable.Range(0, 33).Select(index => (uint)index).ToArray();
        uint[] original = (uint[])input.Clone();
        WarpCoreCLRInputBinding binding = await lease.BindInputsAsync([input], [], CancellationToken.None).ConfigureAwait(false);
        await using var bindingOwner = binding.ConfigureAwait(false);
        Array.Fill(input, uint.MaxValue);
        foreach (int quantum in new[] { layout.MaximumBlockCost, 4096 })
        {
            uint[][] states = Enumerable.Range(0, original.Length).Select(_ => layout.CreateInitialState(32, 10000)).ToArray();
            await lease.ExecuteBatchAsync(binding, 0, states, 32, quantum, CancellationToken.None).ConfigureAwait(false);
            for (int worker = 0; worker < states.Length; worker++)
            {
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, states[worker][0]);
                Assert.AreEqual(TestKernels.Branch(original[worker]), states[worker][WarpLogicalMachineLayout.ResultOffset]);
            }
        }
        await binding.DisposeAsync().ConfigureAwait(false);
        Assert.AreEqual(0L, WarpCoreCLRTransferAdmission.ReservedBytes);
    }

    [TestMethod]
    public void BoundInputsRejectForgedTagsDigestsAndOverlongBatchShapes()
    {
        byte[] encoded = WarpCoreCLRWorkerInputWords.Write(1, [[0x80000000, 0xDEADBEEF]], [uint.MaxValue]);
        WarpCoreCLRWorkerInputWords.Binding input = WarpCoreCLRWorkerInputWords.Read(encoded);
        var admitted = new Dictionary<uint, WarpCoreCLRWorkerInputWords.Binding> { [1] = input };
        byte[] reference = WarpCoreCLRWorkerInputWords.Reference(input.Tag, input.Identity);
        Assert.AreSame(input, WarpCoreCLRWorkerInputWords.Resolve(reference, admitted));
        reference[35] ^= 1;
        Assert.Throws<InvalidDataException>(() => WarpCoreCLRWorkerInputWords.Resolve(reference, admitted));
        Assert.Throws<InvalidDataException>(() => WarpCoreCLRWorkerInputWords.Resolve(new byte[35], admitted));
        Assert.Throws<InvalidDataException>(() => WarpCoreCLRWorkerBatchWords.Request(input.Tag, input.Identity, 0, 1, 1,
            new uint[WarpCoreCLRWorkerBatchWords.MaximumWorkers + 1][]));
    }

    [TestMethod]
    public async Task GlobalIpcStorageAdmissionIsBoundedAndCancellingAWaiterReturnsNoReservation()
    {
        using WarpCoreCLRTransferAdmission.Lease owner = await WarpCoreCLRTransferAdmission.AcquireAsync(
            WarpCoreCLRTransferAdmission.MaximumBytes, CancellationToken.None).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        Task<WarpCoreCLRTransferAdmission.Lease> waiting = WarpCoreCLRTransferAdmission.AcquireAsync(1, cancellation.Token);
        Assert.IsFalse(waiting.IsCompleted);
        await cancellation.CancelAsync().ConfigureAwait(false);
        try { using WarpCoreCLRTransferAdmission.Lease unexpected = await waiting.ConfigureAwait(false); Assert.Fail("Cancelled waiter received a reservation."); }
        catch (OperationCanceledException) { }
        Assert.AreEqual(WarpCoreCLRTransferAdmission.MaximumBytes, WarpCoreCLRTransferAdmission.ReservedBytes);
        owner.Retain(1); Assert.AreEqual(1L, WarpCoreCLRTransferAdmission.ReservedBytes);
        owner.Dispose(); Assert.AreEqual(0L, WarpCoreCLRTransferAdmission.ReservedBytes);
    }
}
