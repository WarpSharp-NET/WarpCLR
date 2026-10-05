using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed partial class WarpCoreCLRWorkerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(nameof(TestKernels.Branch))]
    [DataRow(nameof(TestKernels.Loop))]
    [DataRow(nameof(TestKernels.Call))]
    public async Task ChildNativeCompilationMatchesSourceAtBothQuanta(string name)
    {
        MethodInfo source = typeof(TestKernels).GetMethod(name)!;
        WarpControlFlowKernel kernel = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(source, 1)).ControlFlow;
        var layout = new WarpLogicalMachineLayout(kernel);
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var owner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        Assert.AreNotEqual(Environment.ProcessId, lease.ProcessId); Assert.IsTrue(lease.IsCollectible);
        Assert.IsFalse(AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.ManifestModule.ModuleVersionId == lease.CompiledModule));
        uint[][] inputs = [[0, 1, 7, 31, 127]];
        foreach (int quantum in new[] { layout.MaximumBlockCost, 4096 })
        {
            for (int worker = 0; worker < inputs[0].Length; worker++)
            {
                uint[] state = layout.CreateInitialState(32, 100000);
                while (state[0] == WarpLogicalMachineLayout.Runnable)
                { await lease.ExecuteManagedQuantumAsync(inputs, [], worker, state, 32, quantum, [], CancellationToken.None).ConfigureAwait(false); }
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]);
                Assert.AreEqual((uint)source.Invoke(null, [inputs[0][worker]])!, state[WarpLogicalMachineLayout.ResultOffset]);
            }
        }
    }

    [TestMethod]
    public void CompleteBinaryCodecPreservesTupleVoidPrivateHelpersAndCapabilities()
    {
        WarpControlFlowKernel kernel = RichKernel();
        byte[] bytes = WarpCoreCLRBinaryPlanCodec.Serialize(kernel);
        WarpControlFlowKernel copy = WarpCoreCLRBinaryPlanCodec.Deserialize(bytes, WarpIrHash.Compute(kernel));
        Assert.AreEqual(WarpIrHash.Compute(kernel), WarpIrHash.Compute(copy), StringComparer.Ordinal);
        Assert.IsTrue(copy.Execution!.FrameOwners); Assert.IsTrue(copy.Execution.RuntimeStateAccess);
        Assert.IsTrue(copy.Execution.RecursiveCalls); Assert.IsTrue(copy.Execution.Bodies[1].RuntimeHelper);
        Assert.AreEqual(7, copy.Execution.Bodies[1].PrivateWordCount);
        Assert.AreEqual(2, copy.Functions[0].ResultWordCount); Assert.AreEqual(0, copy.Functions[1].ResultWordCount);
        Assert.Throws<NotSupportedException>(() => WarpCoreCLRPlanCodec.Serialize(kernel));
    }

    [TestMethod]
    public async Task TupleVoidHelpersFrameOwnersAndSourceBoundarySurviveChildQuanta()
    {
        var layout = new WarpLogicalMachineLayout(RichKernel());
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var owner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        foreach (int quantum in new[] { layout.MaximumBlockCost, 4096 })
        {
            uint[] state = layout.CreateInitialState(8, 100);
            uint context = state[WarpLogicalMachineLayout.OwnerContextOffset];
            layout.SetSourceBoundaryMode(state, true);
            int boundaries = 0;
            while (state[0] == 0)
            {
                await lease.ExecuteManagedQuantumAsync([[17]], [], 0, state, 8, quantum, [], CancellationToken.None).ConfigureAwait(false);
                Assert.AreEqual(context, state[WarpLogicalMachineLayout.OwnerContextOffset]);
                if (state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.BeforeSourceBoundary)
                { boundaries++; WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state); }
            }
            Assert.AreEqual(1, boundaries); Assert.AreEqual(1u, state[0]);
            Assert.AreEqual(17u, state[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(24u, state[WarpLogicalMachineLayout.ResultHighOffset]);
            Assert.AreEqual(99u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
            Assert.IsGreaterThanOrEqualTo(3u, state[WarpLogicalMachineLayout.NextActivationOffset]);
        }
    }

    [TestMethod]
    public void CodecRejectsCorruptionTruncationUnsupportedIdentityAndOversize()
    {
        WarpControlFlowKernel kernel = RichKernel();
        byte[] bytes = WarpCoreCLRBinaryPlanCodec.Serialize(kernel);
        string hash = WarpIrHash.Compute(kernel);
        Assert.Throws<InvalidDataException>(() => WarpCoreCLRBinaryPlanCodec.Deserialize(bytes, new string('0', 64)));
        bytes[8] ^= 1;
        Assert.Throws<InvalidDataException>(() => WarpCoreCLRBinaryPlanCodec.Deserialize(bytes, hash));
        Assert.Throws<InvalidDataException>(() => WarpCoreCLRBinaryPlanCodec.Deserialize(bytes.AsSpan(0, bytes.Length - 1), hash));
        byte[] huge = new byte[WarpCoreCLRBinaryPlanCodec.MaximumBytes + 1];
        Assert.Throws<InvalidDataException>(() => WarpCoreCLRBinaryPlanCodec.Deserialize(huge, hash));
    }

    private static WarpControlFlowKernel RichKernel()
    {
        WarpControlFlowFunction[] functions =
        [new(0, "pair", 1, [new WarpBasicBlock(0, [],
            [new(0, WarpIrOpCode.LoadArgument), new(1, WarpIrOpCode.Constant, immediate: 7), new(2, WarpIrOpCode.Add, 0, 1)],
            new WarpTupleReturnTerminator([0, 2]))]),
         new(1, "void", 0, [new WarpBasicBlock(0, [], [], new WarpTupleReturnTerminator([]))])];
        var metadata = new WarpLogicalExecutionMetadata([new(5, false, [1]), new(7, true, [0]), new(3, true, [0])],
            recursiveCalls: true, frameOwners: true, runtimeStateAccess: true);
        return new WarpControlFlowKernel("worker-rich", 1, 0,
            [new WarpBasicBlock(0, [], [new(0, WarpIrOpCode.LoadInput), new WarpIrInstruction(1, 0, [0], 2),
                new WarpIrInstruction(3, 1, [], 0)], new WarpTupleReturnTerminator([1, 2]))], null, functions, metadata);
    }
}
