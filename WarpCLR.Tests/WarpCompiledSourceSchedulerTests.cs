using System.Diagnostics.CodeAnalysis;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed partial class WarpCompiledSourceSchedulerTests
{
    private static readonly Lazy<WarpCompiledRuntimeServices> Services = new(static () => new());

    [TestMethod]
    public void CompiledControllerClaimsOnceAndRejectsCopiedAndReleasedCapabilities()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 5, 2, [Enumerable.Repeat(4u, 5).ToArray()]);
        var controller = new WarpCompiledController(context.Arena, context.Scheduler);
        WarpCompiledControllerGrant grant = controller.TryAcquire()!;
        Assert.IsNotNull(grant);
        uint[] before = (uint[])context.Arena.Clone();
        Assert.IsNull(controller.TryAcquire());
        CollectionAssert.AreEqual(before, context.Arena);
        Assert.AreEqual(1u, controller.FailedClaims);
        var copied = new WarpCompiledControllerGrant(controller, grant.Token);
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.Validate(copied));
        WarpCompiledWordService service = WarpCompiledWordService.Create(typeof(WarpPortableSchedulerServices), nameof(WarpPortableSchedulerServices.TryAcquireWorker));
        WarpCompiledServiceContinuation continuation = service.Start([context.Scheduler, grant.Token, 0]);
        Assert.IsFalse(continuation.Resume(context.Arena, service.Layout.MaximumBlockCost));
        Assert.IsNull(controller.TryAcquire());
        for (int attempt = 0; continuation.Status == WarpLogicalMachineLayout.Runnable && attempt < 10000; attempt++)
        {
            continuation.Resume(context.Arena, service.Layout.MaximumBlockCost);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, continuation.Status);
        Assert.AreEqual(0u, continuation.Result);
        controller.Release(grant);
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.Release(grant));
        Assert.AreEqual(0u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.ControllerOwner]);
    }

    [TestMethod]
    public void ActualCompiledSourceRunsEveryOversubscribedWorkerFairly()
    {
        uint[] counts = Enumerable.Range(1, 13).Select(value => (uint)value).ToArray();
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 13, 2, [counts]);
        for (uint worker = 0; worker < 13; worker++) { context.Advance(worker % 2); }
        for (uint worker = 0; worker < 13; worker++) { Assert.AreEqual(1u, context.WorkerQuanta(worker)); }
        Assert.AreEqual(WarpCompiledSourceBoundary.BeforeSource, context.MachineState(0)[WarpCompiledSourceBoundary.StateOffset]);
        Assert.AreEqual((uint)context.Plan.MaximumSteps, context.MachineState(0)[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        Drain(context);
        for (uint worker = 0; worker < 13; worker++)
        {
            Assert.AreEqual(counts[worker] * (counts[worker] + 1) / 2, context.Result(worker, context.Dispatch)[0]);
            uint entry = Worker(context, worker);
            Assert.AreEqual(context.MachineState(worker)[WarpLogicalMachineLayout.RemainingStepsLowOffset],
                context.Arena[entry + WarpPortableSchedulerLayout.StepRemainingLow]);
            Assert.AreEqual((uint)context.Plan.MaximumSteps,
                context.Arena[entry + WarpPortableSchedulerLayout.StepRemainingLow] + context.Arena[entry + WarpPortableSchedulerLayout.StepSpentLow]);
        }
    }

    [TestMethod]
    public void SourceArithmeticHelpersPreserveExactBitsAndDoNotConsumeSourceBudget()
    {
        var source = CaptureSource(nameof(Kernels.Single));
        WarpPortableWordLoweredProgram program = source.Program;
        int sourceSteps = program.Bodies[0].SourceBlocks.Length;
        var plan = new WarpCompiledSourcePlan(source.Graph, source.Schema, program, 7, 1, 16, sourceSteps, 1, Services.Value);
        WarpCompiledSourceContext context = Bind(plan,
            [Enumerable.Repeat(0x3F800000u, 7).ToArray(), Enumerable.Repeat(0x40000000u, 7).ToArray()]);
        Drain(context);
        for (uint worker = 0; worker < 7; worker++)
        {
            CollectionAssert.AreEqual(new uint[] { 0x40A00000 }, context.Result(worker, context.Dispatch));
            Assert.AreEqual(0u, context.MachineState(worker)[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
            Assert.IsGreaterThan((uint)sourceSteps, context.WorkerQuanta(worker));
        }
    }

    [TestMethod]
    public void ReversedCompiledFaultArrivalChoosesLowestLogicalWorker()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 5, 2,
            [Enumerable.Repeat(10u, 5).ToArray()], maximumSteps: 1);
        for (int attempt = 0; attempt < 10; attempt++) { context.Advance((uint)attempt % 2); }
        for (uint worker = 0; worker < 5; worker++)
        {
            Assert.AreEqual(0u, context.MachineState(worker)[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        }
        WarpCompiledWorkerTicket first = context.Claim(0)!;
        WarpCompiledWorkerTicket second = context.Claim(1)!;
        Assert.AreEqual(0u, first.Worker);
        Assert.AreEqual(1u, second.Worker);
        context.Execute(first);
        context.Execute(second);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, context.MachineState(first.Worker)[WarpLogicalMachineLayout.StatusOffset]);
        context.Commit(second);
        context.Commit(first);
        Assert.AreEqual(0u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.FaultWinner]);
        Assert.AreEqual(WarpPortableSchedulerLayout.StepsExhausted, context.Arena[Worker(context, 0) + WarpPortableSchedulerLayout.FaultKind]);
        WarpCompiledSourceLocation location = context.Plan.FaultLocation(context.MachineState(0).ToArray());
        Assert.AreEqual(location.CilOffset, context.Arena[Worker(context, 0) + WarpPortableSchedulerLayout.FaultCilOffset]);
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Result(0, context.Dispatch));
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Commit(first));
    }

    [TestMethod]
    public void GeneratedLogicalSourceDepthFaultOmitsHelperFrames()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Recursive), 6, 1,
            [Enumerable.Repeat(12u, 6).ToArray()], maximumDepth: 4);
        Drain(context);
        uint winner = context.Arena[context.Scheduler + WarpPortableSchedulerLayout.FaultWinner];
        Assert.AreEqual(WarpPortableSchedulerLayout.StackExhausted,
            context.Arena[Worker(context, winner) + WarpPortableSchedulerLayout.FaultKind]);
        Assert.AreEqual(4u, context.Arena[Worker(context, winner) + WarpPortableSchedulerLayout.FaultDepth]);
        Assert.AreEqual(WarpLogicalMachineLayout.CallDepthFault,
            context.MachineState(winner)[WarpLogicalMachineLayout.FaultKindOffset]);
        WarpCompiledSourceFault fault = context.EscapedFault()!;
        Assert.AreEqual(context.Plan.Identity, fault.PlanIdentity, StringComparer.Ordinal);
        Assert.AreEqual(context.Identity, fault.ContextIdentity, StringComparer.Ordinal);
        Assert.HasCount(4, fault.SourceFrames);
        Assert.IsTrue(fault.SourceFrames.All(frame => frame.Body is not null));
    }

    [TestMethod]
    public void ExplicitStoppedRuntimeFailureQuarantinesInsteadOfReturningAnUnexecutedSourceResult()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 5, 1, [Enumerable.Repeat(10u, 5).ToArray()]);
        WarpCompiledWorkerTicket ticket = context.Claim(0)!;
        WarpCompiledPausedCensus census = context.CapturePausedCensus()!;
        Assert.AreEqual(0u, context.QuarantineStoppedExecutionFailure(ticket, census));
        Assert.AreEqual(WarpPortableSchedulerLayout.DisposedContext, context.State);
        Assert.AreEqual(0u, context.WorkerQuanta(ticket.Worker));
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Result(ticket.Worker, context.Dispatch));
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Execute(ticket));
        Assert.AreEqual(1u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.OutputQuarantined]);
    }

    [TestMethod]
    public void UnboundSourceHeapAndExceptionExecutionRemainExplicitAtAdmission()
    {
        WarpVerificationException allocation = Assert.ThrowsExactly<WarpVerificationException>(() => Lower(nameof(Kernels.NewObject)));
        Assert.AreEqual("WRPCLR2300", allocation.Code, StringComparer.Ordinal);
        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(() => Lower(nameof(Kernels.WithFinally)));
        Assert.AreEqual("WRPCLR2300", exception.Code, StringComparer.Ordinal);
    }

    private static (WarpPortableMethodGraph Graph, WarpPortableSourceHeapSchema Schema, WarpPortableWordLoweredProgram Program) CaptureSource(string name)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Kernels).GetMethod(name)!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        return (graph, schema, WarpPortableWordLowerer.Lower(graph, typed));
    }

    private static WarpPortableWordLoweredProgram Lower(string name) => CaptureSource(name).Program;

    private static WarpCompiledSourceContext Create(string name, uint workers, uint residents, uint[][] arguments,
        int maximumDepth = 16, long maximumSteps = 100000)
    {
        var source = CaptureSource(name);
        return Bind(new(source.Graph, source.Schema, source.Program, workers, residents, maximumDepth, maximumSteps, 1, Services.Value), arguments);
    }

    private static WarpCompiledSourceContext Bind(WarpCompiledSourcePlan plan, uint[][] arguments)
    {
        WarpCompiledSourceEvidence.Capture(plan);
        uint roots = checked(plan.Workers * ((uint)plan.RootTupleCount + 1 + (uint)plan.ResultRootWords.Length) + 4);
        uint[] heap = plan.TypeSchema.CreateArena(WarpLogicalOwnerNamespace.Next(), 1024, 64, roots, plan.Workers, 1024);
        return new(plan, heap, arguments);
    }

    private static uint ObjectType(WarpCompiledSourceContext context) =>
        context.Plan.TypeSchema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(object)));

    private static uint Worker(WarpCompiledSourceContext context, uint worker) =>
        context.Scheduler + context.Arena[context.Scheduler + WarpPortableSchedulerLayout.WorkerStart] + worker * WarpPortableSchedulerLayout.WorkerWords;

    private static void Drain(WarpCompiledSourceContext context)
    {
        for (int attempt = 0; attempt < 100000; attempt++)
        {
            if (context.State is WarpPortableSchedulerLayout.CompletedContext or WarpPortableSchedulerLayout.CancelledContext or
                WarpPortableSchedulerLayout.DisposedContext ||
                (context.State == WarpPortableSchedulerLayout.Quarantined &&
                    context.Arena[context.Scheduler + WarpPortableSchedulerLayout.RunningCount] == 0))
            {
                return;
            }
            context.Advance((uint)attempt % context.Plan.Residents);
        }
        Assert.Fail("Compiled source/controller dispatch did not reach a terminal progress state within its bound.");
    }

    private static class Kernels
    {
        public static int Loop(int count) { int sum = 0; while (count > 0) { sum += count; count--; } return sum; }
        public static int Recursive(int count) => count <= 0 ? 0 : count + Recursive(count - 1);
        public static float Single(float left, float right) => left + right * right;
        public static object Identity(object value) => value;
        public static object OwnerLoop(object value, int count)
        {
            object result = value;
            while (count > 0) { count--; }
            return result;
        }
        public static object OwnerStopLoop(object value, uint count)
        {
            while (count > 0) { count--; }
            return value;
        }
        public static object SelectOwner(object left, object right, int selector)
        {
            object value = left;
            if (selector != 0) { value = right; }
            return value;
        }
        public static object Nested(object value, int depth) => depth == 0 ? value : Nested(value, depth - 1);
        public static object? SwitchOwner(object value, int selector) => selector switch
        {
            0 => value,
            1 => value,
            2 => value,
            3 => value,
            _ => null,
        };
        public static object NewObject() => new object();
        public static int WithFinally(int value) { try { return value; } finally { value++; } }
    }
}
