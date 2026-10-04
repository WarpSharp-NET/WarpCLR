using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed partial class WarpPortableSchedulerTests
{
    private const uint Controller = 1;

    [TestMethod]
    [DataRow(1)]
    [DataRow(31)]
    public void OversubscribedRoundRobinCompletesExactUserResultsAtDifferentQuanta(int quantum)
    {
        const uint workers = 131;
        const uint target = 13;
        uint[] arena = Schema(workers, 3, (uint)quantum).CreateArena(101);
        Claim(arena, 0);
        uint[] progress = new uint[workers];
        uint[] output = new uint[workers];
        var firstRound = new HashSet<uint>();
        uint completed = 0;
        uint turns = 0;
        while (completed < workers && turns < workers * (target + 1))
        {
            (uint worker, uint generation) = Acquire(arena, 0, turns % 3);
            if (turns < workers)
            {
                Assert.IsTrue(firstRound.Add(worker), "A ready logical worker repeated before every admitted worker received a quantum.");
            }
            uint charged = 0;
            while (charged < (uint)quantum && progress[worker] < target)
            {
                Success(WarpPortableSchedulerServices.ChargeQuantum(arena, 0, Controller, worker, generation, 1));
                Success(WarpPortableSchedulerServices.ChargeUserSteps(arena, 0, Controller, worker, generation, 1));
                output[worker] += 0xFFFF1234u ^ worker;
                progress[worker]++;
                charged++;
            }
            if (progress[worker] == target)
            {
                Success(WarpPortableSchedulerServices.CompleteWorker(arena, 0, Controller, worker, generation));
                completed++;
            }
            else
            {
                Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.ChargeQuantum(arena, 0, Controller, worker, generation, 1));
                Success(WarpPortableSchedulerServices.YieldWorker(arena, 0, Controller, worker, generation));
            }
            turns++;
        }
        Assert.AreEqual(workers, completed);
        Assert.HasCount((int)workers, firstRound);
        for (uint worker = 0; worker < workers; worker++)
        {
            Assert.AreEqual(unchecked((0xFFFF1234u ^ worker) * target), output[worker]);
            Assert.AreEqual(target, arena[Entry(arena, 0, worker) + WarpPortableSchedulerLayout.StepSpentLow]);
        }
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchCompleted, WarpPortableSchedulerServices.TryAcquireWorker(arena, 0, Controller, 0));
        Assert.AreEqual(0u, arena[WarpPortableSchedulerLayout.OutputQuarantined]);
    }

    [TestMethod]
    public void GridBarrierRepeatsAcrossEveryLogicalWorkerBeyondResidency()
    {
        const uint workers = 129;
        uint[] arena = Schema(workers, 2, 1, barriers: [Grid(workers, 17)]).CreateArena(102);
        Claim(arena, 0);
        uint barrier = Collective(arena, 0, 0);
        for (uint generation = 1; generation <= 9; generation++)
        {
            for (uint arrived = 0; arrived < workers; arrived++)
            {
                (uint worker, uint run) = Acquire(arena, 0, arrived % 2);
                uint source = arena[Entry(arena, 0, worker) + WarpPortableSchedulerLayout.LogicalStackBase];
                arena[source + 8] = generation;
                Success(WarpPortableSchedulerServices.ArriveBarrier(arena, 0, Controller, worker, run, 0, generation, 17));
                Assert.AreEqual(0u, arena[WarpPortableSchedulerLayout.RunningCount]);
                Assert.AreEqual(arrived + 1 == workers ? generation + 1 : generation, arena[barrier + WarpPortableSchedulerLayout.BarrierGeneration]);
            }
            Assert.AreEqual(0u, arena[barrier + WarpPortableSchedulerLayout.BarrierArrived]);
            for (uint worker = 0; worker < workers; worker++)
            {
                uint entry = Entry(arena, 0, worker);
                Assert.AreEqual(WarpPortableSchedulerLayout.Ready, arena[entry + WarpPortableSchedulerLayout.WorkerState]);
                Assert.AreEqual(generation, arena[arena[entry + WarpPortableSchedulerLayout.LogicalStackBase] + 8]);
            }
        }
    }

    [TestMethod]
    public void GroupAndSubgroupMembershipReleaseIndependentCollectives()
    {
        WarpPortableSchedulerBarrierLayout even = new(21, WarpPortableSchedulerLayout.GroupScope, [0, 2, 4]);
        WarpPortableSchedulerBarrierLayout odd = new(22, WarpPortableSchedulerLayout.SubgroupScope, [1, 3, 5]);
        uint[] arena = Schema(6, 1, 2, barriers: [even, odd]).CreateArena(103);
        Claim(arena, 0);
        for (uint expected = 0; expected < 6; expected++)
        {
            (uint worker, uint generation) = Acquire(arena, 0, 0);
            Assert.AreEqual(expected, worker);
            uint id = worker & 1;
            Success(WarpPortableSchedulerServices.ArriveBarrier(arena, 0, Controller, worker, generation, id, 1, id == 0 ? 21u : 22u));
            if (worker == 4)
            {
                Assert.AreEqual(2u, arena[Collective(arena, 0, 0) + WarpPortableSchedulerLayout.BarrierGeneration]);
                Assert.AreEqual(1u, arena[Collective(arena, 0, 1) + WarpPortableSchedulerLayout.BarrierGeneration]);
            }
        }
        Assert.AreEqual(2u, arena[Collective(arena, 0, 1) + WarpPortableSchedulerLayout.BarrierGeneration]);
    }

    [TestMethod]
    public void MissingDuplicateAndCrossGenerationParticipationCannotSilentlyRelease()
    {
        uint[] arena = Schema(2, 1, 1, barriers: [Grid(2, 17)]).CreateArena(104);
        Claim(arena, 0);
        (uint worker, uint generation) = Acquire(arena, 0, 0);
        Success(WarpPortableSchedulerServices.ArriveBarrier(arena, 0, Controller, worker, generation, 0, 1, 17));
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.ArriveBarrier(arena, 0, Controller, worker, generation, 0, 1, 17));
        Assert.AreEqual(1u, arena[Collective(arena, 0, 0) + WarpPortableSchedulerLayout.BarrierArrived]);
        (uint peer, uint peerRun) = Acquire(arena, 0, 0);
        Success(WarpPortableSchedulerServices.ArriveBarrier(arena, 0, Controller, peer, peerRun, 0, 1, 17));
        (worker, generation) = Acquire(arena, 0, 0);
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, WarpPortableSchedulerServices.ArriveBarrier(arena, 0, Controller, worker, generation, 0, 1, 17));
        Assert.AreEqual(WarpPortableSchedulerLayout.CollectiveViolation, arena[Entry(arena, 0, worker) + WarpPortableSchedulerLayout.FaultKind]);
        Assert.AreEqual(1u, arena[WarpPortableSchedulerLayout.OutputQuarantined]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EarlyCompletionViolatesRequiredBarrierParticipation(bool completionAfterArrival)
    {
        uint[] arena = Schema(2, 2, 1, barriers: [Grid(2, 17)]).CreateArena(105);
        Claim(arena, 0);
        (uint first, uint firstRun) = Acquire(arena, 0, 0);
        (uint second, uint secondRun) = Acquire(arena, 0, 1);
        if (completionAfterArrival)
        {
            Success(WarpPortableSchedulerServices.ArriveBarrier(arena, 0, Controller, first, firstRun, 0, 1, 17));
            Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, WarpPortableSchedulerServices.CompleteWorker(arena, 0, Controller, second, secondRun));
        }
        else
        {
            Success(WarpPortableSchedulerServices.CompleteWorker(arena, 0, Controller, first, firstRun));
            Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, WarpPortableSchedulerServices.ArriveBarrier(arena, 0, Controller, second, secondRun, 0, 1, 17));
        }
        Assert.AreEqual(WarpPortableSchedulerLayout.BarrierAborted, arena[Collective(arena, 0, 0) + WarpPortableSchedulerLayout.BarrierState]);
    }

    [TestMethod]
    public void NonuniformBarrierCycleProducesBoundedDeterministicViolation()
    {
        uint[] arena = Schema(2, 1, 1, barriers: [Grid(2, 17), Grid(2, 18)]).CreateArena(106);
        Claim(arena, 0);
        for (uint barrier = 0; barrier < 2; barrier++)
        {
            (uint worker, uint generation) = Acquire(arena, 0, 0);
            Success(WarpPortableSchedulerServices.ArriveBarrier(arena, 0, Controller, worker, generation, barrier, 1, 17 + barrier));
        }
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, WarpPortableSchedulerServices.DetectStalledCollective(arena, 0, Controller));
        Assert.AreEqual(0u, arena[WarpPortableSchedulerLayout.FaultWinner]);
        Assert.AreEqual(2u, arena[WarpPortableSchedulerLayout.TerminalCount]);
    }

    [TestMethod]
    public void ControllerRejectionAndStalePhysicalCompletionsDoNotMutateState()
    {
        uint[] arena = Schema(5, 1, 1).CreateArena(107);
        uint[] snapshot = (uint[])arena.Clone();
        Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.TryAcquireWorker(arena, 0, 0, 0));
        Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.TryAcquireWorker(arena, 0, 2, 0));
        CollectionAssert.AreEqual(snapshot, arena);
        Claim(arena, 0);
        snapshot = (uint[])arena.Clone();
        Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.RequestCancellation(arena, 0, 2));
        CollectionAssert.AreEqual(snapshot, arena);
        (uint worker, uint generation) = Acquire(arena, 0, 0);
        Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.TryAcquireWorker(arena, 0, Controller, 0));
        Success(WarpPortableSchedulerServices.YieldWorker(arena, 0, Controller, worker, generation));
        (uint next, uint nextGeneration) = Acquire(arena, 0, 0);
        Assert.AreNotEqual(worker, next);
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.CompleteWorker(arena, 0, Controller, worker, generation));
        Assert.AreEqual(WarpPortableSchedulerLayout.Running, arena[Entry(arena, 0, next) + WarpPortableSchedulerLayout.WorkerState]);
        Success(WarpPortableSchedulerServices.CompleteWorker(arena, 0, Controller, next, nextGeneration));
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.TryAcquireWorker(arena, (uint)arena.Length - 1, Controller, 0));
    }

    [TestMethod]
    public void OperationalHelperCostDoesNotChargeSourceBudgetsOrSourceStack()
    {
        uint[] arena = Schema(1, 1, 3, stackLimit: 2, stepLow: 1, stepHigh: 1).CreateArena(108);
        Claim(arena, 0);
        uint entry = Entry(arena, 0, 0);
        for (uint turn = 0; turn < 17; turn++)
        {
            (uint worker, uint generation) = Acquire(arena, 0, 0);
            Success(WarpPortableSchedulerServices.ChargeQuantum(arena, 0, Controller, worker, generation, 3));
            Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.ChargeQuantum(arena, 0, Controller, worker, generation, 1));
            Success(WarpPortableSchedulerServices.YieldWorker(arena, 0, Controller, worker, generation));
        }
        Assert.AreEqual(0u, arena[entry + WarpPortableSchedulerLayout.StepSpentLow]);
        Assert.AreEqual(1u, arena[entry + WarpPortableSchedulerLayout.UserStackDepth]);
        (uint logical, uint run) = Acquire(arena, 0, 0);
        Success(WarpPortableSchedulerServices.ChargeUserSteps(arena, 0, Controller, logical, run, 2));
        Assert.AreEqual(0xFFFFFFFFu, arena[entry + WarpPortableSchedulerLayout.StepRemainingLow]);
        Assert.AreEqual(0u, arena[entry + WarpPortableSchedulerLayout.StepRemainingHigh]);
        Success(WarpPortableSchedulerServices.SetUserLocation(arena, 0, Controller, logical, run, 71, 33, 90));
        Success(WarpPortableSchedulerServices.EnterUserFrame(arena, 0, Controller, logical, run));
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, WarpPortableSchedulerServices.EnterUserFrame(arena, 0, Controller, logical, run));
        Assert.AreEqual(WarpPortableSchedulerLayout.StackExhausted, arena[entry + WarpPortableSchedulerLayout.FaultKind]);
        Assert.AreEqual(2u, arena[entry + WarpPortableSchedulerLayout.FaultDepth]);
        Assert.AreEqual(71u, arena[entry + WarpPortableSchedulerLayout.FaultFunction]);
        Assert.AreEqual(33u, arena[entry + WarpPortableSchedulerLayout.FaultCilOffset]);
    }

    [TestMethod]
    public void UserStepExhaustionOccursBeforeTheRejectedOperationAndRetainsSourceLocation()
    {
        uint[] arena = Schema(17, 2, 1, stepLow: 1).CreateArena(109);
        Claim(arena, 0);
        (uint worker, uint generation) = Acquire(arena, 0, 0);
        Success(WarpPortableSchedulerServices.SetUserLocation(arena, 0, Controller, worker, generation, 7, 19, 23));
        Success(WarpPortableSchedulerServices.ChargeUserSteps(arena, 0, Controller, worker, generation, 1));
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, WarpPortableSchedulerServices.ChargeUserSteps(arena, 0, Controller, worker, generation, 1));
        uint entry = Entry(arena, 0, worker);
        Assert.AreEqual(1u, arena[entry + WarpPortableSchedulerLayout.StepSpentLow]);
        Assert.AreEqual(0u, arena[entry + WarpPortableSchedulerLayout.StepRemainingLow]);
        Assert.AreEqual(WarpPortableSchedulerLayout.StepsExhausted, arena[entry + WarpPortableSchedulerLayout.FaultKind]);
        Assert.AreEqual(23u, arena[entry + WarpPortableSchedulerLayout.FaultInstruction]);
        Assert.AreEqual(17u, arena[WarpPortableSchedulerLayout.TerminalCount]);
    }

    [TestMethod]
    public void RuntimeServicesHaveOnlyPortableWordSignaturesLocalsAndNoExceptionRegions()
    {
        MethodInfo[] methods = typeof(WarpPortableSchedulerServices).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (ref readonly MethodInfo method in methods.AsSpan())
        {
            Assert.AreEqual(typeof(uint), method.ReturnType, method.Name);
            foreach (ref readonly ParameterInfo parameter in method.GetParameters().AsSpan())
            {
                Assert.IsTrue(parameter.ParameterType == typeof(uint) || parameter.ParameterType == typeof(uint[]), method.Name);
            }
            MethodBody? body = method.GetMethodBody();
            Assert.IsNotNull(body);
            Assert.IsEmpty(body.ExceptionHandlingClauses, method.Name);
            foreach (LocalVariableInfo local in body.LocalVariables)
            {
                Assert.IsTrue(local.LocalType == typeof(uint) || local.LocalType == typeof(bool), method.Name + ": " + local.LocalType.Name);
            }
        }
    }

    private static WarpPortableSchedulerSchema Schema(uint workers, uint residents, uint quantum,
        uint stackLimit = 4, uint stepLow = 4096, uint stepHigh = 0,
        IEnumerable<WarpPortableSchedulerBarrierLayout>? barriers = null, IEnumerable<uint>? outputs = null) =>
        new(workers, residents, quantum, stackLimit, stepLow, stepHigh, 16,
            [new(0, 0, []), new(4, 22, [0])], barriers ?? [], outputs);

    private static WarpPortableSchedulerBarrierLayout Grid(uint workers, uint site) =>
        new(site, WarpPortableSchedulerLayout.GridScope, Enumerable.Range(0, (int)workers).Select(static worker => (uint)worker));

    private static uint Entry(uint[] arena, uint scheduler, uint worker) =>
        scheduler + arena[scheduler + WarpPortableSchedulerLayout.WorkerStart] + worker * WarpPortableSchedulerLayout.WorkerWords;

    private static uint Collective(uint[] arena, uint scheduler, uint barrier) =>
        scheduler + arena[scheduler + WarpPortableSchedulerLayout.BarrierStart] + barrier * WarpPortableSchedulerLayout.BarrierWords;

    private static void Claim(uint[] arena, uint scheduler) =>
        Assert.AreEqual(0u, Interlocked.CompareExchange(ref arena[scheduler + WarpPortableSchedulerLayout.ControllerOwner], Controller, 0));

    private static (uint Worker, uint Generation) Acquire(uint[] arena, uint scheduler, uint physical)
    {
        Success(WarpPortableSchedulerServices.TryAcquireWorker(arena, scheduler, Controller, physical));
        return (arena[scheduler + WarpPortableSchedulerLayout.Result], arena[scheduler + WarpPortableSchedulerLayout.Result + 1]);
    }

    private static void Success(uint status) => Assert.AreEqual(0u, status);
}
