using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    public void GeneratedPropagationRejectsAStaleActivationOrMalformedTraceBeforeChangingTheRawCensus()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.Catch), quantum: quantum, stopHandlers: false);
            Assert.AreEqual(0u, driver.RaiseOwner()); uint record = driver.Active;
            uint[] owner = driver.Arena.AsSpan((int)(record + WarpPortableExceptionLayout.TraceReference), 3).ToArray();
            uint payload = driver.Payload(owner); uint raw = payload + driver.Arena[payload + WarpPortableExceptionTraceLayout.RawStart];
            uint[] before = (uint[])driver.Arena.Clone();
            Assert.AreEqual(WarpPortableExceptionLayout.InvalidContinuation, WarpPortableExceptionTestService.Run(typeof(WarpPortableExceptionServices),
                "ProjectManagedTrace", driver.Arena, [driver.Descriptor, record, driver.Arena[raw + WarpPortableExceptionTraceLayout.Physical],
                    driver.Arena[raw + WarpPortableExceptionTraceLayout.Activation] + 1], quantum));
            CollectionAssert.AreEqual(before, driver.Arena);
            driver.Arena[payload + WarpPortableExceptionTraceLayout.ManagedStart] = uint.MaxValue;
            before = (uint[])driver.Arena.Clone();
            Assert.AreEqual(WarpPortableExceptionLayout.InvalidReference, WarpPortableExceptionTestService.Run(typeof(WarpPortableExceptionServices),
                "ProjectManagedTrace", driver.Arena, [driver.Descriptor, record, 0, 0], quantum));
            CollectionAssert.AreEqual(before, driver.Arena);
            driver.Arena[payload + WarpPortableExceptionTraceLayout.ManagedStart] = WarpPortableExceptionTraceLayout.HeaderWords +
                driver.Arena[payload + WarpPortableExceptionTraceLayout.Capacity] * WarpPortableExceptionTraceLayout.FrameWords;
            uint[] rawBefore = driver.Arena.AsSpan((int)raw, (int)WarpPortableExceptionTraceLayout.FrameWords).ToArray();
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
            driver.Apply();
            CollectionAssert.AreEqual(rawBefore, driver.Arena.AsSpan((int)raw, (int)WarpPortableExceptionTraceLayout.FrameWords).ToArray());
            Assert.AreEqual(1u, driver.Arena[payload + WarpPortableExceptionTraceLayout.Count]);
            Assert.AreEqual(1u, driver.Arena[record + WarpPortableExceptionLayout.FrameCount]);
        }
    }
}
