using System.Diagnostics.CodeAnalysis;
using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed partial class WarpPortableCollectiveTests
{
    private const string ParentSchemaIdentity = "02164cc0f4643ca3b90c144e4bc9a5e0fcb03ce6f50b3e2f42b5b8e72e1a563a";
    [TestMethod]
    public void PlansAreImmutableBoundedAndBindEverySemanticChoice()
    {
        WarpPortableCollectivePlan plan = Plan(6, 1, 0, 2, 129);
        Assert.AreEqual(plan.Identity, Plan(6, 1, 0, 2, 129).Identity, StringComparer.Ordinal);
        Assert.AreNotEqual(plan.Identity, Plan(5, 1, 0, 2, 129).Identity, StringComparer.Ordinal);
        Assert.AreNotEqual(plan.Identity, Plan(6, 4, 0, 2, 129).Identity, StringComparer.Ordinal);
        Assert.AreNotEqual(plan.Identity, Plan(6, 1, 0, 3, 129).Identity, StringComparer.Ordinal);
        Assert.AreNotEqual(plan.Identity, WarpPortableCollectivePlan.Create(6, 1, 0, 2, 129, [19, 7, 3], ParentSchemaIdentity).Identity, StringComparer.Ordinal);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<uint>)plan.Metadata)[0] = 0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Plan(1, 1, 0, 1, 65537));
        Assert.ThrowsExactly<ArgumentException>(() => Plan(5, 1, 1, 1, 1));
        Assert.ThrowsExactly<ArgumentException>(() => WarpPortableCollectivePlan.Create(1, 1, 0, 1, 1, [7, 7], ParentSchemaIdentity));
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpPortableCollectivePlan.Create(1, 1, 0, 1, 17, [7], ParentSchemaIdentity, 64));
        Assert.ThrowsExactly<ArgumentException>(() => Plan(5, 1, 0, 1, 1).CreateArena([0, 1]));
        WarpPortableCollectivePlan maximum = Plan(6, 1, 0, 2, 65536);
        Assert.IsLessThanOrEqualTo(WarpPortableCollectiveLayout.MaximumScratchWords, maximum.ScratchWords);
        Assert.IsLessThanOrEqualTo(WarpPortableCollectiveLayout.MaximumNodes, maximum.Nodes);
        Assert.IsLessThanOrEqualTo(34u, maximum.Levels);
        Console.WriteLine($"collective maximum plan nodes={maximum.Nodes}, levels={maximum.Levels}, scratchWords={maximum.ScratchWords}, hash={maximum.Identity}");
    }

    [TestMethod]
    public void EveryTypeOperationAndPrefixMatchesIndependentIntegerAndFloatingOracles()
    {
        foreach ((uint type, uint operation, uint overflow, uint kind) in Cases())
        {
            foreach (uint count in new uint[] { 0, 1, 2, 3, 6, 17, 65 })
            {
                Check(type, operation, overflow, kind, Inputs(type, count, edges: true), generated: false, minimum: false, residents: 3);
                Check(type, operation, overflow, kind, Inputs(type, count, edges: false), generated: false, minimum: false, residents: 1);
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EveryNumericCombinationExecutesActualGeneratedCoreCLR(bool minimumQuantum)
    {
        foreach ((uint type, uint operation, uint overflow, uint kind) in Cases())
        {
            Check(type, operation, overflow, kind, Inputs(type, 6, edges: false), generated: true, minimum: minimumQuantum, residents: 3);
            Check(type, operation, overflow, kind, Inputs(type, 6, edges: true), generated: true, minimum: minimumQuantum, residents: 3);
            Check(type, operation, overflow, kind, Inputs(type, 1, edges: true), generated: true, minimum: minimumQuantum, residents: 1);
            Check(type, operation, overflow, kind, [], generated: true, minimum: minimumQuantum, residents: 1);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FloatingOrderAndScanPrefixIdentitySurviveOversubscription(bool minimumQuantum)
    {
        foreach (uint type in new uint[] { 5, 6 })
        {
            ulong[] inputs = CancellationWitness(type, 129);
            foreach (int residents in new[] { 1, 3, 7 })
            {
                foreach (uint kind in new uint[] { 1, 2, 3 })
                {
                    Check(type, 1, 0, kind, inputs, generated: true, minimum: minimumQuantum, residents, parallel: residents == 7);
                }
            }
            var inclusive = WarpPortableCollectiveOracle.Evaluate(type, 1, 0, 2, inputs);
            var exclusive = WarpPortableCollectiveOracle.Evaluate(type, 1, 0, 3, inputs);
            for (int i = 1; i < inputs.Length; i++) { Assert.AreEqual(inclusive[i - 1].Bits, exclusive[i].Bits); }
            Assert.AreEqual(WarpPortableCollectiveOracle.Evaluate(type, 1, 0, 1, inputs)[0].Bits, inclusive[^1].Bits);
        }
        Assert.IsGreaterThan(0L, WarpPortableCollectiveDriver.SimultaneousJobs);
    }

    private static WarpPortableCollectivePlan Plan(uint type, uint operation, uint overflow, uint kind, uint count) =>
        WarpPortableCollectivePlan.Create(type, operation, overflow, kind, count, Enumerable.Range(0, 131).Select(static member => (uint)(member * 3 + 7)), ParentSchemaIdentity);

    private static IEnumerable<(uint Type, uint Operation, uint Overflow, uint Kind)> Cases()
    {
        for (uint type = 1; type <= 6; type++)
        {
            for (uint operation = 1; operation <= 4; operation++)
            {
                for (uint kind = 1; kind <= 3; kind++)
                {
                    yield return (type, operation, 0, kind);
                    if (type <= 4 && (operation == 1 || operation == 4)) { yield return (type, operation, 1, kind); }
                }
            }
        }
    }

    private static ulong[] Inputs(uint type, uint count, bool edges)
    {
        ulong[] values = new ulong[count];
        ulong[] special = type <= 2 ? [uint.MaxValue, 1, 0x80000000, 0x7FFFFFFF, 0, 0xFFFFFFFE] :
            type <= 4 ? [ulong.MaxValue, 1, 0x8000000000000000, 0x7FFFFFFFFFFFFFFF, 0, 0xFFFFFFFFFFFFFFFE] :
            type == 5 ? [0x80000000, 0, 0x7F812345, 0xFF923456, 0x7F800000, 0xFF800000, 1, 0x80000001, 0x7F7FFFFF, 0x00800000] :
            [0x8000000000000000, 0, 0x7FF0000000001234, 0xFFF123456789ABCD, 0x7FF0000000000000, 0xFFF0000000000000, 1, 0x8000000000000001, 0x7FEFFFFFFFFFFFFF, 0x0010000000000000];
        ulong random = 1701 + type * 17 + count;
        for (int i = 0; i < values.Length; i++)
        {
            if (edges) { values[i] = special[i % special.Length]; continue; }
            if (type <= 4) { values[i] = 1 + NextWitness(ref random) % 16; continue; }
            ulong bits = NextWitness(ref random);
            values[i] = type == 5 ? (bits & 0x807FFFFFUL) | (1 + NextWitness(ref random) % 253 << 23) :
                (bits & 0x800FFFFFFFFFFFFFUL) | (1 + NextWitness(ref random) % 2045 << 52);
        }
        return values;
    }

    private static ulong NextWitness(ref ulong state)
    {
        state ^= state << 13; state ^= state >> 7; state ^= state << 17;
        return state;
    }

    private static ulong[] CancellationWitness(uint type, int count)
    {
        ulong big = type == 5 ? 0x4B800000UL : 0x4340000000000000UL;
        ulong sign = type == 5 ? 0x80000000UL : 0x8000000000000000UL;
        ulong one = type == 5 ? 0x3F800000UL : 0x3FF0000000000000UL;
        ulong[] pattern = [big, one, big | sign, one, sign, sign, one, one, one];
        return Enumerable.Range(0, count).Select(i => pattern[i % pattern.Length]).ToArray();
    }
}
