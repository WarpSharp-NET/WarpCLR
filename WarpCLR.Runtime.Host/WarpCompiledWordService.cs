using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCompiledWordService
{
    private const int MaximumHelperDepth = 32;
    private readonly Lazy<CoreCLRResumableKernel> kernel;

    internal WarpCompiledWordService(WarpLogicalMachineLayout layout)
    {
        Layout = layout;
        kernel = new(() => CoreCLRResumableKernel.Compile(layout));
    }

    internal WarpLogicalMachineLayout Layout { get; }
    internal string KernelHash => WarpIrHash.Compute(Layout.Kernel);

    internal void Prepare() => _ = kernel.Value;

    internal static WarpCompiledWordService Create(Type implementation, string name)
    {
        MethodInfo method = implementation.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("A trusted generated runtime service is missing: " + name);
        return new(WarpWordArenaServiceLowerer.Lower(method));
    }

    internal WarpCompiledServiceContinuation Start(ReadOnlySpan<uint> arguments)
    {
        if (arguments.Length != Layout.Kernel.InputBufferCount && !(arguments.IsEmpty && Layout.Kernel.InputBufferCount == 1))
        {
            throw new ArgumentException("A compiled service argument shape differs from its admitted ABI.", nameof(arguments));
        }
        uint[][] inputs = new uint[Layout.Kernel.InputBufferCount][];
        for (int index = 0; index < inputs.Length; index++)
        {
            inputs[index] = [arguments.IsEmpty ? 0 : arguments[index]];
        }
        return new(this, inputs, Layout.CreateInitialState(MaximumHelperDepth, 100_000_000));
    }

    internal void Resume(WarpCompiledServiceContinuation continuation, uint[] arena, int quantum)
    {
        if (!ReferenceEquals(continuation.Service, this))
        {
            throw new InvalidOperationException("A service continuation belongs to another compiled artifact.");
        }
        kernel.Value.ExecuteManagedQuantum(continuation.Inputs, [], 0, continuation.State,
            MaximumHelperDepth, Math.Max(Layout.MaximumBlockCost, quantum), arena);
    }
}
