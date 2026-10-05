using System.Collections.Concurrent;
using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableCollectiveDriver
{
    private static readonly ConcurrentDictionary<string, Machine> Machines = new(StringComparer.Ordinal);
    private static long generatedCalls, generatedQuanta, simultaneousJobs;
    private readonly bool generated, minimumQuantum;
    private readonly int residents;
    private uint workerCursor, run = 1;

    internal WarpPortableCollectiveDriver(WarpPortableCollectivePlan plan, ulong[] input, bool generated, bool minimumQuantum, int residents)
    {
        Plan = plan; this.generated = generated; this.minimumQuantum = minimumQuantum; this.residents = residents;
        uint[] words = new uint[input.Length * 2];
        for (int i = 0; i < input.Length; i++) { words[i * 2] = (uint)input[i]; words[i * 2 + 1] = (uint)(input[i] >> 32); }
        Arena = plan.CreateArena(words);
    }

    internal WarpPortableCollectivePlan Plan { get; }
    internal uint[] Arena { get; }
    internal uint Descriptor => Plan.DescriptorOffset;
    internal uint Generation => Arena[Descriptor + WarpPortableCollectiveLayout.PlanGeneration];
    internal static long GeneratedCalls => Interlocked.Read(ref generatedCalls);
    internal static long GeneratedQuanta => Interlocked.Read(ref generatedQuanta);
    internal static long SimultaneousJobs => Interlocked.Read(ref simultaneousJobs);

    internal uint Control(string method, params uint[] arguments)
    {
        uint address = Arena[Descriptor + WarpPortableCollectiveLayout.ControllerAddress];
        Assert.AreEqual(0u, Interlocked.CompareExchange(ref Arena[address], 1u, 0u));
        try { return Execute(method, [Descriptor, 1, .. arguments]); }
        finally { Assert.AreEqual(1u, Interlocked.Exchange(ref Arena[address], 0u)); }
    }

    internal uint Execute(string method, uint[] arguments)
    {
        if (!generated)
        {
            MethodInfo source = typeof(WarpPortableCollectiveServices).GetMethod(method, BindingFlags.Public | BindingFlags.Static)!;
            object[] values = new object[arguments.Length + 1];
            values[0] = Arena;
            for (int i = 0; i < arguments.Length; i++) { values[i + 1] = arguments[i]; }
            return (uint)source.Invoke(null, values)!;
        }
        Invocation invocation = CreateInvocation(method, arguments);
        while (invocation.State[0] == WarpLogicalMachineLayout.Runnable) { Tick(invocation); }
        return Result(invocation);
    }

    private static Machine GetMachine(string method) => Machines.GetOrAdd(method, static name =>
    {
        MethodInfo source = typeof(WarpPortableCollectiveServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
        WarpLogicalMachineLayout layout = WarpPortableCollectiveKernels.Lower(source);
        return new(layout, CoreCLRResumableKernel.Compile(layout));
    });

    private static Invocation CreateInvocation(string method, uint[] arguments)
    {
        Machine machine = GetMachine(method);
        uint[][] inputs = new uint[arguments.Length][];
        for (int i = 0; i < arguments.Length; i++) { inputs[i] = [arguments[i]]; }
        Interlocked.Increment(ref generatedCalls);
        return new(machine, inputs, machine.Layout.CreateInitialState(16, 100000000));
    }

    private void Tick(Invocation invocation)
    {
        int quantum = minimumQuantum ? invocation.Machine.Layout.MaximumBlockCost : 1000000;
        invocation.Machine.Core.ExecuteManagedQuantum(invocation.Inputs, [], 0, invocation.State, 16, quantum, Arena);
        Interlocked.Increment(ref generatedQuanta);
    }

    private static uint Result(Invocation invocation)
    {
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, invocation.State[0]);
        return invocation.State[WarpLogicalMachineLayout.ResultOffset];
    }

    private sealed record Machine(WarpLogicalMachineLayout Layout, CoreCLRResumableKernel Core);
    private sealed record Invocation(Machine Machine, uint[][] Inputs, uint[] State);
}
