using System.Buffers.Binary;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableFaultTicketDriver
{
    private void PreparePool()
    {
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcquireServiceLease), uint.MaxValue));
        foreach (WarpPortableSourceFaultPoolResource resource in Program.Pool.Resources)
        { Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.PrepareSourceFaultResource), resource.Id)); }
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.PrepareSourceFaultRecord), 0));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReleaseServiceLease), uint.MaxValue));
    }

    private uint[] AllocateTrace()
    {
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcquireServiceLease), uint.MaxValue));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AllocateArray), Program.Exceptions.TraceType,
            WarpPortableExceptionTraceLayout.HeaderWords + 8 * WarpPortableExceptionTraceLayout.FrameWords * 2));
        uint[] owner = Arena.AsSpan((int)WarpPortableHeapLayout.Result, 3).ToArray();
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), uint.MaxValue));
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReleaseServiceLease), uint.MaxValue));
        return owner;
    }

    private void Activate(uint[][] traces, uint pc)
    {
        Arena[Scheduler + WarpPortableSchedulerLayout.ControllerOwner] = WarpPortableExceptionTestBuilder.Controller;
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.TryAcquireWorker), 0));
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.PublishRoots), 0, 1, 0, 1, 1, 1, pc));
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.AcquireHeapService), 0, 1));
        for (uint index = 0; index < traces.Length; index++)
        { Assert.AreEqual(0u, Exception(nameof(WarpPortableExceptionServices.BindTraceReport), index + 1, traces[index][0], traces[index][1], traces[index][2])); }
        Assert.AreEqual(0u, Exception(nameof(WarpPortableExceptionServices.SealReports)));
    }

    private uint[] CreateParameters()
    {
        uint physical = 2;
        int frame = WarpLogicalMachineLayout.HeaderWords + Program.Layout.FrameWords;
        uint activation = State[frame + WarpLogicalMachineLayout.FrameActivationOffset];
        return [WarpPortableExceptionTestBuilder.Controller, 0, 1, 1, 0, 0, 1,
            0x10293847, 0x56473829, 0xAABBCCDD, 0x91B2D3E4,
            (uint)Program.Row.Function, (uint)Program.Row.Factory.SourceOffset, unchecked((ushort)Program.Row.Factory.OpCode),
            (uint)Program.Row.Factory.EffectIndex, Program.Row.Factory.Id, Program.Row.Factory.FaultDescriptor,
            physical, activation, 1, physical, activation, 1];
    }

    internal void FillGrantedFixture()
    {
        Assert.AreEqual(WarpPortableSourceFaultFactoryLayout.Ready, Arena[Prepared]);
        uint[] fields = [WarpPortableSourceFaultFactoryLayout.PreparedController, WarpPortableSourceFaultFactoryLayout.PreparedWorker,
            WarpPortableSourceFaultFactoryLayout.PreparedRun, WarpPortableSourceFaultFactoryLayout.PreparedDispatch,
            WarpPortableSourceFaultFactoryLayout.PreparedCollectionEpoch, uint.MaxValue, WarpPortableSourceFaultFactoryLayout.PreparedTicketGeneration,
            26, 27, 28, 29, 18, 19, 20, 21, 1, uint.MaxValue, 23, 24, 25, 13, 14, 15];
        for (int index = 0; index < fields.Length; index++) { if (fields[index] != uint.MaxValue) { Arena[Prepared + fields[index]] = Parameters[index]; } }
        Arena[Prepared + WarpPortableSourceFaultFactoryLayout.PreparedOwnerContext] = State[WarpLogicalMachineLayout.OwnerContextOffset];
        uint record = ExceptionRecord;
        Arena[Prepared + WarpPortableSourceFaultFactoryLayout.PreparedExceptionRecord] = Arena[ExceptionWorker + WarpPortableExceptionLayout.PreparedRecord];
        uint root = Arena[record + WarpPortableExceptionLayout.RecordRoot];
        Arena[Prepared + WarpPortableSourceFaultFactoryLayout.PreparedExceptionRoot] = root;
        Arena[Prepared + WarpPortableSourceFaultFactoryLayout.PreparedExceptionRootGeneration] = Arena[Root(root) + WarpPortableHeapLayout.RootGeneration];
        Hash(Pool + WarpPortableSourceFaultFactoryLayout.AdmittedProgramHash, WarpIrHash.Compute(Program.Layout.Kernel));
        Hash(Pool + WarpPortableSourceFaultFactoryLayout.AdmittedExceptionPlanHash, Program.Exceptions.PlanHash);
        Copy(Pool + 48, Prepared + 32); Copy(Pool + 56, Prepared + 40); Copy(Pool + 16, Prepared + 48);
        uint row = Arena[Pool + WarpPortableSourceFaultFactoryLayout.RowStart]; Copy(row + 12, Prepared + 56);
        // This last store is test data only, not a production grant operation.
        Arena[Prepared] = WarpPortableSourceFaultFactoryLayout.Granted;
    }

    private void Copy(uint from, uint to) => Arena.AsSpan((int)from, 8).CopyTo(Arena.AsSpan((int)to, 8));
    private void Hash(uint address, string hash)
    {
        byte[] data = Convert.FromHexString(hash);
        for (int index = 0; index < 8; index++) { Arena[address + (uint)index] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(index * 4)); }
    }
}
