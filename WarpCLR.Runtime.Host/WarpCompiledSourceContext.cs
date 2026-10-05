using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourceContext
{
    private readonly WarpCompiledController controller;
    private readonly WarpCompiledWorkerTicket?[] tickets;
    private readonly WarpCompiledServiceContinuation?[] helpers;
    private readonly uint[] allocations;
    private readonly uint[] executionQuanta;
    private readonly uint[][] states;
    private readonly WarpCompiledWordService mirror;
    private readonly WarpCompiledSourceFault?[] faults;
    private readonly bool[] sourceLeases;
    private readonly bool[] sourceLoanStarted;
    private uint[][] inputs;
    private readonly Lock executionIdentityGate = new();
    private int stopped;
    private WarpCompiledPausedCensus? remoteCensus;

    internal WarpCompiledSourceContext(WarpCompiledSourcePlan plan, uint[] unusedHeap, uint[][] arguments)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(unusedHeap);
        Plan = plan;
        ValidateCanonicalUnusedHeap(unusedHeap);
        inputs = ValidateArguments(arguments);
        Arena = plan.Schema.AttachToEmptyHeap(unusedHeap);
        Scheduler = Arena[WarpPortableSchedulerLayout.HeapDescriptor];
        controller = new(Arena, Scheduler);
        tickets = new WarpCompiledWorkerTicket?[plan.Workers];
        helpers = new WarpCompiledServiceContinuation?[plan.Workers];
        allocations = new uint[plan.Workers];
        executionQuanta = new uint[plan.Workers];
        states = Enumerable.Range(0, checked((int)plan.Workers)).Select(_ => InitialState()).ToArray();
        mirror = plan.Services.Mirror;
        faults = new WarpCompiledSourceFault?[plan.Workers];
        sourceLeases = new bool[plan.Workers];
        sourceLoanStarted = new bool[plan.Workers];
        string schema = string.Join(',', Arena.AsSpan(checked((int)(Scheduler + WarpPortableSchedulerLayout.SchemaHash)), 8).ToArray());
        SchedulerHash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(Arena.AsSpan(
            checked((int)(Scheduler + WarpPortableSchedulerLayout.SchemaHash)), 8))));
        string heapSchema = string.Join(',', Arena.AsSpan(checked((int)WarpPortableHeapLayout.SchemaHash), 8).ToArray());
        string canonical = string.Join('\n', plan.Identity, schema, heapSchema, Arena.Length.ToString(CultureInfo.InvariantCulture),
            Arena[WarpPortableHeapLayout.Context].ToString(CultureInfo.InvariantCulture));
        Identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        sourceArgumentIdentity = WarpCoreCLRReadOnlyWordIdentity.FromArguments(inputs, []);
    }

    internal WarpCompiledSourcePlan Plan { get; }
    internal uint[] Arena { get; }
    internal string Identity { get; }
    internal string SchedulerHash { get; }
    internal uint Scheduler { get; }
    internal uint State => Header(WarpPortableSchedulerLayout.ContextState);
    internal uint Dispatch => Header(WarpPortableSchedulerLayout.DispatchGeneration);
    internal uint Epoch => Header(WarpPortableSchedulerLayout.GCEpoch);
    internal uint FailedControllerClaims => controller.FailedClaims;
    internal uint CooperativeHeapRetries { get; private set; }
    internal WarpCompiledController Controller => controller;

    internal uint WorkerState(uint worker) => Arena[Worker(worker) + WarpPortableSchedulerLayout.WorkerState];
    internal uint WorkerQuanta(uint worker) => executionQuanta[worker];
    internal ReadOnlySpan<uint> MachineState(uint worker) => states[worker];
    internal bool HasRuntimeHelper(WarpCompiledWorkerTicket ticket)
    {
        ValidateTicket(ticket);
        return helpers[ticket.Worker] is not null;
    }

    internal WarpCompiledWorkerTicket? Claim(uint physical)
    {
        if (Volatile.Read(ref stopped) != 0) { return null; }
        WarpCompiledControllerGrant? grant = controller.TryAcquire();
        if (grant is null) { return null; }
        try
        {
            uint status = Invoke(grant, nameof(WarpPortableSchedulerServices.TryAcquireWorker), [physical]);
            if (status != 0) { return null; }
            uint worker = Header(WarpPortableSchedulerLayout.Result);
            uint generation = Header(WarpPortableSchedulerLayout.Result + 1);
            var ticket = new WarpCompiledWorkerTicket(this, worker, physical, generation, Dispatch);
            if (tickets[worker] is not null) { throw new InvalidOperationException("A logical worker already owns a physical continuation."); }
            tickets[worker] = ticket;
            if (allocations[worker] != 0 && helpers[worker] is null)
            {
                status = Invoke(grant, nameof(WarpPortableSchedulerServices.AcquireHeapService), [worker, generation]);
                if (status != 0)
                {
                    ReleaseTicket(ticket);
                    return null;
                }
                helpers[worker] = Plan.Services.Heap(nameof(WarpPortableHeapServices.AllocateObject)).Start([allocations[worker]]);
            }
            else if (!sourceLeases[worker] && states[worker][WarpCompiledSourceBoundary.StateOffset] == WarpCompiledSourceBoundary.BeforeSource &&
                Plan.RequiresSourceLoan(states[worker]))
            {
                status = Invoke(grant, nameof(WarpPortableSchedulerServices.AcquireHeapService), [worker, generation]);
                if (status != 0) { ReleaseTicket(ticket); return null; }
                sourceLeases[worker] = true;
                sourceLoanStarted[worker] = false;
            }
            return ticket;
        }
        finally { controller.Release(grant); }
    }

    internal void Execute(WarpCompiledWorkerTicket ticket)
    {
        lock (executionIdentityGate)
        {
            if (stopped != 0 || remoteCensus is not null) { throw new InvalidOperationException("The exact context execution domain is stopped or remotely owned."); }
            ValidateTicket(ticket);
            ticket.BeginExecution();
        }
        if (helpers[ticket.Worker] is { } helper)
        {
            helper.Resume(Arena, helper.Service.Layout.MaximumBlockCost);
        }
        else
        {
            uint[] state = states[ticket.Worker];
            bool ordinary = State == WarpPortableSchedulerLayout.Active && Header(WarpPortableSchedulerLayout.GCState) == WarpPortableSchedulerLayout.GCIdle;
            bool owned = sourceLeases[ticket.Worker] && sourceLoanStarted[ticket.Worker] &&
                state[WarpCompiledSourceBoundary.StateOffset] != WarpCompiledSourceBoundary.BeforeSource;
            if (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && (ordinary || owned))
            {
                if (state[WarpCompiledSourceBoundary.StateOffset] == WarpCompiledSourceBoundary.BeforeSource)
                {
                    WarpCompiledSourceBoundary.Acknowledge(state);
                    sourceLoanStarted[ticket.Worker] = sourceLeases[ticket.Worker];
                }
                Plan.Kernel.ExecuteManagedQuantum(inputs, [], checked((int)ticket.Worker), state,
                    Plan.MaximumDepth, Plan.Quantum, Arena);
            }
        }
        executionQuanta[ticket.Worker] = checked(executionQuanta[ticket.Worker] + 1);
        ticket.FinishExecution();
    }

    internal uint Commit(WarpCompiledWorkerTicket ticket)
    {
        ValidateTicket(ticket);
        if (!ticket.Executed) { throw new InvalidOperationException("The physical continuation has no compiled outcome to commit."); }
        WarpCompiledControllerGrant? grant = controller.TryAcquire();
        if (grant is null) { return WarpPortableSchedulerLayout.Yield; }
        try
        {
            uint status = helpers[ticket.Worker] is { } helper
                ? CommitHelper(grant, ticket, helper)
                : CommitSource(grant, ticket);
            ReleaseTicket(ticket);
            return status;
        }
        finally { controller.Release(grant); }
    }

    internal uint Advance(uint physical)
    {
        WarpCompiledWorkerTicket? retained = tickets.FirstOrDefault(ticket => ticket is not null && ticket.Physical == physical);
        if (retained is not null)
        {
            if (!retained.Executed) { Execute(retained); }
            return Commit(retained);
        }
        uint worker = Header(WarpPortableSchedulerLayout.ServiceOwner);
        if (Header(WarpPortableSchedulerLayout.GCState) == WarpPortableSchedulerLayout.GCRequested &&
            worker == WarpPortableSchedulerLayout.NoWorker)
        {
            return AdvanceCollection();
        }
        WarpCompiledWorkerTicket? ticket = Claim(physical);
        if (ticket is null) { return WarpPortableSchedulerLayout.Yield; }
        Execute(ticket);
        return Commit(ticket);
    }

    internal void QueueEntryAllocation(uint worker, uint type)
    {
        if (worker >= Plan.Workers || type == 0 || allocations[worker] != 0 || executionQuanta[worker] != 0 ||
            WorkerState(worker) != WarpPortableSchedulerLayout.Ready || Plan.InputRootWords.Length != 1 ||
            Plan.InputRootWords[0] != 0 || Plan.Program.Bodies[0].Arguments[0].Type.Category != WarpCLR.Verifier.WarpPortableStackCategory.Reference)
        {
            throw new InvalidOperationException("Entry allocation requires one exact, unused three-word source reference binding.");
        }
        uint destination = Plan.TypeSchema.TypeId(Plan.Program.Bodies[0].Arguments[0].Type.Identity);
        if (!Plan.TypeSchema.Types.Any(layout => layout.Id == type && layout.Kind == WarpPortableHeapLayout.Class &&
            layout.AssignableTo.Contains(destination)))
        {
            throw new InvalidOperationException("Entry allocation must use an exact captured class assignable to the original source argument.");
        }
        allocations[worker] = type;
    }

    private void ValidateCanonicalUnusedHeap(uint[] unusedHeap)
    {
        if (unusedHeap.Length < WarpPortableHeapLayout.HeaderWords ||
            unusedHeap[WarpPortableHeapLayout.DataStart] > unusedHeap.Length ||
            unusedHeap[WarpPortableHeapLayout.SlotCount] > unusedHeap.Length / WarpPortableHeapLayout.SlotWords ||
            unusedHeap[WarpPortableHeapLayout.RootCount] > unusedHeap.Length / WarpPortableHeapLayout.RootWords ||
            unusedHeap[WarpPortableHeapLayout.WorkerCount] != Plan.Workers)
        {
            throw new ArgumentException("The unused source heap has an invalid bounded shape.", nameof(unusedHeap));
        }
        uint[] expected = Plan.CreateUnusedHeap(unusedHeap[WarpPortableHeapLayout.Context],
            checked((uint)unusedHeap.Length - unusedHeap[WarpPortableHeapLayout.DataStart]),
            unusedHeap[WarpPortableHeapLayout.SlotCount], unusedHeap[WarpPortableHeapLayout.RootCount],
            unusedHeap[WarpPortableHeapLayout.AllocationQuota]);
        if (!unusedHeap.AsSpan().SequenceEqual(expected))
        {
            throw new ArgumentException("The unused heap must exactly match the canonical captured source schema and memory views.", nameof(unusedHeap));
        }
    }

    internal uint RequestCancellation() => Control(nameof(WarpPortableSchedulerServices.RequestCancellation));
    internal uint RequestCollection() => Control(nameof(WarpPortableSchedulerServices.RequestCollection));
    internal uint RequestDisposal() => Control(nameof(WarpPortableSchedulerServices.RequestDisposal));
    internal uint FinishDisposal() => Control(nameof(WarpPortableSchedulerServices.FinishDisposal));
    internal uint ReleaseOutput(uint worker, uint dispatch) => Control(nameof(WarpPortableSchedulerServices.ReleaseOutputRoots), [worker, dispatch]);

    internal uint BeginDispatch(uint[][] arguments)
    {
        uint[][] nextInputs = ValidateArguments(arguments);
        uint status = Control(nameof(WarpPortableSchedulerServices.BeginDispatch));
        if (status != 0) { return status; }
        ResetDispatchSources(nextInputs, CreateDispatchSources());
        return 0;
    }

    private uint[][] CreateDispatchSources() => Enumerable.Range(0, checked((int)Plan.Workers)).Select(_ => InitialState()).ToArray();

    private void ResetDispatchSources(uint[][] nextInputs, uint[][] nextStates)
    {
        inputs = nextInputs;
        sourceArgumentIdentity = WarpCoreCLRReadOnlyWordIdentity.FromArguments(inputs, []);
        for (uint worker = 0; worker < Plan.Workers; worker++)
        {
            if (tickets[worker] is not null || helpers[worker] is not null) { throw new InvalidOperationException("Redispatch retained an unresolved runtime continuation."); }
            states[worker] = nextStates[worker];
            allocations[worker] = 0;
            executionQuanta[worker] = 0;
            faults[worker] = null;
            sourceLeases[worker] = false;
            sourceLoanStarted[worker] = false;
        }
    }

    internal uint[] Result(uint worker, uint dispatch)
    {
        if (Volatile.Read(ref stopped) != 0 || dispatch != Dispatch || State != WarpPortableSchedulerLayout.CompletedContext ||
            Header(WarpPortableSchedulerLayout.OutputQuarantined) != 0 || WorkerState(worker) != WarpPortableSchedulerLayout.Completed)
        {
            throw new InvalidOperationException("Only the owning successfully completed dispatch can expose its typed word result.");
        }
        return Enumerable.Range(0, Plan.Layout.ResultWordCount)
            .Select(word => states[worker][Plan.Layout.GetResultWordOffset(word, Plan.MaximumDepth)]).ToArray();
    }

    internal WarpCompiledSourceFault? EscapedFault()
    {
        uint winner = Header(WarpPortableSchedulerLayout.FaultWinner);
        return winner == WarpPortableSchedulerLayout.NoWorker ? null : faults[winner];
    }

    private uint[] InitialState()
    {
        uint[] state = Plan.Layout.CreateInitialState(Plan.MaximumDepth, Plan.MaximumSteps);
        WarpCompiledSourceBoundary.Configure(Plan.Layout, state);
        return state;
    }

    private uint[][] ValidateArguments(uint[][] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length != Plan.Layout.Kernel.InputBufferCount || arguments.Any(words => words is null || words.Length != Plan.Workers))
        {
            throw new ArgumentException("Source bindings must exactly cover every admitted logical worker and word argument.", nameof(arguments));
        }
        return arguments.Select(words => (uint[])words.Clone()).ToArray();
    }

    private uint Invoke(WarpCompiledControllerGrant grant, string service, ReadOnlySpan<uint> arguments) =>
        Plan.Services.Invoke(service, Arena, Scheduler, grant, arguments, Plan.Quantum);

    private uint Control(string service, ReadOnlySpan<uint> arguments = default)
    {
        WarpCompiledControllerGrant? grant = controller.TryAcquire();
        if (grant is null) { return WarpPortableSchedulerLayout.Yield; }
        try { return Invoke(grant, service, arguments); }
        finally { controller.Release(grant); }
    }

    private uint Header(uint offset) => Arena[checked(Scheduler + offset)];

    private uint Worker(uint worker)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(worker, Plan.Workers);
        return checked(Scheduler + Header(WarpPortableSchedulerLayout.WorkerStart) + worker * WarpPortableSchedulerLayout.WorkerWords);
    }

    private void ValidateTicket(WarpCompiledWorkerTicket ticket)
    {
        if (!ReferenceEquals(ticket.Context, this) || ticket.Worker >= Plan.Workers ||
            !ReferenceEquals(tickets[ticket.Worker], ticket) || ticket.Released || ticket.Dispatch != Dispatch ||
            Arena[Worker(ticket.Worker) + WarpPortableSchedulerLayout.RunGeneration] != ticket.Generation ||
            WorkerState(ticket.Worker) != WarpPortableSchedulerLayout.Running)
        {
            throw new InvalidOperationException("The physical source continuation is stale or belongs to another context/dispatch.");
        }
    }

    private void ReleaseTicket(WarpCompiledWorkerTicket ticket)
    {
        ticket.Released = true;
        tickets[ticket.Worker] = null;
    }
}
