using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

// Proposed actual generated-entry tests. This source-only packet has executed none of them.
[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest constructs this internal fixture through reflected discovery under DiscoverInternals.")]
internal sealed partial class WarpCoreCLROrdinaryArrayEntryTests
{
    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(0, 1)]
    [DataRow(1, 0)]
    [DataRow(1, 1)]
    [DataRow(2, 0)]
    [DataRow(2, 1)]
    public void DirectEntriesRejectInputAndScalarOwnershipBeforeAnyBudgetCharge(int route, int role)
    {
        CoreCLRJitKernel core = CoreCLRJitKernel.Compile(NarrowKernel());
        foreach (bool quarantine in new[] { false, true })
        {
            uint[] input = [0x80000000], scalars = [0x7FC00001];
            uint[] bank = role == 0 ? input : scalars;
            uint[] before = (uint[])bank.Clone();
            object owner = new(), authority = new();
            WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
            WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
            if (quarantine) { WarpOrdinaryArrayRegistry.Quarantine(handle, owner, authority); }
            var budget = new CoreCLRExecutionBudget(100, 4);
            AssertDenied(() => InvokeDirect(core, route, [input], scalars, budget), route == 2);
            Assert.AreEqual(0L, budget.StepsConsumed);
            Assert.AreEqual(0, budget.CurrentCallDepth);
            CollectionAssert.AreEqual(before, bank);
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void DirectEntriesPreserveEveryRawWordAfterAReleasedHold(int route)
    {
        CoreCLRJitKernel core = CoreCLRJitKernel.Compile(NarrowKernel());
        foreach (uint word in new uint[] { 0, uint.MaxValue, 0x80000000, 0x7FC00001, 0xFEDCBA98 })
        {
            uint[] input = [word], scalars = [0x12345678];
            object owner = new(), authority = new();
            WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [input, scalars]);
            WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
            WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
            Assert.AreEqual(word ^ scalars[0], InvokeDirect(core, route, [input], scalars, new(100, 4)));
            WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
            WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
            Assert.AreEqual(word, input[0]);
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public void CompiledFunctionsHaveOnlyValueArgumentsAndRetainTheirExactArithmetic(int reflection)
    {
        CoreCLRJitKernel core = CoreCLRJitKernel.Compile(NarrowKernel());
        MethodInfo function = core.CompiledFunctions[0];
        CollectionAssert.AreEqual(new[] { typeof(CoreCLRExecutionBudget), typeof(uint), typeof(uint) },
            function.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        uint[] bank = [0x7FC00001];
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        // A copied UInt32 value is not an array capability. No ownership is inferred from bits.
        const uint value = 0x7FC00001, mask = 0x80000000;
        var budget = new CoreCLRExecutionBudget(100, 4);
        uint actual = reflection == 0
            ? function.CreateDelegate<Func<CoreCLRExecutionBudget, uint, uint, uint>>()(budget, value, mask)
            : (uint)function.Invoke(null, [budget, value, mask])!;
        Assert.AreEqual(value ^ mask, actual);
        Assert.AreEqual(0, budget.CurrentCallDepth);
        Assert.AreEqual(value, bank[0]);
    }

    [TestMethod]
    [DataRow(WarpIrOpCode.LoadInput)]
    [DataRow(WarpIrOpCode.LoadScalar)]
    public void ImmutableFunctionAdmissionRejectsArraySourceInstructions(WarpIrOpCode operation)
    {
        var function = new WarpControlFlowFunction(0, "ordinary-illegal-array-function", 2,
            [new(0, [], [new(0, operation)], new WarpReturnTerminator(0))]);
        Assert.ThrowsExactly<ArgumentException>(() => NarrowKernel(function));
    }

    [TestMethod]
    public void GeneratedBudgetFailureDisposesAdmissionAndBalancesTheLogicalCall()
    {
        CoreCLRJitKernel core = CoreCLRJitKernel.Compile(NarrowKernel());
        uint[] input = [0xFFFFFFFF];
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [input]);
        var budget = new CoreCLRExecutionBudget(1, 4);
        Assert.ThrowsExactly<CoreCLRResourceLimitException>(() => InvokeDirect(core, 1, [input], [3], budget));
        Assert.AreEqual(0L, budget.StepsConsumed);
        Assert.AreEqual(0, budget.CurrentCallDepth);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        AssertDenied(() => InvokeDirect(core, 1, [input], [3], new(100, 4)), reflection: false);
    }
}
