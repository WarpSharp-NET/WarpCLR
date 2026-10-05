using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    [DataRow("Divide", true)]
    [DataRow("Divide", false)]
    [DataRow("Overflow", true)]
    [DataRow("Overflow", false)]
    [DataRow("Round", true)]
    [DataRow("Round", false)]
    public void GeneratedOperationSpecificRaisePrecedesPrecisePoolRootTransfer(string source, bool smallQuantum)
    {
        var fixture = new WarpPortableFaultTicketDriver(source, smallQuantum);
        uint[] arena = fixture.Arena; uint prepared = fixture.Prepared;
        uint root = fixture.Root(arena[prepared + WarpPortableSourceFaultFactoryLayout.PreparedRoot]);
        uint[] owner = arena.AsSpan((int)prepared + 2, 3).ToArray();
        uint rootGeneration = arena[root + WarpPortableHeapLayout.RootGeneration];
        Assert.AreEqual(WarpPortableExceptionLayout.InvalidPhase, fixture.Command(1));
        fixture.FillGrantedFixture();
        Assert.AreEqual(0u, fixture.Command(1));
        Assert.AreEqual(WarpPortableSourceFaultFactoryLayout.Taken, arena[prepared]);
        Assert.AreEqual(WarpPortableExceptionLayout.InvalidPhase, fixture.Command(3, 1));
        CollectionAssert.AreEqual(owner, arena.AsSpan((int)root + (int)WarpPortableHeapLayout.RootReference, 3).ToArray());
        Assert.AreEqual(0u, fixture.Command(2));
        uint raised = arena[prepared + WarpPortableSourceFaultFactoryLayout.PreparedRaiseGeneration];
        Assert.AreNotEqual(0u, raised);
        uint record = fixture.ExceptionDescriptor + arena[fixture.ExceptionDescriptor + WarpPortableExceptionLayout.RecordStart];
        Assert.AreEqual((uint)fixture.Program.Row.Factory.SourceOffset, arena[record + WarpPortableExceptionLayout.OriginalOffset]);
        Assert.AreEqual((uint)unchecked((ushort)fixture.Program.Row.Factory.OpCode), arena[record + WarpPortableExceptionLayout.OriginalOpCode]);
        Assert.AreNotEqual(0x7Au, arena[record + WarpPortableExceptionLayout.OriginalOpCode]);
        Assert.AreEqual((uint)fixture.Program.Row.Factory.EffectIndex, arena[record + WarpPortableExceptionLayout.OriginalEffect]);
        Assert.AreEqual(fixture.Program.Row.Factory.ExceptionType, arena[record + WarpPortableExceptionLayout.ExceptionExactType]);
        CollectionAssert.AreEqual(owner, arena.AsSpan((int)record + (int)WarpPortableExceptionLayout.ExceptionReference, 3).ToArray());
        uint ehRoot = fixture.Root(arena[record + WarpPortableExceptionLayout.RecordRoot]);
        CollectionAssert.AreEqual(owner, arena.AsSpan((int)ehRoot + (int)WarpPortableHeapLayout.RootReference, 3).ToArray());
        uint traceRoot = fixture.Root(arena[record + WarpPortableExceptionLayout.RecordRoot] + 1);
        uint[] trace = arena.AsSpan((int)record + (int)WarpPortableExceptionLayout.TraceReference, 3).ToArray();
        Assert.AreNotEqual(0u, trace[0] | trace[1] | trace[2]);
        CollectionAssert.AreEqual(trace, arena.AsSpan((int)traceRoot + (int)WarpPortableHeapLayout.RootReference, 3).ToArray());
        Assert.AreEqual(WarpPortableExceptionLayout.InvalidTicket, fixture.Command(3, raised + 1));
        uint liveRoots = arena[WarpPortableHeapLayout.LiveRoots];
        Assert.AreEqual(0u, fixture.Command(3, raised));
        Assert.AreEqual(liveRoots - 1, arena[WarpPortableHeapLayout.LiveRoots]);
        Assert.AreEqual(WarpPortableSourceFaultFactoryLayout.Transferred, arena[prepared]);
        Assert.AreEqual(rootGeneration + 1, arena[root + WarpPortableHeapLayout.RootGeneration]);
        Assert.AreEqual(WarpPortableHeapLayout.Retired, arena[root + WarpPortableHeapLayout.RootState]);
        Assert.IsTrue(arena.AsSpan((int)root + (int)WarpPortableHeapLayout.RootReference, 3).ToArray().All(word => word == 0));
        CollectionAssert.AreEqual(owner, arena.AsSpan((int)ehRoot + (int)WarpPortableHeapLayout.RootReference, 3).ToArray());
        Assert.AreEqual(WarpPortableExceptionLayout.InvalidPhase, fixture.Command(3, raised));
        Assert.AreEqual(WarpPortableExceptionLayout.InvalidPhase, fixture.Command(1));
    }
}
