using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests.Production;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest constructs this internal fixture through reflected discovery under DiscoverInternals.")]
internal sealed partial class ZNativeBankRetentionTests
{
    [TestMethod]
    [DataRow(0, false, false)]
    [DataRow(0, true, false)]
    [DataRow(1, false, false)]
    [DataRow(1, true, false)]
    [DataRow(2, false, false)]
    [DataRow(2, true, false)]
    [DataRow(0, false, true)]
    [DataRow(0, true, true)]
    [DataRow(1, false, true)]
    [DataRow(1, true, true)]
    [DataRow(2, false, true)]
    [DataRow(2, true, true)]
    public void OriginalInputStateAndKnownScalarDenialPrecedesContinuationValidationAndAllocation(
        int role, bool quarantine, bool direct)
    {
        WarpLogicalMachineLayout layout = Layout();
        uint[] state = layout.CreateInitialState(4, 100), input = [17], scalars = [0x80000000];
        state[WarpLogicalMachineLayout.StatusOffset] = uint.MaxValue;
        uint[] bank = role switch { 0 => input, 1 => state, 2 => scalars, _ => throw new ArgumentOutOfRangeException(nameof(role)) };
        uint[] before = (uint[])bank.Clone();
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        if (quarantine) { WarpOrdinaryArrayRegistry.Quarantine(handle, owner, authority); }
        using var transport = new Transport();
        try
        {
            if (direct)
            {
                Assert.ThrowsExactly<InvalidOperationException>(() => WarpNativeMachineDispatch.Resume(Image(layout), state,
                    [input], scalars, 1, 0, 4, layout.MaximumBlockCost, transport.Operations, CancellationToken.None));
            }
            else
            {
                Assert.ThrowsExactly<InvalidOperationException>(() => new WarpNativeMachineExecution(Image(layout), state,
                    [input], scalars, 1, 0, 4, transport.Operations));
            }
            Assert.AreEqual(0, transport.AllocationAttempts);
            Assert.AreEqual(0, transport.Uploads);
            Assert.AreEqual(0, transport.Launches);
            CollectionAssert.AreEqual(before, bank);
        }
        finally { if (!quarantine) { WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority); } }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ResetOriginalDenialPrecedesCapacityValidationAndUpload(bool quarantine)
    {
        WarpLogicalMachineLayout layout = Layout();
        using var transport = new Transport();
        using var execution = new WarpNativeMachineExecution(Image(layout), layout.CreateInitialState(4, 100),
            [[17]], [1], 1, 0, 4, transport.Operations);
        uint[] next = layout.CreateInitialState(4, 100);
        next[WarpLogicalMachineLayout.StatusOffset] = uint.MaxValue;
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [next]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        if (quarantine) { WarpOrdinaryArrayRegistry.Quarantine(handle, owner, authority); }
        int uploads = transport.Uploads, allocations = transport.AllocationAttempts;
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => execution.ResetBatch(next, 2, 0));
            Assert.AreEqual(uploads, transport.Uploads);
            Assert.AreEqual(allocations, transport.AllocationAttempts);
            Assert.IsFalse(transport.Quarantined);
        }
        finally { if (!quarantine) { WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority); } }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReductionSingletonDenialPrecedesOperationValidationAndWordRead(bool quarantine)
    {
        uint[] singleton = [0x7FA12345];
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [singleton]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        if (quarantine) { WarpOrdinaryArrayRegistry.Quarantine(handle, owner, authority); }
        using var transport = new Transport();
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => WarpNativeReductionDispatch.Reduce(Image(Layout()),
                singleton, (WarpReductionOperation)(-1), transport.Operations, CancellationToken.None));
            Assert.AreEqual(0, transport.AllocationAttempts);
            Assert.AreEqual(0, transport.Readbacks);
            Assert.AreEqual(0x7FA12345u, singleton[0]);
        }
        finally { if (!quarantine) { WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority); } }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExactInputCaptureSurvivesOuterReplacementAndRetainsAllRolesThroughEveryFree(bool direct)
    {
        WarpLogicalMachineLayout layout = Layout();
        uint[] state = layout.CreateInitialState(4, 100), input = [17], scalars = [0x80000000], replacement = [999];
        uint[][] outer = [input];
        object owner = new(), authority = new(), otherOwner = new(), otherAuthority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [state, input, scalars]);
        WarpOrdinaryArrayOwner other = WarpOrdinaryArrayRegistry.EnrollOwner(otherOwner, otherAuthority, [replacement]);
        WarpOrdinaryArrayRegistry.Hold(other, otherOwner, otherAuthority);
        using var transport = new Transport();
        transport.BeforeAllocation = attempt => { if (attempt == 1) { outer[0] = replacement; } };
        transport.BeforeFree = _ => Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Hold(handle, owner, authority));
        transport.OnLaunch = CompleteMachine;
        try
        {
            uint[] result;
            if (direct)
            {
                result = WarpNativeMachineDispatch.Resume(Image(layout), state, outer, scalars, 1, 0, 4,
                    layout.MaximumBlockCost, transport.Operations, CancellationToken.None);
            }
            else
            {
                using var execution = new WarpNativeMachineExecution(Image(layout), state, outer, scalars, 1, 0, 4, transport.Operations);
                result = execution.Resume(layout.MaximumBlockCost);
            }
            Assert.AreSame(replacement, outer[0]);
            Assert.AreEqual(0x80000011u, result[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(2, transport.FreeAttempts);
            Assert.IsEmpty(transport.Blocks);
            WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
            WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
        }
        finally { WarpOrdinaryArrayRegistry.ReleaseHold(other, otherOwner, otherAuthority); }
    }

    [TestMethod]
    public void CaptureReadsTheCallerOuterReferenceExactlyOnceAndDoesNotAcquireOpaqueScalarPermission()
    {
        uint[] original = [0x89ABCDEF], replacement = [1];
        var inputs = new SingleCaptureInputs(original, replacement);
        var opaque = new GetterScalars([0xFFC01234]);
        using WarpNativeBankRetention use = WarpNativeBankRetention.Acquire(inputs, opaque, []);
        Assert.AreEqual(1, inputs.CountReads);
        Assert.AreEqual(1, inputs.ReferenceReads);
        Assert.AreSame(original, use.Inputs[0]);
        Assert.AreSame(original, use.Inputs[0]);
        Assert.AreSame(opaque, use.Scalars);
        Assert.AreEqual(0, opaque.ValueReads);
        use.RequirePublishable();
        Assert.AreEqual(1, inputs.CountReads);
        Assert.AreEqual(1, inputs.ReferenceReads);
    }

    [TestMethod]
    public void OpaqueScalarGetterBehaviorAndRawBitsArePreservedWithoutClaimingBackingProvenance()
    {
        WarpLogicalMachineLayout layout = Layout();
        var opaque = new GetterScalars([0x80000000]);
        using var transport = new Transport { OnLaunch = CompleteMachine };
        using var execution = new WarpNativeMachineExecution(Image(layout), layout.CreateInitialState(4, 100),
            [[17]], opaque, 1, 0, 4, transport.Operations);
        Assert.AreEqual(1, opaque.ValueReads);
        uint[] result = execution.Resume(layout.MaximumBlockCost);
        Assert.AreEqual(0x80000011u, result[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(1, opaque.ValueReads);
    }

    [TestMethod]
    public void EveryResetOriginalRemainsAdmittedUntilTheSharedPhysicalStateBufferIsFreed()
    {
        WarpLogicalMachineLayout layout = Layout();
        uint[] first = layout.CreateInitialState(4, 100), second = layout.CreateInitialState(4, 100), third = layout.CreateInitialState(4, 100);
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [first, second, third]);
        using var transport = new Transport();
        var execution = new WarpNativeMachineExecution(Image(layout), first, [[17]], [1], 1, 0, 4, transport.Operations);
        try
        {
            execution.ResetBatch(second, 1, 0);
            execution.ResetBatch(third, 1, 0);
            Assert.AreEqual(2, transport.AllocationAttempts);
            Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Hold(handle, owner, authority));
            transport.BeforeFree = _ => Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Hold(handle, owner, authority));
        }
        finally { execution.Dispose(); }
        Assert.AreEqual(2, transport.FreeAttempts);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(0, true)]
    [DataRow(1, false)]
    [DataRow(1, true)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    public void EveryKnownFreeIsAttemptedAndTheExactFirstFailureRetainsOriginalBanks(int path, bool freesBeforeThrowing)
    {
        WarpLogicalMachineLayout layout = Layout(inputs: 2);
        uint[] state = layout.CreateInitialState(4, 100), first = [17], second = [19], scalars = [1];
        object owner = new(), authority = new();
        uint[][] originalBanks = path == 2 ? [first, second] : [state, first, second, scalars];
        uint[] values = [17, 19];
        if (path == 2) { originalBanks = [values]; }
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, originalBanks);
        var initial = new WarpHostException("WRPNATIVE1007", "First physical free failure.");
        var later = new InvalidOperationException("Independent later free failure.");
        using var transport = new Transport { FreesBeforeThrowing = freesBeforeThrowing,
            OnLaunch = path == 2 ? SumReduction : arguments => CompleteMachine(arguments, inputs: 2) };
        transport.FreeFailures.Add(1, initial);
        transport.FreeFailures.Add(path == 2 ? 2 : 3, later);
        if (path == 0)
        {
            var execution = new WarpNativeMachineExecution(Image(layout), state, [first, second], scalars, 1, 0, 4, transport.Operations);
            Assert.AreSame(initial, Assert.ThrowsExactly<WarpHostException>(execution.Dispose));
            execution.Dispose();
        }
        else if (path == 1)
        {
            Assert.AreSame(initial, Assert.ThrowsExactly<WarpHostException>(() => WarpNativeMachineDispatch.Resume(Image(layout),
                state, [first, second], scalars, 1, 0, 4, layout.MaximumBlockCost, transport.Operations, CancellationToken.None)));
        }
        else
        {
            Assert.AreSame(initial, Assert.ThrowsExactly<WarpHostException>(() => WarpNativeReductionDispatch.Reduce(Image(layout),
                values, WarpReductionOperation.WrappingSum, transport.Operations, CancellationToken.None)));
        }
        Assert.AreEqual(path == 2 ? 2 : 3, transport.FreeAttempts);
        Assert.IsTrue(transport.Quarantined);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.Hold(handle, owner, authority));
        foreach (uint[] bank in originalBanks)
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(new object(), new object(), [bank]));
        }
    }

    [TestMethod]
    public void AllocationAndCleanupCollectorRetainsEveryDistinctExceptionObjectWithoutDisposeReleasingUnknownUse()
    {
        uint[] bank = [17];
        using var transport = new Transport();
        using var use = WarpNativeBankRetention.Acquire([bank], [], []);
        _ = use.Allocate(sizeof(uint), transport.Operations.Allocate);
        _ = use.Allocate(sizeof(uint), transport.Operations.Allocate);
        var original = new InvalidOperationException("Exact original operation failure.");
        var firstFree = new WarpHostException("WRPNATIVE1007", "First free.");
        var secondFree = new InvalidOperationException("Second free.");
        use.RecordFailure(original);
        use.RecordFailure(original);
        transport.FreeFailures.Add(1, firstFree);
        transport.FreeFailures.Add(2, secondFree);
        use.Retire(transport.Operations.Free, transport.Operations.Quarantine);
        Assert.AreSame(original, Assert.ThrowsExactly<InvalidOperationException>(use.ThrowFirstFailure));
        Assert.HasCount(3, use.Failures);
        Assert.AreSame(original, use.Failures[0]);
        Assert.AreSame(firstFree, use.Failures[1]);
        Assert.AreSame(secondFree, use.Failures[2]);
        use.Dispose();
        use.Retire(transport.Operations.Free, transport.Operations.Quarantine);
        Assert.AreEqual(2, transport.FreeAttempts);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(new object(), new object(), [bank]));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UploadFailurePreservesItsObjectAndDrainsAllKnownBuffersEvenWhenFreeFails(bool freeFails)
    {
        WarpLogicalMachineLayout layout = Layout();
        uint[] state = layout.CreateInitialState(4, 100), input = [17], scalars = [1];
        var original = new InvalidOperationException("Exact upload failure.");
        using var transport = new Transport { UploadFailure = original, UploadFailureAt = 2 };
        if (freeFails)
        {
            transport.FreeFailures.Add(1, new WarpHostException("WRPNATIVE1007", "State free failure."));
            transport.FreeFailures.Add(2, new InvalidOperationException("Input free failure."));
        }
        Assert.AreSame(original, Assert.ThrowsExactly<InvalidOperationException>(() => new WarpNativeMachineExecution(Image(layout),
            state, [input], scalars, 1, 0, 4, transport.Operations)));
        Assert.AreEqual(2, transport.FreeAttempts);
        if (freeFails)
        {
            Assert.IsTrue(transport.Quarantined);
            Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(new object(), new object(), [state, input, scalars]));
        }
        else
        {
            object owner = new(), authority = new();
            WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [state, input, scalars]);
            WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
            WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
            Assert.IsEmpty(transport.Blocks);
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void QuarantineDuringReadbackOrFinalFreeDeniesEveryOutput(bool reduction, bool duringFree)
    {
        WarpLogicalMachineLayout layout = Layout();
        uint[] input = [17], values = [17, 19];
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, reduction ? [values] : [input]);
        using var transport = new Transport { OnLaunch = reduction ? SumReduction : CompleteMachine };
        if (duringFree) { transport.BeforeFree = attempt => { if (attempt == 2) { WarpOrdinaryArrayRegistry.Quarantine(handle, owner, authority); } }; }
        else { transport.AfterReadback = () => WarpOrdinaryArrayRegistry.Quarantine(handle, owner, authority); }
        uint[]? machineResult = null;
        uint? reductionResult = null;
        if (reduction)
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => reductionResult = WarpNativeReductionDispatch.Reduce(Image(layout),
                values, WarpReductionOperation.WrappingSum, transport.Operations, CancellationToken.None));
        }
        else
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => machineResult = WarpNativeMachineDispatch.Resume(Image(layout),
                layout.CreateInitialState(4, 100), [input], [1], 1, 0, 4, layout.MaximumBlockCost, transport.Operations, CancellationToken.None));
        }
        Assert.IsNull(machineResult);
        Assert.IsNull(reductionResult);
        Assert.AreEqual(2, transport.FreeAttempts);
        Assert.IsEmpty(transport.Blocks);
        Assert.IsTrue(transport.Quarantined);
    }

    [TestMethod]
    public void UnknownAllocatorOutcomeKeepsOriginalBankAliveAfterFailedConstructorCollection()
    {
        WeakReference<uint[]> original = FailUnknownAllocation();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.IsTrue(original.TryGetTarget(out uint[]? bank));
        Assert.IsNotNull(bank);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpOrdinaryArrayRegistry.EnrollOwner(new object(), new object(), [bank]));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<uint[]> FailUnknownAllocation()
    {
        WarpLogicalMachineLayout layout = Layout();
        uint[] bank = [31];
        var original = new InvalidOperationException("Allocator supplied no positive retirement receipt.");
        using var transport = new Transport { AllocationFailure = original };
        var reference = new WeakReference<uint[]>(bank);
        Assert.AreSame(original, Assert.ThrowsExactly<InvalidOperationException>(() => new WarpNativeMachineExecution(Image(layout),
            layout.CreateInitialState(4, 100), [bank], [1], 1, 0, 4, transport.Operations)));
        Assert.AreEqual(1, transport.AllocationAttempts);
        Assert.AreEqual(0, transport.FreeAttempts);
        Assert.IsTrue(transport.Quarantined);
        return reference;
    }
}
