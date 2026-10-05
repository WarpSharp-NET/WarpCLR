using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpPortableHeapContext : IDisposable
{
    private readonly Lock gate = new();
    private readonly uint[] arena;
    private readonly Dictionary<string, CoreCLRResumableKernel> services = new(StringComparer.Ordinal);
    private bool disposed;

    public WarpPortableHeapContext(WarpPortableHeapSchema schema, uint payloadWords = 4096, uint maximumObjects = 128,
        uint maximumRoots = 64, uint maximumWorkers = 64, uint quotaWords = 4096)
    {
        ArgumentNullException.ThrowIfNull(schema);
        uint token = WarpLogicalOwnerNamespace.Next();
        arena = schema.CreateArena(token, payloadWords, maximumObjects, maximumRoots, maximumWorkers, quotaWords);
    }

    internal int CompiledServiceCount => services.Count;

    public WarpPortableHeapServiceResult Execute(string serviceName, params uint[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(arguments);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            CoreCLRResumableKernel service = GetService(serviceName);
            int arity = typeof(WarpPortableHeapServices).GetMethod(serviceName, BindingFlags.Public | BindingFlags.Static)!
                .GetParameters().Length - 1;
            if (arguments.Length != arity)
            {
                throw new ArgumentException("The ordinary word service requires its exact scalar argument tuple.", nameof(arguments));
            }
            uint[][] inputs = arguments.Length == 0 ? [[0]] : arguments.Select(value => new[] { value }).ToArray();
            int depth = service.Layout.Kernel.Functions.Count + 1;
            long budget = checked(arena.LongLength * 4096 + 65536);
            uint[] state = service.Layout.CreateInitialState(depth, budget);
            while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
            {
                service.ExecuteManagedQuantum(inputs, [], 0, state, depth, 65536, arena);
            }
            if (state[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Completed)
            {
                throw new InvalidOperationException("A trusted compiled heap service exceeded its admitted word-machine resources.");
            }
            return Snapshot(arena, state[WarpLogicalMachineLayout.ResultOffset]);
        }
    }

    public WarpPortableHeapRootHandle AcquireRoot(WarpPortableHeapReference reference)
    {
        WarpPortableHeapServiceResult result = Execute(nameof(WarpPortableHeapServices.AcquireRoot),
            reference.Context, reference.Slot, reference.Generation, WarpPortableHeapLayout.StrongRoot, 0, 0, 0);
        if (result.Fault != 0)
        {
            throw new InvalidOperationException("A logical strong root could not be acquired.");
        }
        return new WarpPortableHeapRootHandle(() => Execute(nameof(WarpPortableHeapServices.ReadRoot), result.Word0, result.Word1),
            () => ReleaseRoot(result.Word0, result.Word1));
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (!disposed)
            {
                disposed = true;
                services.Clear();
                Array.Clear(arena);
            }
        }
    }

    private CoreCLRResumableKernel GetService(string name)
    {
        if (!services.TryGetValue(name, out CoreCLRResumableKernel? service))
        {
            MethodInfo method = typeof(WarpPortableHeapServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
                ?? throw new ArgumentException("The requested service is outside the closed portable heap runtime.", nameof(name));
            service = CoreCLRResumableKernel.Compile(WarpWordArenaServiceLowerer.Lower(method));
            services.Add(name, service);
        }
        return service;
    }

    private void ReleaseRoot(uint root, uint generation)
    {
        lock (gate)
        {
            if (!disposed)
            {
                Execute(nameof(WarpPortableHeapServices.ReleaseRoot), root, generation);
            }
        }
    }

    private static WarpPortableHeapServiceResult Snapshot(uint[] words, uint fault) => new(fault,
        words[WarpPortableHeapLayout.Operation], words[WarpPortableHeapLayout.Argument0], words[WarpPortableHeapLayout.Argument1],
        words[WarpPortableHeapLayout.Result], words[WarpPortableHeapLayout.Result + 1], words[WarpPortableHeapLayout.Result + 2],
        words[WarpPortableHeapLayout.Result + 3], words[WarpPortableHeapLayout.Result + 4], words[WarpPortableHeapLayout.Result + 5]);
}
