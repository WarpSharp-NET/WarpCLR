using WarpCLR.Verifier;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableExceptionPlan
{
    internal uint[] Attach(uint[] arena, uint maximumFrames, uint recordsPerWorker, uint reportsPerWorker)
    {
        ArgumentNullException.ThrowIfNull(arena);
        ArgumentOutOfRangeException.ThrowIfZero(maximumFrames); ArgumentOutOfRangeException.ThrowIfZero(recordsPerWorker);
        ArgumentOutOfRangeException.ThrowIfZero(reportsPerWorker);
        if (arena.Length < WarpPortableHeapLayout.HeaderWords || arena[0] != WarpPortableHeapLayout.Magic ||
            arena[1] != WarpPortableHeapLayout.Version || arena[WarpPortableExceptionLayout.Descriptor] != 0 ||
            arena[WarpPortableHeapLayout.UsedWords] != 0 || arena[WarpPortableHeapLayout.LiveObjects] != 0 ||
            arena[WarpPortableHeapLayout.LeaseState] != 0 || arena[WarpPortableHeapLayout.CollectionState] != 0)
        {
            throw Invalid("EH metadata/root reservation must precede allocation and dispatch in an idle empty context.");
        }
        uint memory = arena[WarpPortableSourceMemoryLayout.Descriptor];
        if (memory < WarpPortableHeapLayout.HeaderWords || memory > arena.Length - WarpPortableSourceMemoryLayout.HeaderWords ||
            !string.Equals(ReadHash(arena, memory + WarpPortableSourceMemoryLayout.Hash), TypeSchemaHash, StringComparison.Ordinal))
        {
            throw Invalid("EH records require the exact admitted source exception/type schema.");
        }
        uint workerCount = arena[WarpPortableHeapLayout.WorkerCount];
        uint rootCount = checked(workerCount * checked(recordsPerWorker + reportsPerWorker) * 2);
        if (workerCount == 0 || rootCount > arena[WarpPortableHeapLayout.RootCount]) { throw Invalid("The context has insufficient precise EH/report root rows."); }
        uint firstRoot = RootReservation(arena, rootCount);
        uint start = arena[WarpPortableHeapLayout.DataStart];
        uint workers = checked((uint)metadata.Length);
        uint records = checked(workers + workerCount * WarpPortableExceptionLayout.WorkerWords);
        uint frames = checked(records + workerCount * recordsPerWorker * WarpPortableExceptionLayout.RecordWords);
        uint reports = checked(frames + workerCount * recordsPerWorker * maximumFrames * WarpPortableExceptionLayout.FrameWords);
        uint generations = checked(reports + workerCount * reportsPerWorker * WarpPortableExceptionLayout.ReportWords);
        uint end = checked(generations + arena[WarpPortableHeapLayout.SlotCount]);
        WarpCLR.IR.WarpCompilationAdmission.Require(GraphHash, WarpCLR.IR.WarpCompilationResourceKind.VerifierWorkspaceSlots,
            end, WarpCLR.IR.WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
        uint[] attached = new uint[checked(arena.Length + (int)end)];
        arena.AsSpan(0, (int)start).CopyTo(attached); arena.AsSpan((int)start).CopyTo(attached.AsSpan(checked((int)(start + end))));
        metadata.AsSpan().CopyTo(attached.AsSpan((int)start));
        attached[3] = (uint)attached.Length; attached[WarpPortableHeapLayout.DataStart] = checked(start + end);
        attached[WarpPortableExceptionLayout.Descriptor] = start;
        uint scheduler = attached[WarpPortableSchedulerLayout.HeapDescriptor];
        if (scheduler != 0) { attached[scheduler + WarpPortableSchedulerLayout.ArenaWords] = (uint)attached.Length; }
        WriteDimensions(attached, start, end, workerCount, workers, records, frames, reports,
            maximumFrames, recordsPerWorker, reportsPerWorker, firstRoot, rootCount);
        attached[start + WarpPortableExceptionLayout.TraceGenerations] = generations;
        attached[start + WarpPortableExceptionLayout.TraceGenerationCount] = arena[WarpPortableHeapLayout.SlotCount];
        ReserveRoots(attached, start, firstRoot, rootCount);
        WriteHashes(attached, start, maximumFrames, recordsPerWorker, reportsPerWorker, firstRoot);
        return attached;
    }

    private static uint RootReservation(uint[] arena, uint count)
    {
        uint first = checked(arena[WarpPortableHeapLayout.RootCount] - count + 1);
        for (uint root = first; root < first + count; root++)
        {
            uint row = arena[WarpPortableHeapLayout.RootStart] + (root - 1) * WarpPortableHeapLayout.RootWords;
            if (arena[row + WarpPortableHeapLayout.RootState] != WarpPortableHeapLayout.Free ||
                arena[row + WarpPortableHeapLayout.RootOwnership] != WarpPortableHeapLayout.UnownedRoot)
            {
                throw Invalid("EH reservation cannot steal scheduler/host root ownership.");
            }
        }
        return first;
    }

    private void WriteDimensions(uint[] arena, uint descriptor, uint end, uint workerCount, uint workers, uint records,
        uint frames, uint reports, uint maximumFrames, uint recordCount, uint reportCount, uint firstRoot, uint roots)
    {
        arena[descriptor + WarpPortableExceptionLayout.End] = end;
        arena[descriptor + WarpPortableExceptionLayout.WorkerCount] = workerCount;
        arena[descriptor + WarpPortableExceptionLayout.WorkerStart] = workers;
        arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker] = recordCount;
        arena[descriptor + WarpPortableExceptionLayout.RecordStart] = records;
        arena[descriptor + WarpPortableExceptionLayout.MaximumFrames] = maximumFrames;
        arena[descriptor + WarpPortableExceptionLayout.FrameStart] = frames;
        arena[descriptor + WarpPortableExceptionLayout.ReportsPerWorker] = reportCount;
        arena[descriptor + WarpPortableExceptionLayout.ReportStart] = reports;
        arena[descriptor + WarpPortableExceptionLayout.RootFirst] = firstRoot;
        arena[descriptor + WarpPortableExceptionLayout.RootCount] = roots;
        arena[descriptor + WarpPortableExceptionLayout.ExceptionType] = ExceptionType;
        arena[descriptor + WarpPortableExceptionLayout.TraceArrayType] = TraceType;
        arena[descriptor + WarpPortableExceptionLayout.StackOverflowType] = StackOverflowType;
        arena[descriptor + WarpPortableExceptionLayout.StateStride] = (uint)FrameWords;
        arena[descriptor + WarpPortableExceptionLayout.PrivateOffset] = (uint)PrivateOffset;
        arena[descriptor + WarpPortableExceptionLayout.NodeCount] = (uint)NodeCount;
    }

    private static void ReserveRoots(uint[] arena, uint descriptor, uint first, uint count)
    {
        for (uint root = first; root < first + count; root++)
        {
            uint row = arena[WarpPortableHeapLayout.RootStart] + (root - 1) * WarpPortableHeapLayout.RootWords;
            arena[row + WarpPortableHeapLayout.RootState] = WarpPortableHeapLayout.Allocated;
            arena[row + WarpPortableHeapLayout.RootOwnership] = WarpPortableHeapLayout.RuntimeOwnedRoot;
            arena[row + WarpPortableHeapLayout.RootKind] = WarpPortableHeapLayout.StrongRoot;
        }
        arena[WarpPortableHeapLayout.LiveRoots] = checked(arena[WarpPortableHeapLayout.LiveRoots] + count);
        uint workers = arena[descriptor + WarpPortableExceptionLayout.WorkerCount];
        uint records = arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker];
        uint reports = arena[descriptor + WarpPortableExceptionLayout.ReportsPerWorker];
        for (uint worker = 0; worker < workers; worker++)
        {
            uint roots = first + worker * (records + reports) * 2;
            for (uint record = 0; record < records; record++)
            {
                uint row = descriptor + arena[descriptor + WarpPortableExceptionLayout.RecordStart] + (worker * records + record) * WarpPortableExceptionLayout.RecordWords;
                arena[row + WarpPortableExceptionLayout.RecordRoot] = roots + record * 2;
            }
            for (uint report = 0; report < reports; report++)
            {
                uint row = descriptor + arena[descriptor + WarpPortableExceptionLayout.ReportStart] + (worker * reports + report) * WarpPortableExceptionLayout.ReportWords;
                arena[row + WarpPortableExceptionLayout.ReportRoot] = roots + (records + report) * 2;
            }
        }
    }

    private void WriteHashes(uint[] arena, uint descriptor, uint maximumFrames, uint recordCount, uint reportCount, uint firstRoot)
    {
        WriteHash(arena, descriptor + WarpPortableExceptionLayout.PlanHash, PlanHash);
        WriteHash(arena, descriptor + WarpPortableExceptionLayout.SchemaHash, TypeSchemaHash);
        WriteHash(arena, descriptor + WarpPortableExceptionLayout.GraphHash, GraphHash);
        WriteHash(arena, descriptor + WarpPortableExceptionLayout.VerifiedHash, VerifiedHash);
        WriteHash(arena, descriptor + WarpPortableExceptionLayout.LayoutHash, LayoutHash);
        WriteHash(arena, descriptor + WarpPortableExceptionLayout.MapsHash, MapsHash);
        WriteHash(arena, descriptor + WarpPortableExceptionLayout.TraceProjectionHash, TraceProjectionHash);
        string attachment = Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(new
        {
            PlanHash, Context = arena[WarpPortableHeapLayout.Context], Workers = arena[WarpPortableHeapLayout.WorkerCount],
            MaximumFrames = maximumFrames, Records = recordCount, Reports = reportCount, FirstRoot = firstRoot,
            Descriptor = descriptor, End = arena[descriptor + WarpPortableExceptionLayout.End],
            Memory = arena[WarpPortableSourceMemoryLayout.Descriptor], Scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor],
            RootOwnership = WarpPortableHeapLayout.RuntimeOwnedRoot,
        })));
        WriteHash(arena, descriptor + 88, attachment);
    }

    internal static string ReadHash(uint[] arena, uint start)
    {
        byte[] bytes = new byte[32];
        for (int word = 0; word < 8; word++) { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(word * 4), arena[start + (uint)word]); }
        return Convert.ToHexString(bytes);
    }

    private static void WriteHash(uint[] arena, uint start, string hash)
    {
        byte[] bytes = Convert.FromHexString(hash);
        for (int word = 0; word < 8; word++) { arena[start + (uint)word] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(word * 4)); }
    }
}
