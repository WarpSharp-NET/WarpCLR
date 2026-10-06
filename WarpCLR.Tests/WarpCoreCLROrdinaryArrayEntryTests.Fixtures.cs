using WarpCLR.Tests;
using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCoreCLROrdinaryArrayEntryTests
{
    private static WarpControlFlowKernel NarrowKernel(WarpControlFlowFunction? function = null) => new(
        "ordinary-array-entry/narrow-call", 1, 1,
        [new(0, [], [new(0, WarpIrOpCode.LoadInput), new(1, WarpIrOpCode.LoadScalar), new(2, 0, [0, 1], 1)], new WarpReturnTerminator(2))],
        functions: [function ?? new(0, "ordinary-array-entry/xor", 2,
            [new(0, [], [new(0, WarpIrOpCode.LoadArgument), new(1, WarpIrOpCode.LoadArgument, immediate: 1),
                new(2, WarpIrOpCode.ExclusiveOr, 0, 1)], new WarpReturnTerminator(2))])]);

    private static uint InvokeDirect(CoreCLRJitKernel core, int route, uint[][] inputs, uint[] scalars, CoreCLRExecutionBudget budget) => route switch
    {
        0 => core.Invoke(inputs, scalars, 0, budget),
        1 => core.CompiledEntryPoint.CreateDelegate<Func<uint[][], uint[], int, CoreCLRExecutionBudget, uint>>()(inputs, scalars, 0, budget),
        2 => (uint)core.CompiledEntryPoint.Invoke(null, [inputs, scalars, 0, budget])!,
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    private static void InvokeQuantum(CoreCLRResumableKernel core, int route, uint[][] inputs, uint[] scalars,
        uint[] state, uint[] arena, int quantum, CancellationToken token = default)
    {
        if (route == 0) { core.ExecuteManagedQuantum(inputs, scalars, 0, state, 4, quantum, arena, token); }
        else if (route == 1)
        {
            core.CompiledEntryPoint.CreateDelegate<Action<uint[][], uint[], int, uint[], int, int, CancellationToken, uint[]>>()
                (inputs, scalars, 0, state, 4, quantum, token, arena);
        }
        else if (route == 2) { core.CompiledEntryPoint.Invoke(null, [inputs, scalars, 0, state, 4, quantum, token, arena]); }
        else { throw new ArgumentOutOfRangeException(nameof(route)); }
    }

    private static void AssertDenied(Action invocation, bool reflection)
    {
        if (reflection)
        {
            TargetInvocationException error = Assert.ThrowsExactly<TargetInvocationException>(invocation);
            Assert.IsInstanceOfType<InvalidOperationException>(error.InnerException);
        }
        else { Assert.ThrowsExactly<InvalidOperationException>(invocation); }
    }

    private static uint[] SelectBank(int role, uint[][] inputs, uint[] scalars, uint[] state, uint[] arena) => role switch
    {
        0 => inputs[0],
        1 => scalars,
        2 => state,
        3 => arena,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static WarpLogicalMachineLayout WideLayout(int kind)
    {
        if (kind == 0)
        {
            return new(new("ordinary-array-entry/raw-pair", 2, 1,
                [new(0, [], [new(0, WarpIrOpCode.LoadInput), new(1, WarpIrOpCode.LoadInput, immediate: 1)],
                    new WarpTupleReturnTerminator([0, 1]))]));
        }
        WarpLogicalMachineLayout original = (kind == 1 ? WarpManagedAtomicKernels.Create32() : WarpManagedWideAtomicKernels.Create64())
            .RequireExactlyOne(layout => layout.Kernel.Name.EndsWith("/add", StringComparison.Ordinal));
        // The fixture's scalar role is deliberately admitted even when the operation does not read it.
        return new(new(original.Kernel.Name + "/ordinary-array-scalar-role", original.Kernel.InputBufferCount, 1,
            original.Kernel.Blocks, original.Kernel.Reduction, original.Kernel.Functions, original.Kernel.Execution));
    }

    private static uint[][] WideInputs(int kind) => kind switch
    {
        0 => [[0x80000000], [0x7FC00001]],
        1 => [[0], [1]],
        2 => [[0], [1], [0]],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static WarpLogicalMachineLayout PrivateFixture()
    {
        const string service = "ordinary-array-entry/private-consistency/0.1";
        WarpBasicBlock[] blocks = [new(0, [], [new(0, WarpPrivateControllerOpCode.LoadController), new(1, 0, [0], 1)], new WarpReturnTerminator(1))];
        WarpControlFlowFunction[] functions = [new(0, service, 1,
            [new(0, [], [new(0, WarpIrOpCode.LoadArgument), new(1, WarpIrOpCode.Constant, immediate: 0x80000000),
                new(2, WarpIrOpCode.Equal, 0, 1)], new WarpReturnTerminator(2))])];
        var metadata = new WarpLogicalExecutionMetadata([new(0, false, [1]), new(0, true, [0], countsSourceDepth: false)],
            frameOwners: true, runtimeStateAccess: true, privateControllerProjection: new([new(0, 0, 0, 0, 0, 1, service)]));
        return new(new("ordinary-array-entry/private-consistency", 1, 1, blocks, null, functions, metadata));
    }
}
