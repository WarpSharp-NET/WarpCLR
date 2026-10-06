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
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(entry.Layout, options.CoreCLR,
            compilationToken, coreProbes).ConfigureAwait(false);
        await PauseCorePublicationAsync(compiled).ConfigureAwait(false);
        Task? lateRetirement = null;
        lock (sync)
        {
            coreWorkers.Add(compiled); coreByKey.Add(key, compiled); compilationCount++;
            if (shutdownRequested) { lateRetirement = RecordRetirementAsync(compiled); }
        }
        // Shutdown already captured this compilation task. Keep the late child
        // owned by that task until its original retirement outcome is terminal.
        if (lateRetirement is not null) { await lateRetirement.ConfigureAwait(false); }
        compilationToken.ThrowIfCancellationRequested();
        PersistPlan(key, plan);
        return compiled;
    }

    private async Task PauseCorePublicationAsync(WarpCoreCLRWorkerKernel compiled)
    {
        if (beforeCorePublication is null) { return; }
        try { await beforeCorePublication(compiled).ConfigureAwait(false); }
        catch (Exception publicationError)
        {
            try { await compiled.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanupError) { throw new AggregateException(publicationError, cleanupError); }
            throw;
        }
    }

    private Task RecordRetirementAsync(WarpCoreCLRWorkerKernel kernel)
    {
        retirements.RemoveAll(task => task.IsCompletedSuccessfully);
        Task retirement = kernel.RetireAsync().AsTask();
        retirements.Add(retirement);
        return retirement;
    }

    private void AdmitCoreWorker()
    {
        coreWorkers.RemoveWhere(worker => worker.IsClosed);
        int pending = entries.Values.Count(entry => !entry.Compilation.IsCompleted);
        if (coreWorkers.Count + pending >= options.CoreCLR.MaximumWorkerProcesses)
        { throw new WarpHostException("WRPCORECLR3004", "The live CoreCLR worker process admission limit is exhausted."); }
    }
}
