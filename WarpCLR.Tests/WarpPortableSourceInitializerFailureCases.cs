using System.Collections.Immutable;
using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableSourceInitializerFailureCases
{
    internal static void InitializerOperationOriginsSeparateInvocationFromOriginalProtectedCil()
    {
        Fixture root = Capture(typeof(CapturedFailure), nameof(CapturedFailure.ProtectedRead));
        Assert.HasCount(1, root.Plan.Rows.Where(row => row.Origin.Kind == WarpPortableSourceOperationOriginKind.EntryInvocation).ToArray());
        WarpPortableSourceInitializerFailureRow entry = root.Plan.Rows.First(row => row.Origin.Kind == WarpPortableSourceOperationOriginKind.EntryInvocation);
        Assert.IsNull(entry.Origin.SourceOffset); Assert.IsNull(entry.Origin.SourceOpCode); Assert.IsNull(entry.Origin.EffectIndex);
        Assert.IsEmpty(entry.Origin.Effects); Assert.IsEmpty(entry.Origin.ExceptionMemberships);
        Assert.IsTrue(root.Typed.Methods.First(method => string.Equals(method.Identity, root.Graph.EntryIdentity, StringComparison.Ordinal))
            .Instructions.Any(instruction => instruction.Reachable && !instruction.ExceptionMemberships.IsEmpty));
        Fixture caller = Capture(typeof(Calls), nameof(Calls.ProtectedCall));
        Assert.HasCount(1, caller.Plan.Rows.Where(row => string.Equals(row.Origin.MethodIdentity, caller.Graph.EntryIdentity, StringComparison.Ordinal)).ToArray());
        WarpPortableSourceInitializerFailureRow instruction = caller.Plan.Rows.First(row =>
            string.Equals(row.Origin.MethodIdentity, caller.Graph.EntryIdentity, StringComparison.Ordinal));
        Assert.AreEqual(WarpPortableSourceOperationOriginKind.Instruction, instruction.Origin.Kind);
        WarpPortableTypedInstruction original = caller.Typed.Methods.First(method => string.Equals(method.Identity, caller.Graph.EntryIdentity, StringComparison.Ordinal))
            .Instructions.First(original => original.Offset == instruction.Origin.SourceOffset);
        Assert.AreEqual(unchecked((ushort)OpCodes.Call.Value), instruction.Origin.SourceOpCode);
        Assert.AreEqual(WarpPortableTypedEffect.TypeInitialize, instruction.Origin.Effects[instruction.Origin.EffectIndex!.Value]);
        CollectionAssert.AreEqual(original.ExceptionMemberships.ToArray(), instruction.Origin.ExceptionMemberships.ToArray());
        Assert.AreEqual(WarpPortableSnapshotIdentity.Hash(original), instruction.Origin.CapturedSourceHash, StringComparer.Ordinal);
        Assert.AreNotEqual(entry.Origin.OriginHash, instruction.Origin.OriginHash, StringComparer.Ordinal);
        Assert.AreEqual(0u, CaptureProbe.Calls);
    }

    internal static void InitializerOriginsRejectFabricatedOpcodesEffectsAndOtherClosures()
    {
        Fixture fixture = Capture(typeof(CapturedFailure), nameof(CapturedFailure.ProtectedRead));
        WarpPortableSourceInitializerPlan plan = WarpPortableSourceInitializerPlan.Capture(fixture.Graph, fixture.Typed, fixture.Schema);
        Assert.HasCount(1, plan.Triggers.Where(trigger => trigger.EntryInvocation).ToArray());
        WarpPortableSourceInitializerTrigger root = plan.Triggers.First(trigger => trigger.EntryInvocation);
        foreach (WarpPortableSourceInitializerTrigger altered in new[]
        {
            root with { SourceOffset = 0 }, root with { SourceOpCode = 0 }, root with { EffectIndex = 0 }, root with { EntryInvocation = false },
        })
        {
            WarpVerificationException failure = Assert.ThrowsExactly<WarpVerificationException>(() =>
                WarpPortableSourceOperationOrigin.CaptureInitializer(fixture.Graph, fixture.Typed, plan, altered));
            Assert.AreEqual("WRPCLR2490", failure.Code, StringComparer.Ordinal);
        }
        Fixture other = Capture(typeof(Calls), nameof(Calls.ProtectedCall));
        Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableSourceOperationOrigin.CaptureInitializer(other.Graph, other.Typed, plan, root));
        Assert.AreEqual(0u, CaptureProbe.Calls);
    }

    internal static void InitializerMessagesRetainEveryRawUtf16UnitAndExactCapturedCorelibTemplate()
    {
        Fixture fixture = Capture(typeof(CapturedFailure), nameof(CapturedFailure.ProtectedRead));
        string template = fixture.Plan.Resources.Text(WarpPortableSourceInitializerMessage.ResourceKey);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in new[] { "\uD800", "\uD801", "\uDC00", "\uDC01", "\uFFFD", "A\0\uD800Z", "Closed`1", "Inner" })
        {
            ImmutableArray<ushort> units = name.Select(unit => (ushort)unit).ToImmutableArray();
            ImmutableArray<ushort> message = WarpPortableSourceInitializerMessage.Materialize(template, units);
            // Independent standard CoreCLR constructor oracle, never source execution.
            var expected = new TypeInitializationException(name, null);
            CollectionAssert.AreEqual(expected.Message.Select(unit => (ushort)unit).ToArray(), message.ToArray());
            Assert.IsTrue(hashes.Add(WarpPortableSnapshotIdentity.Hash(message)));
        }
        CollectionAssert.AreEqual("{x}".Select(unit => (ushort)unit).ToArray(),
            WarpPortableSourceInitializerMessage.Materialize("{{{0}}}", [(ushort)'x']).ToArray());
        foreach (string invalid in new[] { "{1}", "{0,12}", "{0:X}", "{", "}" })
        {
            Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableSourceInitializerMessage.Materialize(invalid, []));
        }
        Assert.AreEqual(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, fixture.Plan.Resources.RuntimeProfile, StringComparer.Ordinal);
        Assert.AreEqual(0u, CaptureProbe.Calls);
    }

    internal static void GeneratedInitializerFailureConstructionCachesExactWrapperAndOrdinaryInnerTypes()
    {
        foreach (Type innerType in new[] { typeof(InvalidOperationException), typeof(TypeInitializationException), typeof(OutOfMemoryException), typeof(StackOverflowException) })
        {
            Fixture fixture = Ready(); uint[] inner = Inner(fixture, innerType);
            Assert.AreEqual(0u, Cache(fixture, inner));
            uint[] wrapper = Reference(fixture.Arena, fixture.Record + WarpPortableSourceInitializerFailureLayout.WrapperReference);
            uint payload = Payload(fixture.Arena, wrapper);
            Assert.AreNotEqual(inner[1], wrapper[1]);
            CollectionAssert.AreEqual(inner, Reference(fixture.Arena, payload + WarpPortableSourceExceptionLayout.InnerExceptionWord));
            CollectionAssert.AreEqual(wrapper, Reference(fixture.Arena, TypeRecord(fixture) + WarpPortableHeapLayout.InitializerException));
            Assert.AreEqual(3u, fixture.Arena[TypeRecord(fixture) + WarpPortableHeapLayout.InitializerState]);
            WarpPortableSourceInitializerFailureType data = fixture.Plan.Types[(int)fixture.Entry.TypeRecord - 1];
            CollectionAssert.AreEqual(data.TypeName.ToArray(), Text(fixture.Arena, Reference(fixture.Arena, payload + WarpPortableSourceExceptionLayout.TypeNameWord)));
            CollectionAssert.AreEqual(data.Message.ToArray(), Text(fixture.Arena, Reference(fixture.Arena, payload + WarpPortableSourceExceptionLayout.MessageWord)));
            Assert.AreEqual(data.DefaultHResult, fixture.Arena[payload + WarpPortableSourceExceptionLayout.HResultWord]);
            Assert.AreEqual(0u, fixture.Arena[WarpPortableHeapLayout.PendingResult]);
            uint descriptor = fixture.Arena[WarpPortableSourceInitializerFailureLayout.Descriptor];
            Assert.IsTrue(fixture.Arena.AsSpan((int)(descriptor + WarpPortableSourceInitializerFailureLayout.AdmittedProgramHash), 16).ToArray().All(word => word == 0));
        }
        Assert.AreEqual(0u, CaptureProbe.Calls);
    }

    internal static void CachedInitializerWrapperResetsOnlyItsOwnTraceAndKeepsMutationAndOriginalInner()
    {
        Fixture fixture = Ready(); uint[] inner = Inner(fixture, typeof(Exception));
        Assert.AreEqual(0u, Cache(fixture, inner));
        uint[] wrapper = Reference(fixture.Arena, fixture.Record + WarpPortableSourceInitializerFailureLayout.WrapperReference);
        uint payload = Payload(fixture.Arena, wrapper), originalPayload = Payload(fixture.Arena, inner);
        uint[] trace = Trace(fixture);
        for (uint word = 0; word < 3; word++)
        {
            fixture.Arena[payload + WarpPortableSourceExceptionLayout.TraceReferenceWord + word] = trace[word];
            fixture.Arena[originalPayload + WarpPortableSourceExceptionLayout.TraceReferenceWord + word] = trace[word];
        }
        fixture.Arena[payload + WarpPortableSourceExceptionLayout.TraceIdentityWord] = 100;
        fixture.Arena[originalPayload + WarpPortableSourceExceptionLayout.TraceIdentityWord] = 101;
        Assert.AreEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.WriteSourceExceptionHResult), [.. wrapper, 0xA1234567]));
        uint[] beforeInner = fixture.Arena.AsSpan((int)originalPayload, WarpPortableSourceExceptionLayout.PrefixWords).ToArray();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Assert.AreEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.ReadCachedSourceInitializerFailure), [fixture.Entry.TypeRecord]));
            CollectionAssert.AreEqual(wrapper, Reference(fixture.Arena, WarpPortableHeapLayout.Result)); Acknowledge(fixture);
            Assert.IsTrue(fixture.Arena.AsSpan((int)(payload + WarpPortableSourceExceptionLayout.TraceReferenceWord), 6).ToArray().All(word => word == 0));
            CollectionAssert.AreEqual(beforeInner, fixture.Arena.AsSpan((int)originalPayload, WarpPortableSourceExceptionLayout.PrefixWords).ToArray());
            Assert.AreEqual(0xA1234567u, fixture.Arena[payload + WarpPortableSourceExceptionLayout.HResultWord]);
        }
    }

    internal static void InitializerFailureRootsRetainWrapperNameMessageAndInnerDuringCollection()
    {
        Fixture fixture = Ready(); uint[] inner = Inner(fixture, typeof(Exception)); Assert.AreEqual(0u, Cache(fixture, inner));
        uint[] wrapper = Reference(fixture.Arena, fixture.Record + WarpPortableSourceInitializerFailureLayout.WrapperReference);
        uint root = fixture.Arena[fixture.Record + WarpPortableSourceInitializerFailureLayout.FirstRoot] + 2;
        uint rootRecord = fixture.Arena[WarpPortableHeapLayout.RootStart] + (root - 1) * WarpPortableHeapLayout.RootWords;
        uint[] beforeRoot = fixture.Arena.AsSpan((int)rootRecord, (int)WarpPortableHeapLayout.RootWords).ToArray();
        Assert.AreEqual(WarpPortableHeapLayout.RuntimeOwnedRoot, fixture.Arena[rootRecord + WarpPortableHeapLayout.RootOwnership]);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, Service(fixture, nameof(WarpPortableHeapServices.ReleaseRoot), [root, 1]));
        CollectionAssert.AreEqual(beforeRoot, fixture.Arena.AsSpan((int)rootRecord, (int)WarpPortableHeapLayout.RootWords).ToArray());
        Release(fixture);
        Assert.AreEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.RequestCollection), []));
        Assert.AreEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.Collect), []));
        Assert.AreEqual(4u, fixture.Arena[WarpPortableHeapLayout.LiveObjects]);
        foreach (uint[] owner in new[] { wrapper, inner, Reference(fixture.Arena, fixture.Record + WarpPortableSourceInitializerFailureLayout.NameReference),
            Reference(fixture.Arena, fixture.Record + WarpPortableSourceInitializerFailureLayout.MessageReference) })
        {
            Assert.AreEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.GetType), owner));
        }
        Assert.AreEqual(3u, fixture.Arena[TypeRecord(fixture) + WarpPortableHeapLayout.InitializerState]);
    }

    internal static void InitializerFailureRejectsInvalidOwnersOriginsAndDataBeforeCacheWrites()
    {
        Fixture fixture = Ready(); uint[] inner = Inner(fixture, typeof(Exception));
        uint[] wrapper = Reference(fixture.Arena, fixture.Record + WarpPortableSourceInitializerFailureLayout.WrapperReference);
        uint payload = Payload(fixture.Arena, wrapper); uint[] before = fixture.Arena.AsSpan((int)payload, 22).ToArray();
        foreach (uint[] invalid in new[] { new uint[3], new[] { inner[0] + 1, inner[1], inner[2] }, new[] { inner[0], inner[1], inner[2] + 1 } })
        {
            Assert.AreNotEqual(0u, Cache(fixture, invalid)); CollectionAssert.AreEqual(before, fixture.Arena.AsSpan((int)payload, 22).ToArray());
            Assert.AreEqual(1u, fixture.Arena[TypeRecord(fixture) + WarpPortableHeapLayout.InitializerState]);
        }
        Assert.AreNotEqual(0u, Cache(fixture, inner, wrongContext: true));
        uint descriptor = fixture.Arena[WarpPortableSourceInitializerFailureLayout.Descriptor];
        foreach (uint counter in new[] { WarpPortableSourceInitializerFailureLayout.TypeCount, WarpPortableSourceInitializerFailureLayout.RowCount })
        {
            uint original = fixture.Arena[descriptor + counter];
            fixture.Arena[descriptor + counter] = uint.MaxValue;
            Assert.AreEqual(WarpPortableHeapLayout.Bounds, Cache(fixture, inner));
            CollectionAssert.AreEqual(before, fixture.Arena.AsSpan((int)payload, 22).ToArray());
            fixture.Arena[descriptor + counter] = original;
        }
        uint row = fixture.Arena[descriptor + WarpPortableSourceInitializerFailureLayout.RowStart] + (fixture.Entry.Id - 1) * WarpPortableSourceInitializerFailureLayout.RowWords;
        fixture.Arena[row + WarpPortableSourceInitializerFailureLayout.RowOpcode] = unchecked((ushort)OpCodes.Call.Value);
        Assert.AreNotEqual(0u, Cache(fixture, inner));
        fixture.Arena[row + WarpPortableSourceInitializerFailureLayout.RowOpcode] = 0;
        Assert.AreEqual(0u, Cache(fixture, inner));
        uint[] saved = fixture.Arena.AsSpan((int)payload, 22).ToArray();
        Assert.AreNotEqual(0u, Cache(fixture, inner)); CollectionAssert.AreEqual(saved, fixture.Arena.AsSpan((int)payload, 22).ToArray());
    }

    internal static void InitializerResourceExhaustionRemainsItsOriginalFaultAndCannotPublishACachedDefault()
    {
        Fixture fixture = Capture(typeof(CapturedFailure), nameof(CapturedFailure.ProtectedRead), quota: 8);
        Acquire(fixture);
        // Preserve the existing heap allocation failure descriptor. A source
        // exception or uncatchable resource report requires a separate binding.
        Assert.AreEqual(WarpPortableHeapLayout.OutOfMemory, Service(fixture, nameof(WarpPortableHeapServices.PrepareSourceInitializerFailure), [fixture.Entry.TypeRecord]));
        Assert.AreEqual(WarpPortableHeapLayout.OutOfMemory, fixture.Arena[WarpPortableHeapLayout.Fault]);
        Assert.AreEqual(8u, fixture.Arena[WarpPortableHeapLayout.Argument1]);
        Assert.AreEqual(WarpPortableSourceInitializerFailureLayout.Failed, fixture.Arena[fixture.Record + WarpPortableSourceInitializerFailureLayout.State]);
        Assert.AreEqual(0u, fixture.Arena[TypeRecord(fixture) + WarpPortableHeapLayout.InitializerState]);
        CollectionAssert.AreEqual(new uint[3], Reference(fixture.Arena, fixture.Record + WarpPortableSourceInitializerFailureLayout.WrapperReference));
        Assert.AreNotEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.ReadCachedSourceInitializerFailure), [fixture.Entry.TypeRecord]));
        Assert.AreNotEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.PrepareSourceInitializerFailure), [fixture.Entry.TypeRecord]));
        Assert.AreEqual(0u, fixture.Arena[WarpPortableHeapLayout.PendingResult]);
    }

    internal static void CapturedInitializerFailureDataNeverGrantsActualSourceFailureExecution()
    {
        Fixture fixture = Capture(typeof(CapturedFailure), nameof(CapturedFailure.ProtectedRead));
        Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(fixture.Graph, fixture.Typed));
        WarpVerificationException rejected = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableClosedInitializerSourcePlan.Capture(fixture.Graph, fixture.Typed, fixture.Schema));
        Assert.AreEqual("WRPCLR2480", rejected.Code, StringComparer.Ordinal);
        uint[] heap = fixture.Schema.CreateArena(399, 4096, 32, 32, 1, 4096);
        Assert.ThrowsExactly<WarpVerificationException>(() => fixture.Plan.AttachToEmptyHeap(heap, 0));
        heap[WarpPortableHeapLayout.RootStart] = uint.MaxValue;
        Assert.ThrowsExactly<WarpVerificationException>(() => fixture.Plan.AttachToEmptyHeap(heap, 1));
        Assert.AreEqual(0u, CaptureProbe.Calls);
    }

    private static class CaptureProbe { public static uint Calls; }
    private static class CapturedFailure
    {
        static CapturedFailure() { CaptureProbe.Calls++; Raise(); }
        private static void Raise() => throw new InvalidOperationException("captured-original-cctor");
        public static uint ProtectedRead()
        {
            try { return 1; }
            catch (TypeInitializationException) { return 7; }
        }
    }
    private static class Calls
    {
        public static uint ProtectedCall()
        {
            try { return CapturedFailure.ProtectedRead(); }
            catch (TypeInitializationException) { return 8; }
        }
    }
}
