using System.Collections.Concurrent;
using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed class WarpPortableExceptionTestService
{
    private static readonly ConcurrentDictionary<MethodInfo, Lazy<WarpPortableExceptionTestService>> Cache = new();
    private readonly CoreCLRResumableKernel compiled;
    private readonly WarpLogicalMachineLayout layout;

    private WarpPortableExceptionTestService(MethodInfo method)
    {
        layout = method.GetParameters().Count(parameter => parameter.ParameterType == typeof(uint[])) == 2 ?
            WarpWordStateArenaServiceLowerer.Lower(method, WarpPortableExceptionServices.BankBindings()) : WarpWordArenaServiceLowerer.Lower(method);
        compiled = CoreCLRResumableKernel.Compile(layout);
    }

    internal static uint Run(Type type, string method, uint[] arena, uint[] arguments, int quantum = 4096)
    {
        MethodInfo source = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        WarpPortableExceptionTestService service = Cache.GetOrAdd(source, static source =>
            new(() => new(source), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        return service.Execute(arena, arguments, quantum);
    }

    internal uint Execute(uint[] arena, uint[] arguments, int quantum)
    {
        uint[][] inputs = arguments.Length == 0 ? [new uint[1]] : arguments.Select(value => new[] { value }).ToArray();
        uint[] state = layout.CreateInitialState(128, 100000000);
        for (int iteration = 0; iteration < 1000000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; iteration++)
        {
            compiled.ExecuteManagedQuantum(inputs, [], 0, state, 128, Math.Max(quantum, layout.MaximumBlockCost), arena, CancellationToken.None);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset],
            $"Service fault {state[WarpLogicalMachineLayout.FaultKindOffset]} at {state[WarpLogicalMachineLayout.FaultFunctionOffset]}/{state[WarpLogicalMachineLayout.FaultBlockOffset]}");
        return state[layout.GetResultWordOffset(0, 128)];
    }
}
