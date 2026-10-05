using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    public void Schema08RoundTripRetainsEveryCapabilityAliasAndTerminalField()
    {
        foreach (WarpLogicalMachineLayout layout in Schema08Layouts())
        {
            WarpControlFlowKernel original = layout.Kernel;
            byte[] bytes = WarpCoreCLRBinaryPlanCodec.Serialize(original);
            WarpControlFlowKernel copy = WarpCoreCLRBinaryPlanCodec.Deserialize(bytes, WarpIrHash.Compute(original));
            CollectionAssert.AreEqual(bytes, WarpCoreCLRBinaryPlanCodec.Serialize(copy));
            Assert.ThrowsExactly<NotSupportedException>(() => WarpCoreCLRPlanCodec.Serialize(original));
            if (original.Execution is null) { Assert.IsNull(copy.Execution); continue; }
            WarpLogicalExecutionMetadata left = original.Execution, right = copy.Execution!;
            Assert.AreEqual(left.RecursiveCalls, right.RecursiveCalls);
            Assert.AreEqual(left.FrameOwners, right.FrameOwners); Assert.AreEqual(left.RuntimeStateAccess, right.RuntimeStateAccess);
            Assert.AreEqual(left.NonlocalStateDispatch, right.NonlocalStateDispatch);
            Assert.AreEqual(left.ManagedExceptionTermination, right.ManagedExceptionTermination);
            Assert.AreEqual(left.LogicalWorkerAccess, right.LogicalWorkerAccess);
            foreach ((WarpLogicalBodyMetadata a, WarpLogicalBodyMetadata b) in left.Bodies.Zip(right.Bodies))
            {
                Assert.AreEqual(a.PrivateWordCount, b.PrivateWordCount); Assert.AreEqual(a.RuntimeHelper, b.RuntimeHelper);
                Assert.AreEqual(a.CountsSourceDepth, b.CountsSourceDepth); Assert.AreEqual(a.AliasOwnerFunction, b.AliasOwnerFunction);
                Assert.AreEqual(a.AliasPrefixWords, b.AliasPrefixWords);
                CollectionAssert.AreEqual(a.SourceBlockCosts.ToArray(), b.SourceBlockCosts.ToArray());
            }
            Assert.AreEqual(layout.ResultWordCount, new WarpLogicalMachineLayout(copy).ResultWordCount);
        }
    }

    private static IEnumerable<WarpLogicalMachineLayout> Schema08Layouts() =>
        new[] { WarpLogicalWorkerHookKernels.CreateDirect(), WarpLogicalWorkerHookKernels.CreateNested(),
            WarpLogicalWorkerHookKernels.CreateInputBinding(), WarpFilterAliasHookKernels.Create(),
            WarpFilterAliasHookKernels.Create(callSource: true), WarpManagedExceptionHookKernels.Create(0),
            WarpManagedExceptionHookKernels.Create(1), WarpManagedExceptionHookKernels.Create(3), WarpManagedExceptionHookKernels.CreateHelper() }
            .Concat(WarpManagedWideAtomicKernels.Create64()).Concat(WarpExceptionMachineHookKernels.Create());

    private static void AssertActualSchemaChild(WarpCoreCLRWorkerLease lease)
    {
        Assert.AreNotEqual(Environment.ProcessId, lease.ProcessId); Assert.IsTrue(lease.IsCollectible);
        Assert.IsFalse(AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.ManifestModule.ModuleVersionId == lease.CompiledModule));
    }

    private static async Task<uint[]> RunSchemaChildAsync(WarpCoreCLRWorkerLease lease,
        uint[] initial, uint[][] inputs, int worker, int depth, int quantum, uint[] arena)
    {
        uint[] state = (uint[])initial.Clone();
        int calls = 0;
        do
        {
            await lease.ExecuteManagedQuantumAsync(inputs, [], worker, state, depth, quantum, arena, CancellationToken.None).ConfigureAwait(false);
            Assert.IsLessThan(200, ++calls);
            if (state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.BeforeSourceBoundary)
            { WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state); }
        } while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable);
        return state;
    }
}
