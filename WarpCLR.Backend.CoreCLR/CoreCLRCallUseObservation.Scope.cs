using System.Security.Cryptography;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public static partial class CoreCLRCallUseObservation
{
    internal sealed class Scope : IDisposable
    {
        private readonly WarpCoreCLRWorkerProtocol.Frame requestFrame;
        private readonly byte[] sessionKey;
        private readonly ulong sequence;
        private readonly byte[] request;
        private readonly List<WarpCoreCLRWorkerCallObservations.CallUse> calls = [];
        private readonly int thread = Environment.CurrentManagedThreadId;
        private bool completed;
        private bool disposed;

        internal Scope(object issuer, Emission emission, WarpCoreCLRWorkerProtocol.Frame frame, byte[] key, ulong sequence, byte[] request)
        {
            if (!ReferenceEquals(issuer, ScopeIssuer))
            { throw new InvalidOperationException("Only the private authenticated observation issuer may create a scope."); }
            Emission = emission; requestFrame = frame; sessionKey = key; this.sequence = sequence; this.request = request;
            Invocation = WarpCoreCLRWorkerWords.ReadRequest(request);
            // Reserve the finite response envelope before invoking any guest code.
            WarpCoreCLRWorkerCallObservations.RequireResponseCapacity(Invocation.State.Length, Invocation.Arena.Length);
        }

        internal Emission Emission { get; }
        internal WarpCoreCLRWorkerWords.Invocation Invocation { get; }

        internal void Record(Site site, uint[][] inputs, uint[] scalars, int worker, uint[] state, uint[] arena, int frame)
        {
            RequireActive();
            if (completed || !ReferenceEquals(scalars, Invocation.Scalars) || !ReferenceEquals(state, Invocation.State) ||
                !ReferenceEquals(arena, Invocation.Arena) || worker != Invocation.Worker || inputs.Length != Invocation.Inputs.Length)
            { throw new InvalidOperationException("The actual emitted invocation differs from the authenticated decoded command."); }
            for (int index = 0; index < inputs.Length; index++)
            {
                if (!ReferenceEquals(inputs[index], Invocation.Inputs[index]))
                { throw new InvalidOperationException("An observed call substituted an original input bank."); }
            }
            if (calls.Count >= WarpCoreCLRWorkerCallObservations.MaximumCalls)
            { throw new InvalidDataException("The finite authenticated call-observation capacity is exhausted."); }
            calls.Add(Capture(site, state, frame));
        }

        private static WarpCoreCLRWorkerCallObservations.CallUse Capture(Site site, uint[] state, int frame)
        {
            WarpLogicalMachineLayout layout = site.Owner.Layout;
            WarpLogicalMachineNode node = site.Node;
            uint depth = state[WarpLogicalMachineLayout.DepthOffset];
            if (depth == 0 || frame != checked(WarpLogicalMachineLayout.HeaderWords + (checked((int)depth) - 1) * layout.FrameWords) ||
                state[frame + WarpLogicalMachineLayout.FrameFunctionOffset] != (uint)node.Function ||
                state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset] != (uint)node.ProgramCounter)
            { throw new InvalidOperationException("The actual observed call frame differs from its private emitted site."); }
            return new(node.Function, node.ProgramCounter, node.Call!.Value.Callee + 1, node.Continuation, frame, depth,
                state[frame + WarpLogicalMachineLayout.FrameActivationOffset],
                depth == 1 ? 0 : state[frame - layout.FrameWords + WarpLogicalMachineLayout.FrameActivationOffset]);
        }

        internal byte[] Complete()
        {
            RequireActive();
            if (completed) { throw new InvalidOperationException("A call-observation scope can complete only once."); }
            byte[] verified = WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(requestFrame, sessionKey,
                WarpCoreCLRWorkerCallObservations.ExecuteObserved, sequence);
            if (!CryptographicOperations.FixedTimeEquals(verified, request))
            { throw new InvalidDataException("The complete authenticated observation request changed during execution."); }
            completed = true;
            return WarpCoreCLRWorkerCallObservations.Response(request, Invocation.State, Invocation.Arena,
                Emission.Method.Module.ModuleVersionId, calls);
        }

        private void RequireActive()
        {
            if (disposed || Environment.CurrentManagedThreadId != thread || !ReferenceEquals(current, this))
            { throw new InvalidOperationException("The authenticated observation must remain on its original synchronous Worker thread."); }
        }

        public void Dispose()
        {
            if (disposed) { return; }
            RequireActive();
            current = null; disposed = true;
        }
    }
}
