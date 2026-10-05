using System.Diagnostics;
using System.Reflection.Emit;
using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    public void ActualRecursiveTraceRetainsEveryOriginalSourceActivationWithoutDeduplicatingMethodIdentity()
    {
        var method = typeof(WarpPortableExceptionFixtureSources).GetMethod(nameof(WarpPortableExceptionFixtureSources.RecursiveTrace))!;
        Func<Exception, int, int> reference = method.CreateDelegate<Func<Exception, int, int>>();
        var exception = new InvalidOperationException("source-recursion-reference");
        Assert.AreEqual(131, reference(exception, 2));
        string[] clr = new StackTrace(exception, fNeedFileInfo: false).GetFrames()
            .Where(frame => frame.GetMethod()?.DeclaringType == typeof(WarpPortableExceptionFixtureSources))
            .Select(frame => frame.GetMethod()!.Name).ToArray();
        CollectionAssert.AreEqual(new[] { nameof(WarpPortableExceptionFixtureSources.RecursiveThrow), nameof(WarpPortableExceptionFixtureSources.RecursiveThrow),
            nameof(WarpPortableExceptionFixtureSources.RecursiveThrow), nameof(WarpPortableExceptionFixtureSources.RecursiveTrace) }, clr);
        foreach (int quantum in new[] { 31, 4096 }) { AssertRecursiveTrace(quantum, clr); }
    }

    private static void AssertRecursiveTrace(int quantum, string[] clr)
    {
        var driver = new WarpPortableExceptionSourceDriver(nameof(WarpPortableExceptionFixtureSources.RecursiveTrace), quantum, flag: 2);
        driver.Execute();
        uint slot = driver.Arena[WarpPortableHeapLayout.SlotStart] + (driver.Owner[1] - 1) * WarpPortableHeapLayout.SlotWords;
        uint exception = driver.Arena[slot + WarpPortableHeapLayout.SlotPayload];
        uint[] owner = driver.Arena.AsSpan((int)(exception + WarpPortableSourceExceptionLayout.TraceReferenceWord), 3).ToArray();
        uint traceSlot = driver.Arena[WarpPortableHeapLayout.SlotStart] + (owner[1] - 1) * WarpPortableHeapLayout.SlotWords;
        uint payload = driver.Arena[traceSlot + WarpPortableHeapLayout.SlotPayload];
        Assert.AreEqual(4u, driver.Arena[payload + WarpPortableExceptionTraceLayout.Count]);
        Assert.AreEqual(4u, driver.Arena[payload + WarpPortableExceptionTraceLayout.RawCount]);
        uint projected = payload + driver.Arena[payload + WarpPortableExceptionTraceLayout.ManagedStart];
        var methods = driver.Program.Graph.Methods.ToDictionary(method => (uint)method.Id);
        string[] logical = Enumerable.Range(0, 4).Select(index => methods[driver.Arena[projected +
            (uint)(3 - index) * WarpPortableExceptionTraceLayout.FrameWords]].SourceMethod.Name).ToArray();
        CollectionAssert.AreEqual(clr, logical);
        for (uint frame = 0; frame < 4; frame++)
        {
            uint row = projected + frame * WarpPortableExceptionTraceLayout.FrameWords;
            var source = methods[driver.Arena[row + WarpPortableExceptionTraceLayout.Method]];
            uint offset = driver.Arena[row + WarpPortableExceptionTraceLayout.Offset];
            var instruction = source.Instructions.First(instruction => instruction.Offset == offset);
            Assert.AreEqual(frame == 3 ? OpCodes.Throw : OpCodes.Call, instruction.OpCode);
        }
    }
}
