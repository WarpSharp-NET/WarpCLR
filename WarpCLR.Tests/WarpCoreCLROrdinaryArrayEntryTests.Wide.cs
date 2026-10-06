using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCoreCLROrdinaryArrayEntryTests
{
    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(0, 1)]
    [DataRow(0, 2)]
    [DataRow(1, 0)]
    [DataRow(1, 1)]
    [DataRow(1, 2)]
    [DataRow(2, 0)]
    [DataRow(2, 1)]
    [DataRow(2, 2)]
    public void PairAndAtomicEntriesDenyEveryBankRoleBeforePairViewsOrMemoryEffects(int kind, int route)
    {
        WarpLogicalMachineLayout layout = WideLayout(kind);
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        foreach (int role in Enumerable.Range(0, 4))
        foreach (bool quarantine in new[] { false, true })
        {
            uint[][] inputs = WideInputs(kind);
            uint[] scalars = [0x7FC00001], state = layout.CreateInitialState(4, 100), arena = [0xFFFFFFFF, 0xFEDCBA98, 0x80000000];
            uint[] bank = SelectBank(role, inputs, scalars, state, arena);
            uint[] beforeState = (uint[])state.Clone(), beforeArena = (uint[])arena.Clone(), beforeBank = (uint[])bank.Clone();
            object owner = new(), authority = new();
            WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
            WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
            if (quarantine) { WarpOrdinaryArrayRegistry.Quarantine(handle, owner, authority); }
            AssertDenied(() => InvokeQuantum(core, route, inputs, scalars, state, arena, 100), route == 2);
            CollectionAssert.AreEqual(beforeState, state);
            CollectionAssert.AreEqual(beforeArena, arena);
            CollectionAssert.AreEqual(beforeBank, bank);
        }
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(0, 1)]
    [DataRow(0, 2)]
    [DataRow(1, 0)]
    [DataRow(1, 1)]
    [DataRow(1, 2)]
    [DataRow(2, 0)]
    [DataRow(2, 1)]
    [DataRow(2, 2)]
    public void UnownedPairAndAtomicEntriesKeepEveryUInt32WordEncoding(int kind, int route)
    {
        WarpLogicalMachineLayout layout = WideLayout(kind);
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[][] inputs = WideInputs(kind);
        uint[] state = layout.CreateInitialState(4, 100), arena = [0xFFFFFFFF, 0xFEDCBA98, 0x80000000];
        InvokeQuantum(core, route, inputs, [0x7FC00001], state, arena, 100);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        uint expectedLow = kind == 0 ? 0x80000000 : 0;
        Assert.AreEqual(expectedLow, state[WarpLogicalMachineLayout.ResultOffset]);
        if (kind != 1)
        {
            Assert.AreEqual(kind == 0 ? 0x7FC00001u : 0xFEDCBA99u, state[WarpLogicalMachineLayout.ResultHighOffset]);
        }
        CollectionAssert.AreEqual(ExpectedWideArenas[kind], arena);
    }

    private static readonly uint[][] ExpectedWideArenas =
        [[0xFFFFFFFF, 0xFEDCBA98, 0x80000000], [0, 0xFEDCBA98, 0x80000000], [0, 0xFEDCBA99, 0x80000000]];
}
