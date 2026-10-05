using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    public void ActualFilterCalleeTraceProjectsOriginalActivationsAndTheActiveFilterCallSite()
    {
        MethodInfo method = typeof(WarpPortableExceptionFixtureSources).GetMethod(nameof(WarpPortableExceptionFixtureSources.CalledFilter))!;
        Func<Exception, Exception, bool, int> reference = method.CreateDelegate<Func<Exception, Exception, bool, int>>();
        var outer = new InvalidOperationException("original-filter-source"); var inner = new InvalidOperationException("explicit-filter-callee");
        Assert.AreEqual(97, reference(outer, inner, true));
        StackFrame[] clr = new StackTrace(inner, fNeedFileInfo: false).GetFrames()
            .Where(frame => frame.GetMethod()?.DeclaringType == typeof(WarpPortableExceptionFixtureSources)).ToArray();
        TestContext.WriteLine("Independent CLR source frames: " + string.Join(';', clr.Select(frame => frame.GetMethod()!.Name + " IL " + frame.GetILOffset())));
        CollectionAssert.AreEqual(new[] { nameof(WarpPortableExceptionFixtureSources.FilterWithEh) },
            clr.Select(frame => frame.GetMethod()!.Name).ToArray());
        foreach (int quantum in new[] { 31, 4096 }) { AssertLogicalFilterTrace(quantum, clr); }
    }

    private static void AssertLogicalFilterTrace(int quantum, StackFrame[] reference)
    {
        var driver = new WarpPortableExceptionSourceDriver(nameof(WarpPortableExceptionFixtureSources.CalledFilter), quantum,
            replacementType: typeof(InvalidOperationException), flag: 1);
        driver.Execute();
        uint slot = driver.Arena[WarpPortableHeapLayout.SlotStart] + (driver.SecondOwner[1] - 1) * WarpPortableHeapLayout.SlotWords;
        uint exception = driver.Arena[slot + WarpPortableHeapLayout.SlotPayload];
        uint[] trace = driver.Arena.AsSpan((int)(exception + WarpPortableSourceExceptionLayout.TraceReferenceWord), 3).ToArray();
        uint traceSlot = driver.Arena[WarpPortableHeapLayout.SlotStart] + (trace[1] - 1) * WarpPortableHeapLayout.SlotWords;
        uint payload = driver.Arena[traceSlot + WarpPortableHeapLayout.SlotPayload];
        Assert.AreEqual(1u, driver.Arena[payload + WarpPortableExceptionTraceLayout.Count]);
        Assert.AreEqual(2u, driver.Arena[payload + WarpPortableExceptionTraceLayout.RawCount]);
        Assert.AreEqual(WarpPortableExceptionTraceLayout.Projected, driver.Arena[payload + WarpPortableExceptionTraceLayout.ProjectionState]);
        Assert.IsGreaterThanOrEqualTo(3u, driver.MaximumCapturedFrames);
        var methods = driver.Program.Graph.Methods.ToDictionary(method => (uint)method.Id);
        uint first = payload + driver.Arena[payload + WarpPortableExceptionTraceLayout.RawStart]; uint second = first + WarpPortableExceptionTraceLayout.FrameWords;
        uint projected = payload + driver.Arena[payload + WarpPortableExceptionTraceLayout.ManagedStart];
        string[] logical = [methods[driver.Arena[projected]].SourceMethod.Name];
        CollectionAssert.AreEqual(reference.Select(frame => frame.GetMethod()!.Name).ToArray(), logical);
        var caller = methods[driver.Arena[first]]; var callee = methods[driver.Arena[second]];
        var calls = caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call &&
            string.Equals(instruction.Method, callee.Identity, StringComparison.Ordinal)).ToArray();
        var throws = callee.Instructions.Where(instruction => instruction.OpCode == OpCodes.Throw).ToArray();
        Assert.HasCount(1, calls); Assert.HasCount(1, throws);
        uint call = (uint)calls[0].Offset; uint raised = (uint)throws[0].Offset;
        Assert.AreEqual(call, driver.Arena[first + WarpPortableExceptionTraceLayout.Offset]);
        Assert.AreEqual(raised, driver.Arena[second + WarpPortableExceptionTraceLayout.Offset]);
        Assert.AreEqual(driver.Arena[second + WarpPortableExceptionTraceLayout.Method], driver.Arena[projected + WarpPortableExceptionTraceLayout.Method]);
        Assert.AreEqual(driver.Arena[second + WarpPortableExceptionTraceLayout.Offset], driver.Arena[projected + WarpPortableExceptionTraceLayout.Offset]);
        Assert.AreEqual(1u, driver.Arena[payload + WarpPortableExceptionTraceLayout.FirstRawFrame]);
        Assert.AreNotEqual(driver.Arena[first + WarpPortableExceptionTraceLayout.Activation], driver.Arena[second + WarpPortableExceptionTraceLayout.Activation]);
        Assert.AreEqual((uint)callee.Id, driver.Arena[exception + WarpPortableSourceExceptionLayout.ThrowMethodWord]);
        Assert.AreEqual(driver.Program.Plan.TraceProjectionHash, WarpPortableExceptionPlan.ReadHash(driver.Arena,
            driver.Descriptor + WarpPortableExceptionLayout.TraceProjectionHash), StringComparer.Ordinal);
        Assert.AreEqual(WarpPortableExceptionTraceLayout.Version, driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.TraceVersion]);
    }
}
