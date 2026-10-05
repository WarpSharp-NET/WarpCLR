using WarpCLR.Backend.CoreCLR;

namespace WarpCLR.Runtime.Host;

public sealed partial class WarpJitCache
{
    private readonly HashSet<WarpCoreCLRWorkerKernel> coreWorkers = [];
    private readonly Dictionary<string, WarpCoreCLRWorkerKernel> coreByKey = new(StringComparer.Ordinal);
    private readonly List<Task> retirements = [];

    private async Task<WarpCoreCLRWorkerKernel> CompileCoreCLRAsync(string key, WarpRuntimeEntry entry, CancellationToken compilationToken)
    {
        byte[] plan = WarpCoreCLRBinaryPlanCodec.Serialize(entry.Kernel);
        ValidateDiskPlan(key, plan);
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(entry.Layout, options.CoreCLR, compilationToken).ConfigureAwait(false);
        lock (sync)
        {
            coreWorkers.Add(compiled); coreByKey.Add(key, compiled); compilationCount++;
            if (shutdownRequested) { RecordRetirement(compiled); }
        }
        compilationToken.ThrowIfCancellationRequested();
        PersistPlan(key, plan);
        return compiled;
    }

    private void RecordRetirement(WarpCoreCLRWorkerKernel kernel)
    {
        retirements.RemoveAll(task => task.IsCompletedSuccessfully);
        retirements.Add(kernel.RetireAsync().AsTask());
    }

    private void AdmitCoreWorker()
    {
        coreWorkers.RemoveWhere(worker => worker.IsClosed);
        int pending = entries.Values.Count(entry => !entry.Compilation.IsCompleted);
        if (coreWorkers.Count + pending >= options.CoreCLR.MaximumWorkerProcesses)
        { throw new WarpHostException("WRPCORECLR3004", "The live CoreCLR worker process admission limit is exhausted."); }
    }
}
