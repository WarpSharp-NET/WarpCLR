using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    public void GeneratedCallerCensusBindsTheProtectedCallBeforeItsPostCallContinuation()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.Caller), quantum: quantum);
            uint index = driver.Arena[driver.Worker + WarpPortableExceptionLayout.PreparedRecord];
            uint caller = driver.Captured(index, 1); uint callee = driver.Captured(index, 2);
            Assert.AreEqual(2u, driver.Arena[driver.Prepared + WarpPortableExceptionLayout.FrameCount]);
            uint site = driver.Site(driver.Arena[caller + WarpPortableExceptionLayout.FrameSite]);
            uint callOffset = driver.Arena[site + WarpPortableExceptionLayout.SiteOffset];
            WarpPortableMethodGraphMethod method = driver.Program.Graph.Methods.First(method => string.Equals(method.Identity, driver.Program.Bindings[0].MethodIdentity, StringComparison.Ordinal));
            Assert.AreEqual(OpCodes.Call.Value, unchecked((short)driver.Arena[site + WarpPortableExceptionLayout.SiteOpCode]));
            Assert.IsTrue(method.ExceptionRegions.Any(region => callOffset >= region.TryOffset && callOffset < region.TryOffset + region.TryLength));
            Assert.AreEqual(1u, driver.Arena[callee + WarpPortableExceptionLayout.FrameFunction]);
            Assert.AreEqual(0u, driver.RaiseOwner(1)); uint record = driver.Active;
            uint[] trace = driver.Arena.AsSpan((int)(record + WarpPortableExceptionLayout.TraceReference), 3).ToArray();
            uint payload = driver.Payload(trace);
            Assert.AreEqual(2u, driver.Arena[payload + WarpPortableExceptionTraceLayout.RawCount]);
            Assert.AreEqual(0u, driver.Arena[payload + WarpPortableExceptionTraceLayout.Count]);
            uint raw = payload + driver.Arena[payload + WarpPortableExceptionTraceLayout.RawStart];
            Assert.AreEqual(callOffset, driver.Arena[raw + WarpPortableExceptionTraceLayout.Offset]);
            Assert.AreEqual(driver.Arena[record + WarpPortableExceptionLayout.OriginalOffset],
                driver.Arena[raw + WarpPortableExceptionTraceLayout.FrameWords + WarpPortableExceptionTraceLayout.Offset]);
            uint[] beforeSearch = (uint[])driver.State.Clone();
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
            CollectionAssert.AreEqual(beforeSearch, driver.State);
            Assert.AreEqual(1u, driver.Arena[record + WarpPortableExceptionLayout.SelectedFrame]);
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
            driver.Apply(); driver.CaptureInitial();
            Assert.AreEqual(1u, driver.Arena[driver.Prepared + WarpPortableExceptionLayout.FrameCount]);
            Assert.AreEqual(WarpPortableExceptionLayout.Caught, driver.Arena[record + WarpPortableExceptionLayout.Phase]);
        }
    }

    [TestMethod]
    public void GeneratedSearchDecidesFiltersBeforePreparingAnyFinally()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.SearchBeforeUnwind), quantum: quantum);
            Assert.AreEqual(0u, driver.RaiseOwner()); uint record = driver.Active;
            uint[] source = (uint[])driver.State.Clone();
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
            Assert.AreEqual(WarpPortableExceptionLayout.RunFilter, driver.Arena[record + WarpPortableExceptionLayout.Action]);
            Assert.AreEqual(0u, driver.Arena[record + WarpPortableExceptionLayout.CleanupClause]);
            CollectionAssert.AreEqual(source, driver.State);
            // This independently checks the compiled phase decision. Actual
            // filter source execution requires the separately admitted alias.
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.CompleteFilter), driver.Raise, 0));
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
            uint catchClause = driver.Clause(driver.Arena[record + WarpPortableExceptionLayout.SelectedClause]);
            Assert.AreEqual(0u, driver.Arena[catchClause + WarpPortableExceptionLayout.ClauseKind]);
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
            Assert.AreEqual(WarpPortableExceptionLayout.RunFinally, driver.Arena[record + WarpPortableExceptionLayout.Action]);
            uint clause = driver.Arena[record + WarpPortableExceptionLayout.CleanupClause];
            uint[] before = (uint[])driver.Arena.Clone();
            Assert.AreEqual(WarpPortableExceptionLayout.InvalidPhase, driver.Service(nameof(WarpPortableExceptionServices.EndCleanup), driver.Raise, clause));
            CollectionAssert.AreEqual(before, driver.Arena);
            driver.Apply(); driver.CaptureInitial();
            Assert.AreEqual(2u, driver.Arena[record + WarpPortableExceptionLayout.Flags] & 2);
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.EndCleanup), driver.Raise, clause));
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
            Assert.AreEqual(WarpPortableExceptionLayout.EnterCatch, driver.Arena[record + WarpPortableExceptionLayout.Action]);
        }
    }

    [TestMethod]
    public void GeneratedRethrowKeepsTheSameObjectAndOriginalTraceIdentity()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.Rethrow), quantum: quantum);
            Assert.AreEqual(0u, driver.RaiseOwner()); uint first = driver.Active;
            uint originalOffset = driver.Arena[first + WarpPortableExceptionLayout.OriginalOffset];
            uint traceIdentity = driver.Arena[first + WarpPortableExceptionLayout.TraceIdentity];
            uint[] trace = driver.Arena.AsSpan((int)(first + WarpPortableExceptionLayout.TraceReference), 3).ToArray();
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
            driver.Apply(); driver.CaptureInitial();
            WarpPortableTypedInstruction rethrow = Instruction(driver, OpCodes.Rethrow.Value);
            driver.Move(0, rethrow.Offset);
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.Rethrow), 0, (uint)rethrow.Offset, unchecked((ushort)rethrow.OpCode)));
            uint replacement = driver.Active;
            Assert.AreNotEqual(first, replacement);
            CollectionAssert.AreEqual(driver.Owner, driver.Arena.AsSpan((int)(replacement + WarpPortableExceptionLayout.ExceptionReference), 3).ToArray());
            CollectionAssert.AreEqual(trace, driver.Arena.AsSpan((int)(replacement + WarpPortableExceptionLayout.TraceReference), 3).ToArray());
            Assert.AreEqual(traceIdentity, driver.Arena[replacement + WarpPortableExceptionLayout.TraceIdentity]);
            Assert.AreEqual(originalOffset, driver.Arena[replacement + WarpPortableExceptionLayout.OriginalOffset]);
            Assert.AreEqual(1u, driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.NextTrace]);
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
            Assert.AreEqual(WarpPortableExceptionLayout.EnterCatch, driver.Arena[replacement + WarpPortableExceptionLayout.Action]);
        }
    }

    [TestMethod]
    public void GeneratedFinallyReplacementUsesItsActualThrowSiteAndANewTrace()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.Replace), quantum: quantum);
            Assert.AreEqual(0u, driver.RaiseOwner()); uint first = driver.Active;
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
            Assert.AreEqual(WarpPortableExceptionLayout.RunFinally, driver.Arena[first + WarpPortableExceptionLayout.Action]);
            driver.Apply(); driver.CaptureInitial();
            WarpPortableTypedInstruction throwing = Instructions(driver).Last(instruction => instruction.OpCode == OpCodes.Throw.Value);
            driver.Move(0, throwing.Offset);
            uint captured = driver.Captured(driver.Arena[driver.Worker + WarpPortableExceptionLayout.PreparedRecord], 1);
            uint site = driver.Site(driver.Arena[captured + WarpPortableExceptionLayout.FrameSite]);
            Assert.AreEqual((uint)throwing.Offset, driver.Arena[site + WarpPortableExceptionLayout.SiteOffset]);
            Assert.AreEqual(2u, driver.Arena[site + WarpPortableExceptionLayout.SiteEffectCount]);
            uint[] second = driver.AllocateReplacement(driver.Id(typeof(StackOverflowException)));
            Assert.AreEqual(0u, driver.RaiseReference(second, driver.Id(typeof(StackOverflowException))));
            uint replacement = driver.Active;
            Assert.AreEqual(WarpPortableExceptionLayout.Free, driver.Arena[first + WarpPortableExceptionLayout.Phase]);
            Assert.AreEqual((uint)throwing.Offset, driver.Arena[replacement + WarpPortableExceptionLayout.OriginalOffset]);
            Assert.AreEqual(2u, driver.Arena[replacement + WarpPortableExceptionLayout.TraceIdentity]);
            CollectionAssert.AreEqual(second, driver.Arena.AsSpan((int)(replacement + WarpPortableExceptionLayout.ExceptionReference), 3).ToArray());
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
            Assert.AreEqual(WarpPortableExceptionLayout.EnterCatch, driver.Arena[replacement + WarpPortableExceptionLayout.Action]);
        }
    }

    [TestMethod]
    public void GeneratedLeaveRunsFinallyAndClearsItsRootedRecordBeforeResuming()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.Leave), quantum: quantum);
            WarpPortableTypedInstruction leave = Instructions(driver).First(instruction => instruction.OpCode == OpCodes.Leave.Value || instruction.OpCode == OpCodes.Leave_S.Value);
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.BeginLeave), 0, (uint)leave.Offset, unchecked((ushort)leave.OpCode)));
            uint record = driver.Active;
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
            uint clause = driver.Arena[record + WarpPortableExceptionLayout.CleanupClause];
            Assert.AreEqual(WarpPortableExceptionLayout.RunFinally, driver.Arena[record + WarpPortableExceptionLayout.Action]);
            driver.Apply(); driver.CaptureInitial();
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.EndCleanup), driver.Raise, clause));
            Assert.AreEqual(0u, driver.Service(nameof(WarpPortableExceptionServices.AdvanceUnwind), driver.Raise));
            Assert.AreEqual(WarpPortableExceptionLayout.ResumeLeave, driver.Arena[record + WarpPortableExceptionLayout.Action]);
            driver.Apply();
            // The exact wait recaptures immediately after leave; it may reuse
            // the cleared record, but cannot retain the old raise/reference.
            Assert.AreEqual(WarpPortableExceptionLayout.Prepared, driver.Arena[record + WarpPortableExceptionLayout.Phase]);
            Assert.AreEqual(0u, driver.Arena[record + WarpPortableExceptionLayout.RaiseGeneration]);
            CollectionAssert.AreEqual(new uint[] { 0, 0, 0 }, driver.Arena.AsSpan((int)(record + WarpPortableExceptionLayout.ExceptionReference), 3).ToArray());
            Assert.AreEqual(0u, driver.Arena[driver.Worker + WarpPortableExceptionLayout.ActiveRecord]);
        }
    }

    private static System.Collections.Immutable.ImmutableArray<WarpPortableTypedInstruction> Instructions(WarpPortableExceptionTestDriver driver) =>
        driver.Program.Typed.Methods.First(method => string.Equals(method.Identity, driver.Program.Bindings[0].MethodIdentity, StringComparison.Ordinal)).Instructions;

    private static WarpPortableTypedInstruction Instruction(WarpPortableExceptionTestDriver driver, short opcode) => Instructions(driver).First(instruction => instruction.OpCode == opcode);
}
