using System.Security.Cryptography;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.CoreCLR.Worker;

internal static partial class Program
{
    private static async Task ExecuteBoundCommandAsync(Stream output, byte[] key, ulong sequence,
        WarpCoreCLRWorkerProtocol.Frame frame, CoreCLRResumableKernel kernel, InputBindings bindings)
    {
        if (frame.Kind == WarpCoreCLRWorkerProtocol.BindInputs)
        {
            WarpCoreCLRWorkerInputWords.Binding binding = WarpCoreCLRWorkerInputWords.Read(frame.Payload);
            if (binding.Tag <= bindings.LastTag || bindings.Items.Count >= WarpCoreCLRWorkerInputWords.MaximumBindings ||
                bindings.Bytes + binding.Bytes > WarpCoreCLRWorkerWords.MaximumBytes ||
                binding.Inputs.Length != kernel.Layout.Kernel.InputBufferCount || binding.Scalars.Length != kernel.Layout.Kernel.ScalarArgumentCount)
            { throw new InvalidDataException("Immutable input binding changed its admitted shape, namespace, or aggregate capacity."); }
            bindings.Items.Add(binding.Tag, binding); bindings.LastTag = binding.Tag; bindings.Bytes += binding.Bytes;
            await WarpCoreCLRWorkerProtocol.WriteAsync(output, key, WarpCoreCLRWorkerProtocol.InputsBound, sequence,
                WarpCoreCLRWorkerInputWords.Reference(binding.Tag, binding.Identity), CancellationToken.None).ConfigureAwait(false);
            return;
        }
        WarpCoreCLRWorkerInputWords.Binding admitted = WarpCoreCLRWorkerInputWords.Resolve(frame.Payload, bindings.Items);
        if (frame.Kind == WarpCoreCLRWorkerProtocol.UnbindInputs && frame.Payload.Length == 36)
        {
            bindings.Items.Remove(admitted.Tag); bindings.Bytes -= admitted.Bytes;
            await WarpCoreCLRWorkerProtocol.WriteAsync(output, key, WarpCoreCLRWorkerProtocol.InputsUnbound, sequence,
                frame.Payload, CancellationToken.None).ConfigureAwait(false);
            return;
        }
        if (frame.Kind != WarpCoreCLRWorkerProtocol.ExecuteBatch || kernel.Layout.Kernel.Execution is not null || kernel.Layout.RequiresManagedMemory)
        { throw new InvalidDataException("Batched execution requires an admitted pure legacy map without managed runtime capabilities."); }
        WarpCoreCLRWorkerBatchWords.Invocation call = WarpCoreCLRWorkerBatchWords.ReadRequest(frame.Payload);
        for (int slot = 0; slot < call.States.Length; slot++)
        {
            uint[] state = call.States[slot];
            if (state.Length < WarpLogicalMachineLayout.HeaderWords || state[WarpLogicalMachineLayout.SourceBoundaryModeOffset] != 0)
            { throw new InvalidDataException("Batched execution cannot bypass a managed source boundary."); }
            while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
            { kernel.ExecuteManagedQuantum(admitted.Inputs, admitted.Scalars, checked(call.InputBase + slot), state, call.Depth, call.Quantum, [], CancellationToken.None); }
            if (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Faulted) { break; }
        }
        await WarpCoreCLRWorkerProtocol.WriteAsync(output, key, WarpCoreCLRWorkerProtocol.BatchExecuted, sequence,
            WarpCoreCLRWorkerBatchWords.Response(frame.Payload, call.States), CancellationToken.None).ConfigureAwait(false);
    }

    private sealed class InputBindings
    {
        internal Dictionary<uint, WarpCoreCLRWorkerInputWords.Binding> Items { get; } = [];
        internal int Bytes { get; set; }
        internal uint LastTag { get; set; }
    }
}
