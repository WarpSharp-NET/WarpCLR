using WarpCLR.Compiler;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GeneratedFaultTakeRejectsEveryChangedOperationArgumentAndAllHashWords(bool smallQuantum)
    {
        var fixture = new WarpPortableFaultTicketDriver("Divide", smallQuantum);
        fixture.FillGrantedFixture();
        uint[] expected = fixture.Arena.AsSpan((int)fixture.Prepared, (int)WarpPortableSourceFaultFactoryLayout.PreparedWords).ToArray();
        for (int index = 0; index < fixture.Parameters.Length; index++)
        {
            fixture.Parameters[index] ^= 1;
            Assert.AreNotEqual(0u, fixture.Command(1), $"Changed operation argument {index}.");
            fixture.Parameters[index] ^= 1;
            CollectionAssert.AreEqual(expected, fixture.Arena.AsSpan((int)fixture.Prepared, expected.Length).ToArray());
        }
        foreach (uint first in new[] { 26u, 32u, 40u, 48u, 56u })
        {
            uint count = first == 26 ? 4u : 8u;
            for (uint word = 0; word < count; word++)
            {
                fixture.Arena[fixture.Prepared + first + word] ^= 1;
                Assert.AreNotEqual(0u, fixture.Command(1), $"Changed binding word {first + word}.");
                fixture.Arena[fixture.Prepared + first + word] ^= 1;
                CollectionAssert.AreEqual(expected, fixture.Arena.AsSpan((int)fixture.Prepared, expected.Length).ToArray());
            }
        }
        Assert.AreEqual(0u, fixture.Command(1));
        Assert.AreEqual(WarpPortableSourceFaultFactoryLayout.Taken, fixture.Arena[fixture.Prepared]);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GeneratedFaultAcknowledgementRequiresTheActualEhExceptionAndTraceRoots(bool smallQuantum)
    {
        var fixture = new WarpPortableFaultTicketDriver("Divide", smallQuantum);
        fixture.FillGrantedFixture();
        Assert.AreEqual(0u, fixture.Command(1));
        Assert.AreEqual(0u, fixture.Command(2));
        uint[] arena = fixture.Arena;
        uint raise = arena[fixture.Prepared + WarpPortableSourceFaultFactoryLayout.PreparedRaiseGeneration];
        uint record = fixture.ExceptionDescriptor + arena[fixture.ExceptionDescriptor + WarpPortableExceptionLayout.RecordStart];
        uint poolRoot = fixture.Root(arena[fixture.Prepared + WarpPortableSourceFaultFactoryLayout.PreparedRoot]);
        uint ehRootId = arena[record + WarpPortableExceptionLayout.RecordRoot];
        uint ehRoot = fixture.Root(ehRootId), traceRoot = fixture.Root(ehRootId + 1);
        uint[] protectedRoot = arena.AsSpan((int)poolRoot, (int)WarpPortableHeapLayout.RootWords).ToArray();
        uint[] addresses = [ehRoot + WarpPortableHeapLayout.RootReference, traceRoot + WarpPortableHeapLayout.RootReference,
            ehRoot + WarpPortableHeapLayout.RootOwnership, traceRoot + WarpPortableHeapLayout.RootGeneration,
            record + WarpPortableExceptionLayout.RaiseGeneration, record + WarpPortableExceptionLayout.OriginalOffset];
        foreach (uint address in addresses)
        {
            arena[address] ^= 1;
            Assert.AreNotEqual(0u, fixture.Command(3, raise), $"Changed EH publication word {address}.");
            arena[address] ^= 1;
            CollectionAssert.AreEqual(protectedRoot, arena.AsSpan((int)poolRoot, protectedRoot.Length).ToArray());
            Assert.AreEqual(WarpPortableSourceFaultFactoryLayout.Taken, arena[fixture.Prepared]);
        }
        Assert.AreEqual(0u, fixture.Command(3, raise));
        Assert.AreEqual(WarpPortableSourceFaultFactoryLayout.Transferred, arena[fixture.Prepared]);
    }

    [TestMethod]
    public void PlausibleGrantedFixtureDataCannotAdmitTheOriginalImplicitFaultSource()
    {
        var fixture = new WarpPortableFaultTicketDriver("Divide", smallQuantum: true);
        fixture.FillGrantedFixture();
        WarpVerificationException failure = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableWordLowerer.Lower(fixture.Program.Graph, fixture.Program.Typed));
        Assert.AreEqual("WRPCLR2300", failure.Code, StringComparer.Ordinal);
        Assert.AreEqual(WarpPortableSourceFaultFactoryLayout.Granted, fixture.Arena[fixture.Prepared]);
    }
}
