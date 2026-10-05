using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(-1)]
    public async Task ActualChildManagedTerminalPreservesReferenceAndNoNormalResult(int words)
    {
        WarpLogicalMachineLayout layout = words == -1 ? WarpManagedExceptionHookKernels.CreateHelper() : WarpManagedExceptionHookKernels.Create(words);
        WarpCoreCLRWorkerKernel child = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var owner = child.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = child.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        AssertActualSchemaChild(lease);
        uint[] initial = layout.CreateInitialState(2, 10);
        initial[WarpLogicalMachineLayout.ResultOffset] = 0xBAD00BAD;
        initial[WarpLogicalMachineLayout.ResultHighOffset] = 0x13579BDF;
        if (layout.ResultWordCount > 2) { initial[layout.GetResultWordOffset(2, 2)] = 0xCAFEBABE; }
        uint[] arena = [0xCAFE, 0x7FA12345];
        uint[] small = await RunSchemaChildAsync(lease, initial, [[0x1234], [42], [uint.MaxValue]], 0, 2, layout.MaximumBlockCost, arena).ConfigureAwait(false);
        uint[] large = await RunSchemaChildAsync(lease, initial, [[0x1234], [42], [uint.MaxValue]], 0, 2, 4096, arena).ConfigureAwait(false);
        CollectionAssert.AreEqual(small, large); CollectionAssert.AreEqual(new uint[] { 0xCAFE, 0x7FA12345 }, arena);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, small[0]); Assert.AreEqual(8u, small[1]);
        Assert.AreEqual(0x1234u, small[WarpLogicalMachineLayout.EscapedExceptionContextOffset]);
        Assert.AreEqual(42u, small[WarpLogicalMachineLayout.EscapedExceptionObjectOffset]);
        Assert.AreEqual(uint.MaxValue, small[WarpLogicalMachineLayout.EscapedExceptionGenerationOffset]);
        Assert.AreEqual(0xBAD00BADu, small[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(0x13579BDFu, small[WarpLogicalMachineLayout.ResultHighOffset]);
        if (layout.ResultWordCount > 2) { Assert.AreEqual(0xCAFEBABEu, small[layout.GetResultWordOffset(2, 2)]); }
        Assert.AreEqual(9u, small[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
    }
}
