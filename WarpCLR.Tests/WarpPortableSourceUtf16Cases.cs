using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableSourceUtf16Cases
{
    private static readonly string[] CollisionTexts = ["\uD800", "\uD801", "\uDC00", "\uDC01", "\uFFFD"];

    internal static void RawSourceUtf16IdentityDistinguishesTheActualJsonReplacementCollision()
    {
        Assert.HasCount(1, CollisionTexts.Select(text => Convert.ToHexString(JsonSerializer.SerializeToUtf8Bytes(text))).Distinct(StringComparer.Ordinal));
        Assert.HasCount(CollisionTexts.Length, CollisionTexts.Select(WarpPortableSourceUtf16Identity.Hash).Distinct(StringComparer.Ordinal));
        foreach (string text in CollisionTexts)
        {
            CollectionAssert.AreEqual(text.Select(character => (uint)character).ToArray(), WarpPortableSourceUtf16Identity.CodeUnits(text));
        }
        Assert.AreNotEqual(WarpPortableSourceUtf16Identity.Hash(""), WarpPortableSourceUtf16Identity.Hash("\0"), StringComparer.Ordinal);
        Assert.AreNotEqual(WarpPortableSourceUtf16Identity.Hash("\uD800\uDC00"), WarpPortableSourceUtf16Identity.Hash("\uFFFD"), StringComparer.Ordinal);
        Assert.AreNotEqual(WarpPortableSourceUtf16Identity.Hash("\uD800\uDC00"), WarpPortableSourceUtf16Identity.Hash("\uDC00\uD800"), StringComparer.Ordinal);
    }

    internal static void ResourceAndFactoryDataHashesKeepExactUnpairedMessageAndParameterUnits()
    {
        WarpPortableSourceFaultPoolCases.Fixture baseline = WarpPortableSourceFaultPoolCases.Create("Round");
        Fixture[] messages = CollisionTexts.Select(text => Create(baseline, text, "digits", "fixed-operation")).ToArray();
        Assert.HasCount(CollisionTexts.Length, messages.Select(item => item.Contract.Resources.ContractHash).Distinct(StringComparer.Ordinal));
        Assert.HasCount(CollisionTexts.Length, messages.Select(item => item.Contract.ContractHash).Distinct(StringComparer.Ordinal));
        Assert.HasCount(CollisionTexts.Length, messages.Select(item => item.Plan.PlanHash).Distinct(StringComparer.Ordinal));
        Fixture[] parameters = CollisionTexts.Select(text => Create(baseline, "fixed-message", text, "fixed-operation")).ToArray();
        Assert.HasCount(1, parameters.Select(item => item.Contract.Resources.ContractHash).Distinct(StringComparer.Ordinal));
        Assert.HasCount(CollisionTexts.Length, parameters.Select(item => item.Contract.ContractHash).Distinct(StringComparer.Ordinal));
        Assert.HasCount(CollisionTexts.Length, parameters.Select(item => item.Plan.PlanHash).Distinct(StringComparer.Ordinal));
        // These are compiler-data forgery witnesses, never a production resource
        // capture or an invocation grant. The ordinary source path still denies faults.
        Assert.ThrowsExactly<WarpCLR.Verifier.WarpVerificationException>(() => WarpPortableWordLowerer.Lower(baseline.Graph, baseline.Typed));
    }

    internal static void OperationDescriptorUsesTheSameVersionedRawUtf16IdentityAsItsCapturedRow()
    {
        WarpPortableSourceFaultPoolCases.Fixture baseline = WarpPortableSourceFaultPoolCases.Create("Round");
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (string text in CollisionTexts)
        {
            Fixture fixture = Create(baseline, "fixed-message", "digits", text);
            uint descriptor = fixture.Arena[WarpPortableSourceFaultFactoryLayout.Descriptor];
            uint row = fixture.Arena[descriptor + WarpPortableSourceFaultFactoryLayout.RowStart];
            byte[] expected = Convert.FromHexString(WarpPortableSourceUtf16Identity.Hash(text));
            uint[] words = Enumerable.Range(0, 8).Select(index => BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(index * 4))).ToArray();
            CollectionAssert.AreEqual(words, fixture.Arena.AsSpan((int)(row + WarpPortableSourceFaultFactoryLayout.RowOperationHash), 8).ToArray());
            hashes.Add(Convert.ToHexString(expected));
        }
        Assert.HasCount(CollisionTexts.Length, hashes);
    }

    internal static void ActualGeneratedResourceAndExceptionPreparationPreservesEveryRawUtf16Unit()
    {
        WarpPortableSourceFaultPoolCases.Fixture baseline = WarpPortableSourceFaultPoolCases.Create("Round");
        foreach (string text in CollisionTexts.Append("\uD800\uDC00\0\uDC01\uFFFD"))
        {
            Fixture fixture = Create(baseline, text, text, "fixed-operation"); uint[] arena = fixture.Arena;
            Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, uint.MaxValue));
            foreach (WarpPortableSourceFaultPoolResource resource in fixture.Plan.Resources)
            {
                Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.PrepareSourceFaultResource), arena, resource.Id));
            }
            Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.PrepareSourceFaultRecord), arena, 0));
            Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ValidatePreparedSourceFault), arena, fixture.Contract.Rows[0].Id, 0));
            uint slot = arena[WarpPortableHeapLayout.SlotStart] + (arena[WarpPortableHeapLayout.Result + 1] - 1) * WarpPortableHeapLayout.SlotWords;
            uint payload = arena[slot + WarpPortableHeapLayout.SlotPayload];
            Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), arena, uint.MaxValue));
            foreach (uint offset in new uint[] { WarpPortableSourceExceptionLayout.MessageWord, WarpPortableSourceExceptionLayout.ParamNameWord })
            {
                uint[] owner = arena.AsSpan((int)(payload + offset), 3).ToArray();
                uint stringSlot = arena[WarpPortableHeapLayout.SlotStart] + (owner[1] - 1) * WarpPortableHeapLayout.SlotWords;
                Assert.AreEqual((uint)text.Length, arena[stringSlot + WarpPortableHeapLayout.SlotLength]);
                for (uint index = 0; index < text.Length; index++)
                {
                    Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.StringCharacter), arena, owner[0], owner[1], owner[2], index));
                    Assert.AreEqual((uint)text[(int)index], arena[WarpPortableHeapLayout.Result]);
                }
            }
            Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, uint.MaxValue));
        }
    }

    // Invoke compiler metadata constructors to prove that differently forged raw
    // data cannot collide. No original user method/cctor/exception constructor runs.
    private static Fixture Create(WarpPortableSourceFaultPoolCases.Fixture baseline, string message, string parameter, string operation)
    {
        WarpPortableSourceFaultFactoryRow original = baseline.Contract.Rows.First(row => row.FaultDescriptor == 2);
        WarpPortableSourceFaultFactoryRow row = original with { Id = 1, ParamName = parameter, OperationIdentity = operation };
        ImmutableArray<WarpPortableSourceExceptionResource> data = [new(row.ResourceKey, message)];
        var resources = (WarpPortableSourceExceptionResources)OnlyConstructor(typeof(WarpPortableSourceExceptionResources)).Invoke(
            [baseline.Contract.Resources.CorelibHash, baseline.Contract.Resources.UiCulture, data]);
        ImmutableArray<WarpPortableSourceFaultFactoryRow> rows = [row];
        var contract = (WarpPortableSourceFaultFactoryContract)OnlyConstructor(typeof(WarpPortableSourceFaultFactoryContract)).Invoke(
            [baseline.Graph, baseline.Typed, baseline.Schema, rows, resources]);
        WarpPortableSourceFaultPoolPlan plan = WarpPortableSourceFaultPoolPlan.Create(contract, baseline.Schema, baseline.Bodies, 1);
        uint[] arena = plan.AttachToEmptyHeap(baseline.Schema.CreateArena(709, 4096, 32, 64, 2, 4096), 1);
        return new(contract, plan, arena);
    }

    private static ConstructorInfo OnlyConstructor(Type type)
    {
        ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.HasCount(1, constructors); return constructors[0];
    }

    private static uint Service(string name, uint[] arena, params uint[] arguments) => WarpPortableSourceFaultPoolCases.Service(name, arena, arguments);
    private sealed record Fixture(WarpPortableSourceFaultFactoryContract Contract, WarpPortableSourceFaultPoolPlan Plan, uint[] Arena);
}
