using System.Collections.ObjectModel;

namespace WarpCLR.Compiler;

internal sealed class WarpPortableSchedulerBarrierLayout
{
    private readonly ReadOnlyCollection<uint> members;

    public WarpPortableSchedulerBarrierLayout(uint site, uint scope, IEnumerable<uint> workers)
    {
        ArgumentOutOfRangeException.ThrowIfZero(site);
        ArgumentOutOfRangeException.ThrowIfLessThan(scope, WarpPortableSchedulerLayout.GridScope);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scope, WarpPortableSchedulerLayout.SubgroupScope);
        ArgumentNullException.ThrowIfNull(workers);
        uint[] copy = workers.ToArray();
        ArgumentOutOfRangeException.ThrowIfZero(copy.Length, nameof(workers));
        members = Array.AsReadOnly(copy);
        Site = site;
        Scope = scope;
    }

    public uint Site { get; }

    public uint Scope { get; }

    public ReadOnlyCollection<uint> Members => members;
}
