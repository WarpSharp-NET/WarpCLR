using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableSourceFaultPoolCases
{
    internal static void GeneratedFaultPreparationUsesExactMessagesTypesParametersAndHResults()
    {
        Fixture fixture = Create("Round"); uint[] arena = fixture.Arena;
        Lease(arena); PrepareResources(fixture);
        ArgumentOutOfRangeException reference = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MathF.Round(1, -1, MidpointRounding.ToEven));
        foreach (WarpPortableSourceFaultPoolResource resource in fixture.Plan.Resources)
        {
            uint row = ResourceRow(arena, resource.Id); CollectionAssert.AreEqual(resource.Text.ToCharArray(), Text(arena, Reference(arena, row + 2)).ToCharArray());
        }
        for (uint index = 0; index < fixture.Plan.PreparedCount; index++)
        {
            Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.PrepareSourceFaultRecord), arena, [index]));
            uint record = PreparedRow(arena, index), rowId = arena[record + 1];
            Assert.AreEqual(0u, arena[WarpPortableHeapLayout.PendingResult]);
            Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ValidatePreparedSourceFault), arena, [rowId, index]));
            uint[] owner = Reference(arena, WarpPortableHeapLayout.Result); uint payload = Payload(arena, owner);
            Assert.AreEqual(fixture.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(ArgumentOutOfRangeException))),
                arena[Slot(arena, owner[1]) + WarpPortableHeapLayout.SlotType]);
            string message = (string)typeof(Exception).GetField("_message", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reference)!;
            Assert.AreEqual(message, Text(arena, Reference(arena, payload)), StringComparer.Ordinal);
            Assert.AreEqual(reference.ParamName, Text(arena, Reference(arena, payload + 12)), StringComparer.Ordinal);
            Assert.AreEqual(unchecked((uint)reference.HResult), arena[payload + 21]);
            Assert.AreEqual(0u, arena[payload + 15] | arena[payload + 16] | arena[payload + 17]);
            Assert.AreNotEqual(reference.Message, message, StringComparer.Ordinal); // Virtual formatting is still a separate contract.
            Acknowledge(arena);
        }
        Release(arena); Assert.AreEqual(fixture.Plan.RootCount, arena[WarpPortableHeapLayout.LiveRoots]);
    }

    internal static void PreparedFaultGenerationsAndRuntimeRootsCannotBeReusedOrReleasedByAHostRootCall()
    {
        Fixture fixture = Create("Divide"); uint[] arena = fixture.Arena; Lease(arena); PrepareResources(fixture);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.PrepareSourceFaultRecord), arena, [0]));
        uint first = PreparedRow(arena, 0), used = arena[WarpPortableHeapLayout.UsedWords]; uint[] before = arena.Skip((int)first).Take(32).ToArray();
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, Service(nameof(WarpPortableHeapServices.PrepareSourceFaultRecord), arena, [0]));
        Assert.AreEqual(used, arena[WarpPortableHeapLayout.UsedWords]); CollectionAssert.AreEqual(before, arena.Skip((int)first).Take(32).ToArray());
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.PrepareSourceFaultRecord), arena, [1]));
        uint second = PreparedRow(arena, 1); Assert.AreNotEqual(arena[first + 3], arena[second + 3]);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, Service(nameof(WarpPortableHeapServices.ReleaseRoot), arena, [arena[first + 5], arena[first + 6]]));
        Release(arena);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.RequestCollection), arena, []));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.Collect), arena, []));
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.Result]);
        Lease(arena); Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ValidatePreparedSourceFault), arena, [arena[first + 1], 0])); Acknowledge(arena); Release(arena);
    }

    internal static void FaultPreparationRejectsMetadataTextLeaseAndOriginalSiteMismatchBeforeAllocation()
    {
        Fixture fixture = Create("Divide"); uint[] arena = fixture.Arena; uint descriptor = arena[57], row = ResourceRow(arena, 1);
        uint used = arena[WarpPortableHeapLayout.UsedWords];
        Assert.AreEqual(WarpPortableHeapLayout.Busy, Service(nameof(WarpPortableHeapServices.PrepareSourceFaultResource), arena, [1]));
        Lease(arena); uint text = arena[row]; uint saved = arena[text]; arena[text] = 0x10000;
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, Service(nameof(WarpPortableHeapServices.PrepareSourceFaultResource), arena, [1]));
        Assert.AreEqual(used, arena[WarpPortableHeapLayout.UsedWords]); Assert.AreEqual(0u, arena[row + 7]); arena[text] = saved;
        arena[descriptor + 24] ^= 1;
        Assert.AreEqual(WarpPortableHeapLayout.InvalidType, Service(nameof(WarpPortableHeapServices.PrepareSourceFaultResource), arena, [1]));
        arena[descriptor + 24] ^= 1; PrepareResources(fixture);
        uint record = PreparedRow(arena, 0); arena[record + 20] ^= 1; used = arena[WarpPortableHeapLayout.UsedWords];
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, Service(nameof(WarpPortableHeapServices.PrepareSourceFaultRecord), arena, [0]));
        Assert.AreEqual(used, arena[WarpPortableHeapLayout.UsedWords]); Assert.AreEqual(0u, arena[record]); Release(arena);
    }

    internal static void PreparedValidationPreservesReadyStateAndDoesNotMintSourceAuthority()
    {
        Fixture fixture = Create("Divide"); uint[] arena = fixture.Arena; Lease(arena); PrepareResources(fixture);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.PrepareSourceFaultRecord), arena, [0]));
        uint record = PreparedRow(arena, 0), descriptor = arena[57]; uint[] before = arena.Skip((int)record).Take(32).ToArray();
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ValidatePreparedSourceFault), arena, [arena[record + 1], 0]));
        CollectionAssert.AreEqual(before, arena.Skip((int)record).Take(32).ToArray()); Assert.AreEqual(0u, arena[record + 7]);
        Assert.IsTrue(arena.Skip((int)descriptor + 48).Take(16).All(word => word == 0)); Acknowledge(arena);
        uint[] owner = Reference(arena, record + 2); uint payload = Payload(arena, owner), hresult = arena[payload + 21];
        arena[payload + 21] = 0;
        Assert.AreNotEqual(0u, Service(nameof(WarpPortableHeapServices.ValidatePreparedSourceFault), arena, [arena[record + 1], 0]));
        arena[payload + 21] = hresult; arena[record + 4]++;
        Assert.AreNotEqual(0u, Service(nameof(WarpPortableHeapServices.ValidatePreparedSourceFault), arena, [arena[record + 1], 0]));
        arena[record + 4]--; Release(arena);
        WarpVerificationException unbound = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(fixture.Graph, fixture.Typed));
        Assert.AreEqual("WRPCLR2300", unbound.Code, StringComparer.Ordinal);
    }

    internal static void FaultPoolAttachmentIsBoundedAtomicAndCannotRepurposeReservedRoots()
    {
        Fixture fixture = Create("Divide"); uint[] pristine = fixture.Schema.CreateArena(701, 4096, 32, 64, 2, 4096);
        uint[] before = (uint[])pristine.Clone(); uint[] attached = fixture.Plan.AttachToEmptyHeap(pristine, 1);
        CollectionAssert.AreEqual(before, pristine); Assert.AreNotEqual(0u, attached[57]);
        Assert.ThrowsExactly<WarpVerificationException>(() => fixture.Plan.AttachToEmptyHeap(attached, 1));
        uint root = pristine[WarpPortableHeapLayout.RootStart]; pristine[root + WarpPortableHeapLayout.RootOwnership] = WarpPortableHeapLayout.RuntimeOwnedRoot;
        before = (uint[])pristine.Clone(); Assert.ThrowsExactly<WarpVerificationException>(() => fixture.Plan.AttachToEmptyHeap(pristine, 1));
        CollectionAssert.AreEqual(before, pristine);
        Assert.Throws<WarpCompilationResourceException>(() => WarpPortableSourceFaultPoolPlan.Create(fixture.Contract, fixture.Schema, fixture.Bodies, uint.MaxValue));
    }

    internal static Fixture Create(string method)
    {
        Type source = typeof(WarpPortableSourceFaultFactoryCases).GetNestedType("Source", BindingFlags.NonPublic)!;
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source.GetMethod(method)!); WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableSourceFaultFactoryContract contract = WarpPortableSourceFaultFactoryContract.CaptureFinite(graph, typed, schema);
        WarpPortableWordBody[] bodies = typed.Methods.OrderBy(method => method.Identity, StringComparer.Ordinal).Select((method, index) =>
            WarpPortableWordLowerer.PlanPrivateStorage(graph, typed, method.Identity, index + 1)).ToArray();
        WarpPortableSourceFaultPoolPlan plan = WarpPortableSourceFaultPoolPlan.Create(contract, schema, bodies, 2);
        uint[] arena = plan.AttachToEmptyHeap(schema.CreateArena(701, 4096, 32, 64, 2, 4096), 1);
        return new(graph, typed, schema, contract, bodies, plan, arena);
    }

    private static void PrepareResources(Fixture fixture)
    {
        foreach (WarpPortableSourceFaultPoolResource resource in fixture.Plan.Resources)
        {
            Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.PrepareSourceFaultResource), fixture.Arena, [resource.Id]));
        }
    }
    internal static uint Service(string name, uint[] arena, uint[] arguments)
    {
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableHeapServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout); uint[] state = layout.CreateInitialState(64, 10000000);
        uint[][] inputs = arguments.Length == 0 ? [[0]] : arguments.Select(value => new[] { value }).ToArray(); int quanta = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && quanta++ < 100000)
        {
            compiled.ExecuteManagedQuantum(inputs, [], 0, state, 64, layout.MaximumBlockCost, arena);
        }
        Assert.IsLessThan(100000, quanta); Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return state[layout.GetResultWordOffset(0, 64)];
    }
    private static void Lease(uint[] arena) => Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
    private static void Acknowledge(uint[] arena) => Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), arena, [uint.MaxValue]));
    private static void Release(uint[] arena) => Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    private static uint ResourceRow(uint[] arena, uint id) => arena[arena[57] + WarpPortableSourceFaultFactoryLayout.ResourceStart] + (id - 1) * WarpPortableSourceFaultFactoryLayout.ResourceWords;
    private static uint PreparedRow(uint[] arena, uint index) => arena[arena[57] + WarpPortableSourceFaultFactoryLayout.PreparedStart] + index * WarpPortableSourceFaultFactoryLayout.PreparedWords;
    private static uint Slot(uint[] arena, uint slot) => arena[WarpPortableHeapLayout.SlotStart] + (slot - 1) * WarpPortableHeapLayout.SlotWords;
    private static uint Payload(uint[] arena, uint[] owner) => arena[Slot(arena, owner[1]) + WarpPortableHeapLayout.SlotPayload];
    private static uint[] Reference(uint[] arena, uint offset) => arena.Skip((int)offset).Take(3).ToArray();
    private static string Text(uint[] arena, uint[] owner)
    {
        uint slot = Slot(arena, owner[1]), payload = arena[slot + WarpPortableHeapLayout.SlotPayload], length = arena[slot + WarpPortableHeapLayout.SlotLength];
        return new(Enumerable.Range(0, (int)length).Select(index => (char)(arena[payload + ((uint)index >> 1)] >> ((index & 1) * 16))).ToArray());
    }
    internal sealed record Fixture(WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed, WarpPortableSourceHeapSchema Schema,
        WarpPortableSourceFaultFactoryContract Contract, WarpPortableWordBody[] Bodies, WarpPortableSourceFaultPoolPlan Plan, uint[] Arena);
}
