using System.Reflection;
using System.Runtime.CompilerServices;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal static class WarpCacheLifetimeProbe
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static async Task<WeakReference> CaptureCompiledAssemblyAsync(WarpRuntimeContext context, WarpRuntimeModule module)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(module);
        var cache = (WarpJitCache)typeof(WarpRuntimeContext)
            .GetField("jitCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        CoreCLRResumableKernel compiled = await cache.GetOrCompileAsync(module, entry, CancellationToken.None).ConfigureAwait(false);
        Assert.IsTrue(compiled.IsCollectible);
        return new WeakReference(compiled.CompiledEntryPoint.Module.Assembly);
    }
}
