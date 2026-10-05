using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host.Native;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this internal fixture through reflection.")]
internal sealed class WarpStateArenaBridgeTests
{
    [TestMethod]
    public void GeneratedServicesReadAndWriteTheExecutingStateAndArenaAsDistinctBanks()
    {
        foreach (WarpLogicalMachineLayout layout in WarpStateArenaBridgeKernels.Create())
        foreach (uint value in new uint[] { 0, 1, 0x80000000, 0x7FA12345, uint.MaxValue })
        foreach (int quantum in new[] { layout.MaximumBlockCost, 65536 })
        {
            uint[] state = layout.CreateInitialState(4, 1);
            state[48] = 0x3F800001;
            uint[] arena = [0xDEADBEEF, 0x80000000, 0];
            CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
            int quanta = 0;
            do
            {
                core.ExecuteManagedQuantum([[value]], [], 0, state, 4, quantum, arena);
                WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 4);
                Assert.IsLessThan(1000, ++quanta);
            } while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable);
            bool indirect = layout.Kernel.Name.Contains(nameof(WarpStateArenaBridgeServices.CopyOwnedAddress), StringComparison.Ordinal);
            uint expectedState = indirect ? unchecked(0x3F800001 + value) : arena[1];
            uint expectedOutput = 0x3F800001 ^ (indirect ? expectedState : value);
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(expectedState, state[48]);
            Assert.AreEqual(expectedOutput, arena[0]);
            Assert.AreEqual(expectedOutput, state[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(indirect ? 0u : (uint)state.Length, arena[2]);
            Assert.AreEqual(1u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        }
    }

    [TestMethod]
    public void SwappedHelperBanksAndMissingClosureBindingsAreRejected()
    {
        MethodInfo direct = Method(nameof(WarpStateArenaBridgeServices.CopyOwnedWords));
        MethodInfo read = Method(nameof(WarpStateArenaBridgeServices.ReadStateWord));
        MethodInfo write = Method(nameof(WarpStateArenaBridgeServices.WriteArenaWord));
        var bindings = new Dictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>>
        {
            [direct] = [WarpRuntimeWordBank.State, WarpRuntimeWordBank.Arena, WarpRuntimeWordBank.Word],
            [read] = [WarpRuntimeWordBank.Arena, WarpRuntimeWordBank.Word],
            [write] = [WarpRuntimeWordBank.Arena, WarpRuntimeWordBank.Word, WarpRuntimeWordBank.Word],
        };
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpWordStateArenaServiceLowerer.Lower(direct, bindings));
        Assert.AreEqual("WRPCIL1017", error.Code, StringComparer.Ordinal);
        bindings.Remove(read);
        Assert.ThrowsExactly<ArgumentException>(() => WarpWordStateArenaServiceLowerer.Lower(direct, bindings));
        Assert.ThrowsExactly<WarpVerificationException>(() => new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(direct, 1, wordArena: true)));
    }

    [TestMethod]
    public void BindingsAreOwnedAndCannotTagNumericParametersAsBanks()
    {
        MethodInfo method = Method(nameof(WarpStateArenaBridgeServices.CopyOwnedAddress));
        WarpRuntimeWordBank[] parameters = [WarpRuntimeWordBank.State, WarpRuntimeWordBank.Arena, WarpRuntimeWordBank.Word];
        var supplied = new Dictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>> { [method] = parameters };
        var owned = new WarpWordBankBindings(supplied);
        parameters[0] = WarpRuntimeWordBank.Arena;
        supplied.Clear();
        Assert.IsTrue(owned.GetStateParameters(method)[0]);
        Assert.IsFalse(owned.GetStateParameters(method)[1]);
        Assert.ThrowsExactly<ArgumentException>(() => new WarpWordBankBindings(new Dictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>>
        {
            [method] = [WarpRuntimeWordBank.State, WarpRuntimeWordBank.Arena, WarpRuntimeWordBank.State],
        }));
    }

    [TestMethod]
    public void SwappingTheDeclaredBanksChangesTheAdmittedIrIdentity()
    {
        MethodInfo method = Method(nameof(WarpStateArenaBridgeServices.CopyOwnedAddress));
        WarpLogicalMachineLayout normal = WarpWordStateArenaServiceLowerer.Lower(method,
            new Dictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>>
            { [method] = [WarpRuntimeWordBank.State, WarpRuntimeWordBank.Arena, WarpRuntimeWordBank.Word] });
        WarpLogicalMachineLayout swapped = WarpWordStateArenaServiceLowerer.Lower(method,
            new Dictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>>
            { [method] = [WarpRuntimeWordBank.Arena, WarpRuntimeWordBank.State, WarpRuntimeWordBank.Word] });
        Assert.AreNotEqual(WarpIrHash.Compute(normal.Kernel), WarpIrHash.Compute(swapped.Kernel), StringComparer.Ordinal);
        Assert.IsTrue(normal.RequiresManagedMemory);
        Assert.IsTrue(normal.Kernel.Execution!.RuntimeStateAccess);
    }

    private static MethodInfo Method(string name) => typeof(WarpStateArenaBridgeServices).GetMethod(name)!;
}
