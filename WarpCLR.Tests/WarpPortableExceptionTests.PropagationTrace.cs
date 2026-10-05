using System.Diagnostics;
using System.Reflection.Emit;
using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    public void EscapingFilterCalleeTraceStopsAtTheActualFilterBoundaryWithoutLosingTheCapturedOwner()
    {
        var method = typeof(WarpPortableExceptionFixtureSources).GetMethod(nameof(WarpPortableExceptionFixtureSources.FilterEscape))!;
        Func<Exception, Exception, int> reference = method.CreateDelegate<Func<Exception, Exception, int>>();
        var first = new InvalidOperationException("original-filter-reference"); var second = new InvalidOperationException("escaped-filter-reference");
        Assert.AreEqual(83, reference(first, second));
        string[] clr = ClrSourceTrace(second);
        CollectionAssert.AreEqual(new[] { nameof(WarpPortableExceptionFixtureSources.ThrowFilter), nameof(WarpPortableExceptionFixtureSources.FilterEscape) }, clr);
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionSourceDriver(nameof(WarpPortableExceptionFixtureSources.FilterEscape), quantum, replacementType: typeof(InvalidOperationException));
            driver.Execute();
            uint payload = AssertPropagationTrace(driver, driver.SecondOwner, clr);
            Assert.AreEqual(2u, driver.Arena[payload + WarpPortableExceptionTraceLayout.RawCount]);
            Assert.IsGreaterThanOrEqualTo(3u, driver.MaximumCapturedFrames);
            AssertOriginalTraceInstructions(driver, payload, [OpCodes.Call, OpCodes.Throw]);
        }
    }

    [TestMethod]
    public void RethrowExtendsThePropagationBoundaryAndPreservesTheOriginalThrowAndRecursionIdentity()
    {
        var method = typeof(WarpPortableExceptionFixtureSources).GetMethod(nameof(WarpPortableExceptionFixtureSources.RethrowCaller))!;
        Func<Exception, int> reference = method.CreateDelegate<Func<Exception, int>>();
        var exception = new InvalidOperationException("rethrow-reference");
        Assert.AreEqual(137, reference(exception));
        string[] clr = ClrSourceTrace(exception);
        CollectionAssert.AreEqual(new[] { nameof(WarpPortableExceptionFixtureSources.Callee), nameof(WarpPortableExceptionFixtureSources.RethrowCallee),
            nameof(WarpPortableExceptionFixtureSources.RethrowCaller) }, clr);
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionSourceDriver(nameof(WarpPortableExceptionFixtureSources.RethrowCaller), quantum);
            driver.Execute();
            uint payload = AssertPropagationTrace(driver, driver.Owner, clr);
            Assert.AreEqual(3u, driver.Arena[payload + WarpPortableExceptionTraceLayout.RawCount]);
            AssertOriginalTraceInstructions(driver, payload, [OpCodes.Call, OpCodes.Call, OpCodes.Throw]);
            uint throwMethod = driver.Arena[payload + WarpPortableExceptionTraceLayout.ThrowMethod];
            Assert.AreEqual(nameof(WarpPortableExceptionFixtureSources.Callee), driver.Program.Graph.Methods.First(source => source.Id == throwMethod).SourceMethod.Name, StringComparer.Ordinal);
        }
    }

    private static string[] ClrSourceTrace(Exception exception) => new StackTrace(exception, fNeedFileInfo: false).GetFrames()
        .Where(frame => frame.GetMethod()?.DeclaringType == typeof(WarpPortableExceptionFixtureSources))
        .Select(frame => frame.GetMethod()!.Name).ToArray();

    private static uint AssertPropagationTrace(WarpPortableExceptionSourceDriver driver, uint[] owner, string[] clr)
    {
        uint slot = driver.Arena[WarpPortableHeapLayout.SlotStart] + (owner[1] - 1) * WarpPortableHeapLayout.SlotWords;
        uint exception = driver.Arena[slot + WarpPortableHeapLayout.SlotPayload];
        uint trace = driver.Arena[exception + WarpPortableSourceExceptionLayout.TraceReferenceWord + 1];
        uint traceSlot = driver.Arena[WarpPortableHeapLayout.SlotStart] + (trace - 1) * WarpPortableHeapLayout.SlotWords;
        uint payload = driver.Arena[traceSlot + WarpPortableHeapLayout.SlotPayload];
        Assert.AreEqual((uint)clr.Length, driver.Arena[payload + WarpPortableExceptionTraceLayout.Count]);
        Assert.AreEqual(WarpPortableExceptionTraceLayout.Projected, driver.Arena[payload + WarpPortableExceptionTraceLayout.ProjectionState]);
        uint start = payload + driver.Arena[payload + WarpPortableExceptionTraceLayout.ManagedStart];
        var methods = driver.Program.Graph.Methods.ToDictionary(source => (uint)source.Id);
        string[] projected = Enumerable.Range(0, clr.Length).Select(index => methods[driver.Arena[start +
            (uint)(clr.Length - 1 - index) * WarpPortableExceptionTraceLayout.FrameWords + WarpPortableExceptionTraceLayout.Method]].SourceMethod.Name).ToArray();
        CollectionAssert.AreEqual(clr, projected);
        return payload;
    }

    private static void AssertOriginalTraceInstructions(WarpPortableExceptionSourceDriver driver, uint payload, OpCode[] expected)
    {
        uint start = payload + driver.Arena[payload + WarpPortableExceptionTraceLayout.ManagedStart];
        for (uint index = 0; index < (uint)expected.Length; index++)
        {
            uint row = start + index * WarpPortableExceptionTraceLayout.FrameWords;
            var method = driver.Program.Graph.Methods.First(source => source.Id == driver.Arena[row + WarpPortableExceptionTraceLayout.Method]);
            uint offset = driver.Arena[row + WarpPortableExceptionTraceLayout.Offset];
            Assert.AreEqual(expected[index], method.Instructions.First(instruction => instruction.Offset == offset).OpCode);
        }
    }
}
