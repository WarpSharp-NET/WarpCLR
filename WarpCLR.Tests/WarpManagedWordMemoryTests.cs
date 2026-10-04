using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests;

[TestClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest creates this fixture through discovery.")]
internal sealed class WarpManagedWordMemoryTests
{
    [TestMethod]
    public void ManagedArenaIsExplicitAndWordWritesPersistAcrossResumedCalls()
    {
        WarpLogicalMachineLayout layout = WarpManagedMemoryKernels.CreateWriteReadCount();
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(2, 1000);
        uint[][] inputs = [[3], [0x7FC01234]];
        uint[] arena = [91, 92, 93, 94, 95];
        Assert.ThrowsExactly<ArgumentException>(() => compiled.ExecuteQuantum(inputs, [], 0, state, 2, 1000));
        compiled.ExecuteManagedQuantum(inputs, [], 0, state, 2, layout.MaximumBlockCost, arena);
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(94u, arena[3]);
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
        {
            compiled.ExecuteManagedQuantum(inputs, [], 0, state, 2, layout.MaximumBlockCost, arena);
        }

        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(0x7FC01234u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(5u, state[WarpLogicalMachineLayout.ResultHighOffset]);
        CollectionAssert.AreEqual(new uint[] { 91, 92, 93, 0x7FC01234, 95 }, arena);
    }

    [TestMethod]
    [DataRow(0u, 0)]
    [DataRow(4u, 4)]
    [DataRow(uint.MaxValue, 4)]
    public void InvalidArenaAddressFaultsBeforeAccessAndQuarantinesResults(uint index, int length)
    {
        WarpLogicalMachineLayout layout = WarpManagedMemoryKernels.CreateWriteReadCount();
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(2, 1000);
        uint[] arena = Enumerable.Repeat(0xAA551122u, length).ToArray();
        compiled.ExecuteManagedQuantum([[index], [9]], [], 0, state, 2, 1000, arena);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(WarpLogicalMachineLayout.ManagedMemoryBoundsFault, state[WarpLogicalMachineLayout.FaultKindOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultHighOffset]);
        CollectionAssert.AreEqual(Enumerable.Repeat(0xAA551122u, length).ToArray(), arena);
    }

    [TestMethod]
    [DataRow(WarpBackendKind.NVPTX)]
    [DataRow(WarpBackendKind.AMDGPU)]
    [DataRow(WarpBackendKind.SPIRV)]
    public void EveryDialectBindsTheSameArenaAndChecksUnsignedBounds(WarpBackendKind backend)
    {
        WarpLogicalMachineLayout layout = WarpManagedMemoryKernels.CreateWriteReadCount();
        string source = WarpPortableMachineEmitter.Emit(layout, backend);
        StringAssert.Contains(source, "ptr addrspace(1) %warp_heap, i32 %warp_heap_words", StringComparison.Ordinal);
        StringAssert.Contains(source, "icmp uge i32", StringComparison.Ordinal);
        StringAssert.Contains(source, "memory_fault_", StringComparison.Ordinal);
        StringAssert.Contains(source, "getelementptr i32, ptr addrspace(1) %warp_heap", StringComparison.Ordinal);
    }
}
