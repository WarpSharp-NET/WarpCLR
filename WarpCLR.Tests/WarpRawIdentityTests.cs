using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest discovers and instantiates this internal fixture through reflection.")]
internal sealed class WarpRawIdentityTests
{
    [TestMethod]
    public void EveryRawUtf16CodeUnitHasAUniqueIrIdentityAndExactBinaryRoundTrip()
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        for (int word = 0; word <= ushort.MaxValue; word++)
        {
            string name = "identity_" + (char)word;
            WarpControlFlowKernel original = Kernel(name);
            string hash = WarpIrHash.Compute(original);
            Assert.IsTrue(hashes.Add(hash));
            WarpControlFlowKernel decoded = WarpCoreCLRBinaryPlanCodec.Deserialize(WarpCoreCLRBinaryPlanCodec.Serialize(original), hash);
            Assert.AreEqual(name, decoded.Name, StringComparer.Ordinal);
            Assert.AreEqual(name, decoded.Functions[0].Name, StringComparer.Ordinal);
        }
        Assert.HasCount(65536, hashes);
    }

    [TestMethod]
    [DataRow(31)]
    [DataRow(4096)]
    public void PairedUnpairedAndNulNamesPreserveGenuineCoreClrCallResultsAndWholeBanks(int quantum)
    {
        uint[][] inputs = [[0, 1, 7, 0x80000000, uint.MaxValue, 0xD800, 0xDC01]];
        CoreCLRResumableKernel baseline = CoreCLRResumableKernel.Compile(new(Kernel("identity_original")));
        foreach (string name in new[] { "identity_\uD800", "identity_\uD801", "identity_\uDC00", "identity_\uDC01", "identity_\uFFFD", "identity_\uD800\uDC00", "identity_\0" })
        {
            WarpControlFlowKernel original = Kernel(name);
            WarpControlFlowKernel decoded = WarpCoreCLRBinaryPlanCodec.Deserialize(WarpCoreCLRBinaryPlanCodec.Serialize(original), WarpIrHash.Compute(original));
            CoreCLRResumableKernel actual = CoreCLRResumableKernel.Compile(new(decoded));
            Assert.IsTrue(actual.IsCollectible);
            Assert.IsTrue(actual.CompiledEntryPoint.Module.Assembly.IsDynamic);
            Assert.AreNotEqual(IntPtr.Zero, actual.CompiledEntryPoint.MethodHandle.GetFunctionPointer());
            for (int worker = 0; worker < inputs[0].Length; worker++)
            {
                uint[] expected = baseline.Layout.CreateInitialState(2, 100);
                uint[] state = actual.Layout.CreateInitialState(2, 100);
                do
                {
                    baseline.ExecuteQuantum(inputs, [], worker, expected, 2, quantum);
                    actual.ExecuteQuantum(inputs, [], worker, state, 2, quantum);
                    CollectionAssert.AreEqual(expected, state);
                } while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable);
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
                Assert.AreEqual(unchecked(inputs[0][worker] + 7), state[WarpLogicalMachineLayout.ResultOffset]);
            }
        }
    }

    private static WarpControlFlowKernel Kernel(string name) => new(name, 1, 0,
        [new WarpBasicBlock(0, [], [new(0, WarpIrOpCode.LoadInput), new WarpIrInstruction(1, 0, [0], 1)], new WarpReturnTerminator(1))],
        functions: [new WarpControlFlowFunction(0, name, 1,
            [new WarpBasicBlock(0, [], [new(0, WarpIrOpCode.LoadArgument), new(1, WarpIrOpCode.Constant, immediate: 7), new(2, WarpIrOpCode.Add, 0, 1)], new WarpReturnTerminator(2))])]);
}
