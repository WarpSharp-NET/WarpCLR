using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    [TestMethod]
    public void CallerCannotConstructASourcePrewarmingScope() =>
        Assert.ThrowsExactly<InvalidOperationException>(() => new WarpCompiledSourceContext.PrewarmScope(null!, null!, new object()));

    [TestMethod]
    public async Task PrewarmedNormalAndEmergencySourceRolesUseDistinctActualChildrenBeforeAnyClaim()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 1, 1, [[7]]);
        Task<WarpCompiledPrewarmedSourceModules> preparation = WarpCompiledPrewarmedSourceModules.CreateAsync(context, new());
        Assert.IsNull(context.Claim(0));
        WarpCompiledPrewarmedSourceModules modules = await preparation.ConfigureAwait(false);
        await using var owner = modules.ConfigureAwait(false);
        WarpCoreCLRWorkerLease[] roles = [modules.Source, modules.CompareExchange, modules.Census, modules.PublicationRelease, modules.DisposeController];
        Assert.AreEqual(5, roles.Select(role => role.ProcessId).Distinct().Count());
        Assert.AreEqual(5, roles.Select(role => role.CompiledModule).Distinct().Count());
        Assert.AreSame(modules, context.RequirePreparedSourceModules());
        Assert.AreEqual(WarpIrHash.Compute(context.Plan.Layout.Kernel), WarpIrHash.Compute(modules.Source.Layout.Kernel), StringComparer.Ordinal);
        Assert.IsFalse(context.Plan.HasLocalSourceKernel);
        WarpCompiledWorkerTicket ticket = context.Claim(0)!;
        Assert.IsNotNull(ticket);
        await Assert.ThrowsAsync<InvalidOperationException>(() => WarpCompiledPrewarmedSourceModules.CreateAsync(context, new())).ConfigureAwait(false);
        Assert.AreEqual(0u, context.WorkerQuanta(0));
        Assert.IsFalse(context.Plan.HasLocalSourceKernel);
    }
}
