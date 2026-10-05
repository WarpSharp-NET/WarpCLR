using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GeneratedFaultTakeRequiresTheCapturedPhysicalActivationAndExactFactoryData(bool smallQuantum)
    {
        var fixture = new WarpPortableFaultTicketDriver("Round", smallQuantum);
        fixture.FillGrantedFixture();
        uint[] arena = fixture.Arena;
        uint captured = fixture.ExceptionDescriptor + arena[fixture.ExceptionDescriptor + WarpPortableExceptionLayout.FrameStart];
        uint slot = arena[WarpPortableHeapLayout.SlotStart] +
            (arena[fixture.Prepared + WarpPortableSourceFaultFactoryLayout.PreparedReference + 1] - 1) * WarpPortableHeapLayout.SlotWords;
        uint payload = arena[slot + WarpPortableHeapLayout.SlotPayload];
        uint[] addresses = [captured + WarpPortableExceptionLayout.FramePhysical, captured + WarpPortableExceptionLayout.FrameActivation,
            captured + WarpPortableExceptionLayout.FramePrivateWords, captured + WarpPortableExceptionLayout.FrameSite,
            payload + WarpPortableSourceExceptionLayout.HResultWord, payload + WarpPortableSourceExceptionLayout.ParamNameWord];
        uint[] before = arena.AsSpan((int)fixture.Prepared, (int)WarpPortableSourceFaultFactoryLayout.PreparedWords).ToArray();
        foreach (uint address in addresses)
        {
            arena[address] ^= 1;
            Assert.AreNotEqual(0u, fixture.Command(1), $"Changed captured/data word {address}.");
            arena[address] ^= 1;
            CollectionAssert.AreEqual(before, arena.AsSpan((int)fixture.Prepared, before.Length).ToArray());
        }
        Assert.AreEqual(0u, fixture.Command(1));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GeneratedFaultAcknowledgementCannotWrapThePoolRootGeneration(bool smallQuantum)
    {
        var fixture = new WarpPortableFaultTicketDriver("Divide", smallQuantum);
        fixture.FillGrantedFixture();
        Assert.AreEqual(0u, fixture.Command(1));
        Assert.AreEqual(0u, fixture.Command(2));
        uint[] arena = fixture.Arena;
        uint raise = arena[fixture.Prepared + WarpPortableSourceFaultFactoryLayout.PreparedRaiseGeneration];
        uint root = fixture.Root(arena[fixture.Prepared + WarpPortableSourceFaultFactoryLayout.PreparedRoot]);
        arena[root + WarpPortableHeapLayout.RootGeneration] = uint.MaxValue;
        arena[fixture.Prepared + WarpPortableSourceFaultFactoryLayout.PreparedRootGeneration] = uint.MaxValue;
        uint[] before = arena.AsSpan((int)root, (int)WarpPortableHeapLayout.RootWords).ToArray();
        uint liveRoots = arena[WarpPortableHeapLayout.LiveRoots];
        Assert.AreEqual(WarpPortableExceptionLayout.GenerationExhausted, fixture.Command(3, raise));
        CollectionAssert.AreEqual(before, arena.AsSpan((int)root, before.Length).ToArray());
        Assert.AreEqual(liveRoots, arena[WarpPortableHeapLayout.LiveRoots]);
        Assert.AreEqual(WarpPortableSourceFaultFactoryLayout.Taken, arena[fixture.Prepared]);
    }
}
