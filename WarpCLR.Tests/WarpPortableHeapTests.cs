using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using WarpCLR.Compiler;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpPortableHeapTests
{
    [TestMethod]
    public void PreciseTracingPreservesCyclesAndAliasesThenRejectsStaleIdentities()
    {
        uint[] arena = Arena();
        WarpPortableHeapReference left = Allocate(arena, 3);
        WarpPortableHeapReference right = Allocate(arena, 3);
        StoreReference(arena, left, 0, right);
        StoreReference(arena, left, 3, right);
        StoreReference(arena, right, 0, left);
        (uint root, uint generation) = Root(arena, left);
        Collect(arena);
        Assert.AreEqual(2u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(right, ReadReference(arena, left, 0));
        Assert.AreEqual(right, ReadReference(arena, left, 3));
        Assert.AreEqual(left, ReadReference(arena, right, 0));
        Success(WarpPortableHeapServices.ReleaseRoot(arena, root, generation));
        Collect(arena);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.UsedWords]);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, WarpPortableHeapServices.GetType(arena, left.Context, left.Slot, left.Generation));
        WarpPortableHeapReference reused = Allocate(arena, 3);
        Assert.AreEqual(left.Slot, reused.Slot);
        Assert.AreNotEqual(left.Generation, reused.Generation);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, WarpPortableHeapServices.GetType(arena, right.Context, right.Slot, right.Generation));
    }

    [TestMethod]
    public void ScalarHandlePatternsAndStaleRootSlotsDoNotConservativelyRetainObjects()
    {
        uint[] arena = Arena();
        WarpPortableHeapReference keeper = Allocate(arena, 9);
        WarpPortableHeapReference victim = Allocate(arena, 3);
        (uint keeperRoot, uint keeperGeneration) = Root(arena, keeper);
        (uint victimRoot, uint victimGeneration) = Root(arena, victim);
        Success(WarpPortableHeapServices.WriteWord(arena, keeper.Context, keeper.Slot, keeper.Generation, 0, victim.Context));
        Success(WarpPortableHeapServices.WriteWord(arena, keeper.Context, keeper.Slot, keeper.Generation, 1, victim.Slot));
        Success(WarpPortableHeapServices.WriteWord(arena, keeper.Context, keeper.Slot, keeper.Generation, 2, victim.Generation));
        Success(WarpPortableHeapServices.ReleaseRoot(arena, victimRoot, victimGeneration));
        Collect(arena);
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, WarpPortableHeapServices.GetType(arena, victim.Context, victim.Slot, victim.Generation));
        Success(WarpPortableHeapServices.GetType(arena, keeper.Context, keeper.Slot, keeper.Generation));
        Success(WarpPortableHeapServices.ReleaseRoot(arena, keeperRoot, keeperGeneration));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, WarpPortableHeapServices.ReadRoot(arena, victimRoot, victimGeneration));
        Collect(arena);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]);
    }

    [TestMethod]
    public void InteriorRootsKeepOnlyTheirOwnerAndValidateBoundsTypeAndGeneration()
    {
        uint[] arena = Arena();
        WarpPortableHeapReference owner = Allocate(arena, 11);
        Success(WarpPortableHeapServices.MakeInterior(arena, owner.Context, owner.Slot, owner.Generation, 5, 2, 4));
        Assert.AreEqual(5u, arena[WarpPortableHeapLayout.Result + 3]);
        Assert.AreEqual(2u, arena[WarpPortableHeapLayout.Result + 4]);
        Success(WarpPortableHeapServices.AcquireRoot(arena, owner.Context, owner.Slot, owner.Generation, WarpPortableHeapLayout.InteriorRoot, 5, 2, 4));
        uint root = arena[WarpPortableHeapLayout.Result];
        uint generation = arena[WarpPortableHeapLayout.Result + 1];
        Collect(arena);
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, WarpPortableHeapServices.MakeInterior(arena, owner.Context, owner.Slot, owner.Generation, 6, 2, 4));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, WarpPortableHeapServices.MakeInterior(arena, owner.Context, owner.Slot, owner.Generation, 1, 3, 1));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, WarpPortableHeapServices.MakeInterior(arena, owner.Context, owner.Slot, owner.Generation, 5, 0, 4));
        Success(WarpPortableHeapServices.ReleaseRoot(arena, root, generation));
        Collect(arena);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, WarpPortableHeapServices.MakeInterior(arena, owner.Context, owner.Slot, owner.Generation, 5, 2, 4));
    }

    [TestMethod]
    public void ValueFieldsArraysAndBoxesPreserveRawWordsAndPreciseEmbeddedReferences()
    {
        uint[] arena = Arena();
        WarpPortableHeapReference child = Allocate(arena, 3);
        WarpPortableHeapReference composite = Allocate(arena, 11);
        uint[] value = [child.Context, child.Slot, child.Generation, 0x7FF80031u, 0xFF001123u];
        Stage(arena, value);
        Success(WarpPortableHeapServices.WriteValue(arena, composite.Context, composite.Slot, composite.Generation, 0, 5, 0));
        Stage(arena, [0, 0, 0, 0, 0]);
        Success(WarpPortableHeapServices.ReadValue(arena, composite.Context, composite.Slot, composite.Generation, 0, 5, 0));
        CollectionAssert.AreEqual(value, Scratch(arena, 5));
        Success(WarpPortableHeapServices.BoxValue(arena, 5, 0));
        WarpPortableHeapReference box = ResultReference(arena);
        Stage(arena, [0, 0, 0, 0, 0]);
        Success(WarpPortableHeapServices.UnboxValue(arena, box.Context, box.Slot, box.Generation, 5, 0));
        CollectionAssert.AreEqual(value, Scratch(arena, 5));
        Success(WarpPortableHeapServices.AllocateArray(arena, 7, 2));
        WarpPortableHeapReference array = ResultReference(arena);
        Stage(arena, value);
        Success(WarpPortableHeapServices.WriteValueArrayElement(arena, array.Context, array.Slot, array.Generation, 1, 0));
        (uint root, uint generation) = Root(arena, array);
        Collect(arena);
        Assert.AreEqual(2u, arena[WarpPortableHeapLayout.LiveObjects]);
        Success(WarpPortableHeapServices.GetType(arena, child.Context, child.Slot, child.Generation));
        Stage(arena, [0, 0, 0, 0x7FF80031u, 0xFF001123u]);
        Success(WarpPortableHeapServices.WriteValueArrayElement(arena, array.Context, array.Slot, array.Generation, 1, 0));
        Collect(arena);
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, WarpPortableHeapServices.GetType(arena, child.Context, child.Slot, child.Generation));
        Success(WarpPortableHeapServices.ReleaseRoot(arena, root, generation));
    }

    [TestMethod]
    public void ObjectArrayElementsPreserveAliasesAndRejectBoundsAndPartialReferenceWrites()
    {
        uint[] arena = Arena();
        WarpPortableHeapReference child = Allocate(arena, 3);
        Success(WarpPortableHeapServices.AllocateArray(arena, 6, 2));
        WarpPortableHeapReference array = ResultReference(arena);
        StoreReference(arena, array, 0, child);
        StoreReference(arena, array, 3, child);
        Root(arena, array);
        Success(WarpPortableHeapServices.GetLength(arena, array.Context, array.Slot, array.Generation));
        Assert.AreEqual(2u, arena[WarpPortableHeapLayout.Result]);
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, WarpPortableHeapServices.ArrayElement(arena, array.Context, array.Slot, array.Generation, 2));
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, WarpPortableHeapServices.ArrayElement(arena, array.Context, array.Slot, array.Generation, uint.MaxValue));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, WarpPortableHeapServices.WriteReference(arena, array.Context, array.Slot,
            array.Generation, 1, child.Context, child.Slot, child.Generation));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, WarpPortableHeapServices.WriteWord(arena, array.Context, array.Slot, array.Generation, 4, 0));
        Collect(arena);
        Assert.AreEqual(2u, arena[WarpPortableHeapLayout.LiveObjects]);
        StoreReference(arena, array, 0, default);
        Collect(arena);
        Assert.AreEqual(2u, arena[WarpPortableHeapLayout.LiveObjects]);
        StoreReference(arena, array, 3, default);
        Collect(arena);
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
    }

    [TestMethod]
    public void CastsBoxingAndReferenceStoresRejectWrongTypesWithoutPartialPublication()
    {
        uint[] arena = Arena();
        Stage(arena, [0x00000001, 0x80000000]);
        Success(WarpPortableHeapServices.BoxValue(arena, 4, 0));
        WarpPortableHeapReference box = ResultReference(arena);
        Success(WarpPortableHeapServices.IsInstance(arena, box.Context, box.Slot, box.Generation, 1));
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.Result]);
        Success(WarpPortableHeapServices.Cast(arena, box.Context, box.Slot, box.Generation, 2, 0));
        Assert.IsTrue(ResultReference(arena).IsNull);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, WarpPortableHeapServices.Cast(arena, box.Context, box.Slot, box.Generation, 2, 1));
        Stage(arena, [0xABCDEF01, 0xABCDEF02, 0xABCDEF03, 0xABCDEF04, 0xABCDEF05]);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidCast, WarpPortableHeapServices.UnboxValue(arena, box.Context, box.Slot, box.Generation, 5, 0));
        Assert.AreEqual(0xABCDEF01u, Scratch(arena, 1)[0]);
        Assert.AreEqual(WarpPortableHeapLayout.NullReference, WarpPortableHeapServices.UnboxValue(arena, 0, 0, 0, 4, 0));
        WarpPortableHeapReference owner = Allocate(arena, 3);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, WarpPortableHeapServices.WriteWord(arena, owner.Context, owner.Slot, owner.Generation, 1, 0));
        Assert.AreEqual(WarpPortableHeapLayout.WrongContext, WarpPortableHeapServices.WriteReference(arena, owner.Context, owner.Slot, owner.Generation, 0, owner.Context + 1, box.Slot, box.Generation));
        Assert.IsTrue(ReadReference(arena, owner, 0).IsNull);
    }

    [TestMethod]
    public void StringsPreserveUtf16CodeUnitsAndRemainImmutable()
    {
        uint[] arena = Arena();
        uint[] characters = [0, 0x0041, 0xD800, 0xDCFF, 0xFFFF];
        Stage(arena, characters);
        Success(WarpPortableHeapServices.CreateString(arena, 8, 0, (uint)characters.Length));
        WarpPortableHeapReference text = ResultReference(arena);
        for (uint index = 0; index < characters.Length; index++)
        {
            Success(WarpPortableHeapServices.StringCharacter(arena, text.Context, text.Slot, text.Generation, index));
            Assert.AreEqual(characters[index], arena[WarpPortableHeapLayout.Result]);
        }
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, WarpPortableHeapServices.StringCharacter(arena, text.Context, text.Slot, text.Generation, uint.MaxValue));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, WarpPortableHeapServices.WriteWord(arena, text.Context, text.Slot, text.Generation, 0, 0));
        Stage(arena, [0x10000]);
        uint live = arena[WarpPortableHeapLayout.LiveObjects];
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, WarpPortableHeapServices.CreateString(arena, 8, 0, 1));
        Assert.AreEqual(live, arena[WarpPortableHeapLayout.LiveObjects]);
        Success(WarpPortableHeapServices.CreateString(arena, 8, 0, 0));
        WarpPortableHeapReference empty = ResultReference(arena);
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, WarpPortableHeapServices.StringCharacter(arena, empty.Context, empty.Slot, empty.Generation, 0));
    }

    [TestMethod]
    public void CollectionRequiresEveryRegisteredLogicalWorkerToPark()
    {
        uint[] arena = Arena();
        WarpPortableHeapReference owner = Allocate(arena, 3);
        Root(arena, owner);
        Success(WarpPortableHeapServices.EnterWorker(arena, 0));
        Success(WarpPortableHeapServices.EnterWorker(arena, 1));
        Success(WarpPortableHeapServices.RequestCollection(arena));
        Assert.AreEqual(WarpPortableHeapLayout.Busy, WarpPortableHeapServices.Collect(arena));
        Assert.AreEqual(WarpPortableHeapLayout.Busy, WarpPortableHeapServices.AllocateObject(arena, 3));
        Success(WarpPortableHeapServices.ParkWorker(arena, 0));
        Assert.AreEqual(WarpPortableHeapLayout.Busy, WarpPortableHeapServices.Collect(arena));
        Success(WarpPortableHeapServices.ParkWorker(arena, 1));
        Success(WarpPortableHeapServices.Collect(arena));
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
        Success(WarpPortableHeapServices.ResumeWorker(arena, 0));
        Success(WarpPortableHeapServices.ResumeWorker(arena, 1));
        Success(WarpPortableHeapServices.ExitWorker(arena, 0));
        Success(WarpPortableHeapServices.ExitWorker(arena, 1));
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.ActiveWorkers]);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.ParkedWorkers]);
    }

    [TestMethod]
    public void AllocatedResultSurvivesYieldUntilCallerPublishesRootAndReleasesServiceLease()
    {
        uint[] arena = Arena();
        Success(WarpPortableHeapServices.EnterWorker(arena, 0));
        Success(WarpPortableHeapServices.AcquireServiceLease(arena, 0));
        Success(WarpPortableHeapServices.AllocateObject(arena, 3));
        WarpPortableHeapReference unresolved = ResultReference(arena);
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.PendingResult]);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LeaseEpoch]);
        Success(WarpPortableHeapServices.RequestCollection(arena));
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.CollectionEpoch]);
        Assert.AreEqual(unresolved, ResultReference(arena));
        Assert.AreEqual(WarpPortableHeapLayout.Busy, WarpPortableHeapServices.ParkWorker(arena, 0));
        Assert.AreEqual(WarpPortableHeapLayout.Busy, WarpPortableHeapServices.Collect(arena));
        Assert.AreEqual(WarpPortableHeapLayout.Busy, WarpPortableHeapServices.ReleaseServiceLease(arena, 0));
        Assert.AreEqual(unresolved, ResultReference(arena));
        (uint root, uint generation) = Root(arena, unresolved);
        Success(WarpPortableHeapServices.AcknowledgeServiceResult(arena, 0));
        Assert.AreEqual(WarpPortableHeapLayout.Busy, WarpPortableHeapServices.ParkWorker(arena, 0));
        Success(WarpPortableHeapServices.ReleaseServiceLease(arena, 0));
        Success(WarpPortableHeapServices.ParkWorker(arena, 0));
        Success(WarpPortableHeapServices.Collect(arena));
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
        Success(WarpPortableHeapServices.ResumeWorker(arena, 0));
        Success(WarpPortableHeapServices.GetType(arena, unresolved.Context, unresolved.Slot, unresolved.Generation));
        Success(WarpPortableHeapServices.ReleaseRoot(arena, root, generation));
        Success(WarpPortableHeapServices.RequestCollection(arena));
        Success(WarpPortableHeapServices.ParkWorker(arena, 0));
        Success(WarpPortableHeapServices.Collect(arena));
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]);
    }

    [TestMethod]
    public void WrongOwnerCannotPublishPendingResultAndExplicitAbortQuarantinesIt()
    {
        uint[] arena = Arena();
        Success(WarpPortableHeapServices.EnterWorker(arena, 0));
        Success(WarpPortableHeapServices.AcquireServiceLease(arena, 0));
        Success(WarpPortableHeapServices.AllocateObject(arena, 3));
        WarpPortableHeapReference abandoned = ResultReference(arena);
        Success(WarpPortableHeapServices.RequestCollection(arena));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, WarpPortableHeapServices.AcknowledgeServiceResult(arena, 1));
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.PendingResult]);
        Assert.AreEqual(WarpPortableHeapLayout.Busy, WarpPortableHeapServices.AcquireServiceLease(arena, 1));
        Assert.AreEqual(WarpPortableHeapLayout.Busy, WarpPortableHeapServices.Collect(arena));
        Success(WarpPortableHeapServices.AbortServiceLease(arena, 0));
        Assert.IsTrue(ResultReference(arena).IsNull);
        Success(WarpPortableHeapServices.ParkWorker(arena, 0));
        Success(WarpPortableHeapServices.Collect(arena));
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference,
            WarpPortableHeapServices.GetType(arena, abandoned.Context, abandoned.Slot, abandoned.Generation));
    }

    [TestMethod]
    public void StaticRootsAndFailedTypeInitializationRetainExceptionIdentityPerContext()
    {
        uint[] arena = Arena();
        WarpPortableHeapReference value = Allocate(arena, 3);
        Success(WarpPortableHeapServices.BeginTypeInitialization(arena, 3, 0));
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.Result]);
        Success(WarpPortableHeapServices.BeginTypeInitialization(arena, 3, 0));
        Assert.AreEqual(2u, arena[WarpPortableHeapLayout.Result]);
        Assert.AreEqual(WarpPortableHeapLayout.Busy, WarpPortableHeapServices.BeginTypeInitialization(arena, 3, 1));
        Success(WarpPortableHeapServices.WriteStaticReference(arena, 3, 0, value.Context, value.Slot, value.Generation));
        Success(WarpPortableHeapServices.WriteStaticWord(arena, 3, 3, 0xF0123456));
        Success(WarpPortableHeapServices.CompleteTypeInitialization(arena, 3, 0));
        Success(WarpPortableHeapServices.BeginTypeInitialization(arena, 3, 1));
        Assert.AreEqual(3u, arena[WarpPortableHeapLayout.Result]);
        Collect(arena);
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
        Success(WarpPortableHeapServices.BeginTypeInitialization(arena, 1, 1));
        Success(WarpPortableHeapServices.FailTypeInitialization(arena, 1, 1, value.Context, value.Slot, value.Generation));
        Success(WarpPortableHeapServices.WriteStaticReference(arena, 3, 0, 0, 0, 0));
        Collect(arena);
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(WarpPortableHeapLayout.TypeInitializationFailed, WarpPortableHeapServices.BeginTypeInitialization(arena, 1, 0));
        Assert.AreEqual(value, ResultReference(arena));
        uint[] other = Arena(context: 2);
        Success(WarpPortableHeapServices.BeginTypeInitialization(other, 1, 0));
        Assert.AreEqual(1u, other[WarpPortableHeapLayout.Result]);
    }

    [TestMethod]
    public void ExplicitGraphTransferPreservesTypesValuesAliasesAndCyclesWithNewIdentities()
    {
        uint[] source = Arena();
        uint[] destination = Arena(context: 2);
        WarpPortableHeapReference left = Allocate(source, 3);
        WarpPortableHeapReference right = Allocate(source, 3);
        StoreReference(source, left, 0, right);
        StoreReference(source, left, 3, right);
        StoreReference(source, right, 0, left);
        Success(WarpPortableHeapServices.WriteWord(source, left.Context, left.Slot, left.Generation, 6, 0xFACE1234));
        Success(WarpPortableHeapServices.TransferGraph(source, destination, left.Context, left.Slot, left.Generation));
        WarpPortableHeapReference clonedLeft = ResultReference(destination);
        WarpPortableHeapReference clonedRight = ReadReference(destination, clonedLeft, 0);
        Assert.AreNotEqual(left, clonedLeft);
        Assert.AreEqual(clonedRight, ReadReference(destination, clonedLeft, 3));
        Assert.AreEqual(clonedLeft, ReadReference(destination, clonedRight, 0));
        Success(WarpPortableHeapServices.GetType(destination, clonedLeft.Context, clonedLeft.Slot, clonedLeft.Generation));
        Assert.AreEqual(3u, destination[WarpPortableHeapLayout.Result]);
        Success(WarpPortableHeapServices.ReadWord(destination, clonedLeft.Context, clonedLeft.Slot, clonedLeft.Generation, 6));
        Assert.AreEqual(0xFACE1234u, destination[WarpPortableHeapLayout.Result]);
        Assert.AreEqual(WarpPortableHeapLayout.WrongContext, WarpPortableHeapServices.GetType(destination, left.Context, left.Slot, left.Generation));
        Root(destination, clonedLeft);
        Collect(destination);
        Assert.AreEqual(2u, destination[WarpPortableHeapLayout.LiveObjects]);
        Collect(source);
        Assert.AreEqual(0u, source[WarpPortableHeapLayout.LiveObjects]);
        Success(WarpPortableHeapServices.GetType(destination, clonedRight.Context, clonedRight.Slot, clonedRight.Generation));
    }

    [TestMethod]
    public void FailedGraphTransferRollsBackNewObjectsAndQuotasWithoutTouchingExistingObjects()
    {
        uint[] source = Arena();
        uint[] destination = Arena(context: 2, objects: 2);
        WarpPortableHeapReference left = Allocate(source, 3);
        WarpPortableHeapReference right = Allocate(source, 3);
        StoreReference(source, left, 0, right);
        StoreReference(source, right, 0, left);
        WarpPortableHeapReference sentinel = Allocate(destination, 9);
        Success(WarpPortableHeapServices.WriteWord(destination, sentinel.Context, sentinel.Slot, sentinel.Generation, 0, 0x12345678));
        uint before = destination[WarpPortableHeapLayout.UsedWords];
        Assert.AreEqual(WarpPortableHeapLayout.OutOfMemory, WarpPortableHeapServices.TransferGraph(source, destination, left.Context, left.Slot, left.Generation));
        Assert.AreEqual(1u, destination[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(before, destination[WarpPortableHeapLayout.UsedWords]);
        Assert.IsTrue(ResultReference(destination).IsNull);
        Success(WarpPortableHeapServices.ReadWord(destination, sentinel.Context, sentinel.Slot, sentinel.Generation, 0));
        Assert.AreEqual(0x12345678u, destination[WarpPortableHeapLayout.Result]);
        Success(WarpPortableHeapServices.AllocateObject(destination, 9));
        Assert.AreEqual(2u, destination[WarpPortableHeapLayout.LiveObjects]);
    }

    [TestMethod]
    public void PressureCoalescesFreedBlocksAndFaultReportingNeedsNoOrdinaryAllocation()
    {
        uint[] arena = Arena(words: 64, objects: 16, roots: 1);
        WarpPortableHeapReference survivor = Allocate(arena, 3);
        Root(arena, survivor);
        for (int round = 0; round < 64; round++)
        {
            while (WarpPortableHeapServices.AllocateObject(arena, 3) == 0)
            {
            }
            Assert.AreEqual(WarpPortableHeapLayout.OutOfMemory, arena[WarpPortableHeapLayout.Fault]);
            Assert.IsTrue(ResultReference(arena).IsNull);
            Collect(arena);
            Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
            Assert.AreEqual(11u, arena[WarpPortableHeapLayout.UsedWords]);
            Assert.AreEqual(WarpPortableHeapLayout.Quota, WarpPortableHeapServices.AcquireRoot(arena, survivor.Context, survivor.Slot,
                survivor.Generation, WarpPortableHeapLayout.StrongRoot, 0, 0, 0));
        }
        Assert.AreEqual(WarpPortableHeapLayout.OutOfMemory, WarpPortableHeapServices.AllocateArray(arena, 7, uint.MaxValue));
        Assert.AreEqual(64u, arena[WarpPortableHeapLayout.AllocationQuota]);
        Assert.AreEqual(11u, arena[WarpPortableHeapLayout.UsedWords]);
        Assert.AreEqual(WarpPortableHeapLayout.OutOfMemory, arena[WarpPortableHeapLayout.Fault]);
    }

    [TestMethod]
    public void RandomCyclicGraphsMatchIndependentReachabilityAcrossPressureCollections()
    {
        uint[] arena = Arena();
        uint seed = 0x76473489;
        for (int round = 0; round < 128; round++)
        {
            CheckRandomGraph(arena, ref seed);
        }
    }

    [TestMethod]
    public void ImmutableSchemasRejectOverlappingReferencesAndDifferentCanonicalTypeIdentities()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new WarpPortableHeapSchema(
        [
            new(1, "test:Object", WarpPortableHeapLayout.Class, 0, []),
            new(2, "test:Bad", WarpPortableHeapLayout.Class, 5, [1], [new(0, 1), new(2, 1)]),
        ]));
        var sourceSchema = new WarpPortableHeapSchema([new(1, "test:Foo", WarpPortableHeapLayout.Class, 0, [])]);
        var destinationSchema = new WarpPortableHeapSchema([new(1, "test:Bar", WarpPortableHeapLayout.Class, 0, [])]);
        uint[] source = sourceSchema.CreateArena(1, 64, 4, 4, 4, 64);
        uint[] destination = destinationSchema.CreateArena(2, 64, 4, 4, 4, 64);
        WarpPortableHeapReference reference = Allocate(source, 1);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidType,
            WarpPortableHeapServices.TransferGraph(source, destination, reference.Context, reference.Slot, reference.Generation));
        Assert.AreEqual(0u, destination[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(0u, destination[WarpPortableHeapLayout.UsedWords]);
    }

    [TestMethod]
    public void ExhaustedGenerationRetiresObjectAndRootSlotsInsteadOfReusingIdentity()
    {
        uint[] arena = Arena(objects: 1, roots: 1);
        uint slotStart = arena[WarpPortableHeapLayout.SlotStart];
        arena[slotStart + WarpPortableHeapLayout.SlotGeneration] = uint.MaxValue;
        WarpPortableHeapReference reference = Allocate(arena, 3);
        Assert.AreEqual(uint.MaxValue, reference.Generation);
        uint rootStart = arena[WarpPortableHeapLayout.RootStart];
        arena[rootStart + WarpPortableHeapLayout.RootGeneration] = uint.MaxValue;
        (uint root, uint generation) = Root(arena, reference);
        Success(WarpPortableHeapServices.ReleaseRoot(arena, root, generation));
        Assert.AreEqual(WarpPortableHeapLayout.Quota, WarpPortableHeapServices.AcquireRoot(arena, reference.Context, reference.Slot,
            reference.Generation, WarpPortableHeapLayout.StrongRoot, 0, 0, 0));
        Collect(arena);
        Assert.AreEqual(WarpPortableHeapLayout.Retired, arena[slotStart + WarpPortableHeapLayout.SlotState]);
        Assert.AreEqual(WarpPortableHeapLayout.Retired, arena[rootStart + WarpPortableHeapLayout.RootState]);
        Assert.AreEqual(WarpPortableHeapLayout.OutOfMemory, WarpPortableHeapServices.AllocateObject(arena, 3));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, WarpPortableHeapServices.GetType(arena, reference.Context, reference.Slot, reference.Generation));
    }

    [TestMethod]
    public void ContextOwnedHeapSurvivesDispatchesAndHandlesProvideExplicitRootLifetime()
    {
        using var context = new WarpPortableHeapContext(Schema(), payloadWords: 256, quotaWords: 256);
        WarpPortableHeapServiceResult allocated = context.Execute(nameof(WarpPortableHeapServices.AllocateObject), 3);
        Assert.AreEqual(0u, allocated.Fault);
        WarpPortableHeapReference reference = allocated.Reference;
        using WarpPortableHeapRootHandle root = context.AcquireRoot(reference);
        Assert.AreEqual(reference, root.Reference);
        Assert.IsGreaterThan(0, context.CompiledServiceCount);
        Assert.AreEqual(0u, context.Execute(nameof(WarpPortableHeapServices.EnterWorker), 0).Fault);
        Assert.AreEqual(0u, context.Execute(nameof(WarpPortableHeapServices.WriteWord), reference.Context, reference.Slot, reference.Generation, 6, 0x87654321).Fault);
        Assert.AreEqual(0u, context.Execute(nameof(WarpPortableHeapServices.ExitWorker), 0).Fault);
        Assert.AreEqual(0u, context.Execute(nameof(WarpPortableHeapServices.EnterWorker), 0).Fault);
        WarpPortableHeapServiceResult read = context.Execute(nameof(WarpPortableHeapServices.ReadWord), reference.Context, reference.Slot, reference.Generation, 6);
        Assert.AreEqual(0u, read.Fault);
        Assert.AreEqual(0x87654321u, read.Word0);
        Assert.AreEqual(0u, context.Execute(nameof(WarpPortableHeapServices.ExitWorker), 0).Fault);
        using var other = new WarpPortableHeapContext(Schema(), payloadWords: 256, quotaWords: 256);
        Assert.AreEqual(WarpPortableHeapLayout.WrongContext, other.Execute(nameof(WarpPortableHeapServices.GetType), reference.Context, reference.Slot, reference.Generation).Fault);
        root.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = root.Reference);
        Assert.AreEqual(0u, context.Execute(nameof(WarpPortableHeapServices.RequestCollection)).Fault);
        Assert.AreEqual(1u, context.Execute(nameof(WarpPortableHeapServices.Collect)).Word0);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, context.Execute(nameof(WarpPortableHeapServices.GetType), reference.Context, reference.Slot, reference.Generation).Fault);
        context.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => context.Execute(nameof(WarpPortableHeapServices.RequestCollection)));
    }

    [TestMethod]
    public void RuntimeServiceClosureUsesOnlyPortableWordLocalsAndTrustedWordBankParameters()
    {
        foreach (MethodInfo method in typeof(WarpPortableHeapServices).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            Assert.AreEqual(typeof(uint), method.ReturnType, method.Name);
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                Assert.IsTrue(parameter.ParameterType == typeof(uint) || parameter.ParameterType == typeof(uint[]), method.Name);
            }
            MethodBody body = method.GetMethodBody() ?? throw new InvalidOperationException();
            Assert.HasCount(0, body.ExceptionHandlingClauses, method.Name);
            foreach (LocalVariableInfo local in body.LocalVariables)
            {
                Assert.IsTrue(local.LocalType == typeof(uint) || local.LocalType == typeof(bool), method.Name);
            }
        }
    }

    private static WarpPortableHeapSchema Schema() => new(
    [
        new(1, "test:Object", WarpPortableHeapLayout.Class, 0, []),
        new(2, "test:INode", WarpPortableHeapLayout.Interface, 0, [1]),
        new(3, "test:Node", WarpPortableHeapLayout.Class, 7, [2], [new(0, 1), new(3, 1)], staticWords: 4, staticReferences: [new(0, 1)]),
        new(4, "test:Number64", WarpPortableHeapLayout.Value, 2, [1]),
        new(5, "test:Pair", WarpPortableHeapLayout.Value, 5, [1], [new(0, 1)]),
        new(6, "test:Object[]", WarpPortableHeapLayout.ReferenceArray, 0, [1], elementType: 1),
        new(7, "test:Pair[]", WarpPortableHeapLayout.ValueArray, 0, [1], elementType: 5),
        new(8, "test:String", WarpPortableHeapLayout.String, 0, [1]),
        new(9, "test:Other", WarpPortableHeapLayout.Class, 4, [1]),
        new(10, "test:Word32", WarpPortableHeapLayout.Value, 1, [1]),
        new(11, "test:Composite", WarpPortableHeapLayout.Class, 7, [1], [new(0, 1)]),
    ]);

    private static uint[] Arena(uint context = 1, uint words = 1024, uint objects = 64, uint roots = 16) =>
        Schema().CreateArena(context, words, objects, roots, maximumWorkers: 8, quotaWords: words);

    private static WarpPortableHeapReference Allocate(uint[] arena, uint type)
    {
        Success(WarpPortableHeapServices.AllocateObject(arena, type));
        return ResultReference(arena);
    }

    private static WarpPortableHeapReference ResultReference(uint[] arena) =>
        new(arena[WarpPortableHeapLayout.Result], arena[WarpPortableHeapLayout.Result + 1], arena[WarpPortableHeapLayout.Result + 2]);

    private static void StoreReference(uint[] arena, WarpPortableHeapReference owner, uint offset, WarpPortableHeapReference value) =>
        Success(WarpPortableHeapServices.WriteReference(arena, owner.Context, owner.Slot, owner.Generation, offset, value.Context, value.Slot, value.Generation));

    private static WarpPortableHeapReference ReadReference(uint[] arena, WarpPortableHeapReference owner, uint offset)
    {
        Success(WarpPortableHeapServices.ReadReference(arena, owner.Context, owner.Slot, owner.Generation, offset));
        return ResultReference(arena);
    }

    private static (uint Root, uint Generation) Root(uint[] arena, WarpPortableHeapReference reference)
    {
        Success(WarpPortableHeapServices.AcquireRoot(arena, reference.Context, reference.Slot, reference.Generation,
            WarpPortableHeapLayout.StrongRoot, 0, 0, 0));
        return (arena[WarpPortableHeapLayout.Result], arena[WarpPortableHeapLayout.Result + 1]);
    }

    private static void Collect(uint[] arena)
    {
        Success(WarpPortableHeapServices.RequestCollection(arena));
        Success(WarpPortableHeapServices.Collect(arena));
    }

    private static void Stage(uint[] arena, uint[] values) => values.CopyTo(arena, (int)arena[WarpPortableHeapLayout.ScratchStart]);

    private static uint[] Scratch(uint[] arena, int count) => arena.AsSpan((int)arena[WarpPortableHeapLayout.ScratchStart], count).ToArray();

    private static void Success(uint fault) => Assert.AreEqual(WarpPortableHeapLayout.Success, fault);

    private static void CheckRandomGraph(uint[] arena, ref uint seed)
    {
        const int count = 24;
        var references = new WarpPortableHeapReference[count];
        var edges = new int[count * 2];
        foreach (ref WarpPortableHeapReference reference in references.AsSpan())
        {
            reference = Allocate(arena, 3);
        }
        int edgeIndex = 0;
        foreach (ref readonly WarpPortableHeapReference reference in references.AsSpan())
        {
            for (int edge = 0; edge < 2; edge++)
            {
                int target = (int)(Next(ref seed) % (count + 1)) - 1;
                edges[edgeIndex++] = target;
                StoreReference(arena, reference, (uint)edge * 3, target < 0 ? default : references[target]);
            }
        }
        var starting = new HashSet<int>();
        for (int index = 0; index < 3; index++)
        {
            starting.Add((int)(Next(ref seed) % count));
        }
        var leases = new List<(uint Root, uint Generation)>();
        foreach (int index in starting)
        {
            leases.Add(Root(arena, references[index]));
        }
        HashSet<int> reachable = Reachable(edges, starting);
        Collect(arena);
        Assert.AreEqual((uint)reachable.Count, arena[WarpPortableHeapLayout.LiveObjects]);
        for (int index = 0; index < count; index++)
        {
            WarpPortableHeapReference reference = references[index];
            Assert.AreEqual(reachable.Contains(index) ? WarpPortableHeapLayout.Success : WarpPortableHeapLayout.InvalidReference,
                WarpPortableHeapServices.GetType(arena, reference.Context, reference.Slot, reference.Generation));
        }
        foreach ((uint root, uint generation) in leases)
        {
            Success(WarpPortableHeapServices.ReleaseRoot(arena, root, generation));
        }
        Collect(arena);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.UsedWords]);
    }

    private static HashSet<int> Reachable(int[] edges, IEnumerable<int> starting)
    {
        var result = new HashSet<int>(starting);
        var queue = new Queue<int>(result);
        while (queue.TryDequeue(out int index))
        {
            for (int edge = 0; edge < 2; edge++)
            {
                int target = edges[index * 2 + edge];
                if (target >= 0 && result.Add(target))
                {
                    queue.Enqueue(target);
                }
            }
        }
        return result;
    }

    private static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
}
