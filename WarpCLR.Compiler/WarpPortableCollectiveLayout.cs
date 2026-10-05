namespace WarpCLR.Compiler;

internal static class WarpPortableCollectiveLayout
{
    internal const string Semantics = "warp.collectives.ordered/padded-adjacent-prefix-tree-raw-carry-postorder-fault-transactional/0.1";
    internal const string BindingSemantics = "warp.collectives.binding/scheduler-heap-layout-v2-runtime-owned-roots-parent-schema-before-data-start/0.1";
    internal const string SchedulerSemantics = "warp.scheduler.words/cooperative-all-logical-workers/root-ownership-v2/0.1";
    internal const string HeapSemantics = "warp.heap.words/precise-nonmoving-context/root-ownership-v2/0.1";
    internal const uint Magic = 0x57434F31;
    internal const uint Version = 1;
    internal const uint HeaderWords = 64;
    internal const uint NodeWords = 8;
    internal const uint StateWords = 12;
    internal const uint WorkerWords = 8;
    internal const uint MaximumElements = 65536;
    internal const uint MaximumNodes = 1048576;
    internal const uint MaximumScratchWords = 25165824;

    internal const uint UInt32Type = 1, Int32Type = 2, UInt64Type = 3, Int64Type = 4, Binary32Type = 5, Binary64Type = 6;
    internal const uint Sum = 1, Minimum = 2, Maximum = 3, Product = 4;
    internal const uint Wrapping = 0, Checked = 1;
    internal const uint Reduction = 1, InclusiveScan = 2, ExclusiveScan = 3;
    internal const uint Success = 0, Yield = 1, Invalid = 2, Finished = 3, ArithmeticFault = 4, Cancelled = 5, GenerationExhausted = 6;
    internal const uint Idle = 0, Running = 1, Completed = 2, Faulted = 3, Aborted = 4;
    internal const uint IdentityNode = 0, LeafNode = 1, ArithmeticNode = 2, ExportNode = 3, FaultSelectionNode = 4;

    internal const uint TotalWords = 2, NodeCount = 3, WorkerCount = 4, LevelCount = 5, InputCount = 6, OutputCount = 7;
    internal const uint ElementType = 8, Operation = 9, OverflowMode = 10, Kind = 11;
    internal const uint Nodes = 12, States = 13, Jobs = 14, Levels = 15, Outputs = 16, Members = 17, Workers = 18, Inputs = 19;
    internal const uint ControllerAddress = 20, SchedulerAddress = 21, BoundDispatch = 22, PlanGeneration = 23, RunState = 24;
    internal const uint CurrentLevel = 25, NextJob = 26, ActiveJobs = 27, CancellationPending = 28;
    internal const uint FaultKind = 29, FaultNode = 30, FaultOutput = 31;
    internal const uint IdentityWords = 32, FaultRoot = 40, CommittedGeneration = 41, AcknowledgedGeneration = 42;
    internal const uint InputWords = 43;
    internal const uint HeapVersion = 44, SchedulerVersion = 45, ReservedRootOwnership = 46, BindingPresent = 47, ParentSchemaIdentity = 48;

    internal const uint NodeKind = 0, Left = 1, Right = 2, Start = 3, End = 4, Level = 5;
    internal const uint NodeStatus = 0, ValueLow = 1, ValueHigh = 2, NodeFault = 3, FaultOrigin = 4;
    internal const uint StateGeneration = 5, OwnerWorker = 6, OwnerJob = 7, OriginOutput = 8;
    internal const uint NodePending = 0, NodeRunning = 1, NodeDone = 2;
    internal const uint WorkerState = 0, WorkerNode = 1, WorkerPlan = 2, WorkerJob = 3;
    internal const uint WorkerDispatch = 4, WorkerRun = 5, WorkerLevel = 6;
}
