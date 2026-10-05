using System.Reflection;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableClosedFrameExceptionCases
{
    internal static WarpPortableExceptionSourceProgram Capture(string method)
    {
        MethodInfo entry = typeof(Sources).GetMethod(method)!;
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(entry,
            concreteTypes: [typeof(InvalidOperationException), typeof(StackOverflowException)]);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        var exceptions = new WarpPortableExceptionSourceBinding(graph, typed, schema, WarpPortableExceptionTestBuilder.Controller);
        WarpPortableClosedFrameSourcePlan plan = WarpPortableClosedFrameSourcePlan.CaptureForExceptionComposition(graph, typed, schema, exceptions);
        var binding = new WarpPortableClosedFrameExceptionBinding(plan, exceptions);
        WarpPortableWordLoweredProgram lowered = WarpPortableWordLowerer.Lower(graph, typed, schema, binding);
        var layout = new WarpLogicalMachineLayout(lowered.Kernel);
        Assert.AreEqual(binding.ExceptionPlan.LayoutHash, WarpPortableExceptionPlan.ComputeLayoutHash(layout), StringComparer.Ordinal);
        _ = WarpPortableWordProgramIdentity.Validate(graph, schema, lowered);
        return new(graph, typed, schema, lowered, binding.ExceptionPlan, layout);
    }

    internal static void NormalActualConstructorAndHandledCalleeRetainBothReferenceFields()
    {
        foreach (string method in new[] { nameof(Sources.NormalPair), nameof(Sources.ConstructorHandlesItsCallee) })
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableClosedFrameExceptionDriver(method, quantum);
            driver.Execute();
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset]);
            Assert.IsGreaterThan(0u, driver.FrameOwnersValidated);
            WarpPortableTypedType result = driver.Program.Lowered.EntryProjection.ResultType;
            Assert.HasCount(2, result.ManagedRootByteOffsets);
            foreach (int offset in result.ManagedRootByteOffsets)
            {
                CollectionAssert.AreEqual(driver.Owner, Enumerable.Range(offset / 4, 3)
                    .Select(word => driver.State[driver.Program.Layout.GetResultWordOffset(word, WarpPortableClosedFrameExceptionDriver.MaximumDepth)]).ToArray());
            }
            Assert.IsTrue(driver.Program.Lowered.Bodies.SelectMany(body => body.PrivateTemporaries).Any(temporary => temporary.Owners.Length == 2));
        }
    }

    internal static void ActualFailingConstructorUsesOrderedCatchFinallyAndFilterExecution()
    {
        foreach ((string method, uint expected, uint? flag) in new (string, uint, uint?)[]
        {
            (nameof(Sources.FailedPair), 101, null), (nameof(Sources.FinallyPair), 107, null),
            (nameof(Sources.FilterPair), 301, 1), (nameof(Sources.FilterPair), 302, 0),
            (nameof(Sources.FilterCallsOrdinaryConstructorFrame), 401, null),
        })
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableClosedFrameExceptionDriver(method, quantum, flag);
            driver.Execute();
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(expected, driver.State[driver.Program.Layout.GetResultWordOffset(0, WarpPortableClosedFrameExceptionDriver.MaximumDepth)]);
            Assert.IsGreaterThan(0u, driver.FrameOwnersValidated);
            Assert.IsGreaterThan(0u, driver.Boundaries);
        }
    }

    internal static void AliasTailAddressesAndMissingImplicitFactoriesRemainDenied()
    {
        foreach (string method in new[] { nameof(Sources.AliasCreatesValueInItsOwnTail), nameof(Sources.AliasBorrowsStorage),
            nameof(Sources.FaultingMath), nameof(Sources.ThrowNull) })
        {
            WarpVerificationException denied = Assert.ThrowsExactly<WarpVerificationException>(() => Capture(method));
            Assert.IsTrue(denied.Code is "WRPCLR2430" or "WRPCLR2300" or "WRPCLR2390");
        }
    }
}
