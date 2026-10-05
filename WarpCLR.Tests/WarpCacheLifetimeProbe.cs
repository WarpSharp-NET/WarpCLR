using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal static class WarpCacheLifetimeProbe
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static async Task<int> CaptureCompiledProcessAsync(WarpRuntimeContext context, WarpRuntimeModule module)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(module);
        var cache = (WarpJitCache)typeof(WarpRuntimeContext)
            .GetField("jitCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        WarpCoreCLRWorkerLease compiled = await cache.GetOrCompileAsync(module, entry, CancellationToken.None).ConfigureAwait(false);
        await using var lease = compiled.ConfigureAwait(false);
        Assert.IsTrue(compiled.IsCollectible);
        return compiled.ProcessId;
    }
}
