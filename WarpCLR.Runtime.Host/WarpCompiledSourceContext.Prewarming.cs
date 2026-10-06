using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourceContext
{
    private readonly object prewarmAuthority = new();
    private WarpCompiledPrewarmedSourceModules? sourceModules;

    internal WarpCompiledPrewarmedSourceModules RequirePreparedSourceModules()
    {
        lock (executionIdentityGate)
        {
            if (stopped != 0 || sourceModules is null)
            { throw new InvalidOperationException("The exact source context has no successful retained prewarming boundary."); }
            sourceModules.Validate(this);
            return sourceModules;
        }
    }

    internal PrewarmScope BeginSourcePrewarming()
    {
        WarpCompiledControllerGrant grant = controller.TryAcquire()
            ?? throw new InvalidOperationException("Source modules require a separately available startup controller.");
        bool suspended = false;
        try
        {
            lock (executionIdentityGate)
            {
                if (stopped != 0 || sourceModules is not null || remoteCensus is not null || tickets.Any(ticket => ticket is not null) ||
                    helpers.Any(helper => helper is not null) || executionQuanta.Any(quanta => quanta != 0) ||
                    sourceLeases.Any(held => held) || states.Any(state => state[0] != WarpCLR.IR.WarpLogicalMachineLayout.Runnable))
                { throw new InvalidOperationException("Every source and cleanup child must be compiled before the first source claim."); }
                WarpCompiledPausedCensus census = SnapshotPausedCensus(null);
                controller.SuspendForRemote(grant, false);
                suspended = true;
                remoteCensus = census;
                try { return new(this, census, prewarmAuthority); }
                catch { Volatile.Write(ref stopped, 1); throw; }
            }
        }
        finally { if (!suspended) { controller.Release(grant); } }
    }

    internal sealed class PrewarmScope
    {
        private readonly WarpCompiledSourceContext context;
        private readonly WarpCompiledPausedCensus census;
        private readonly byte[] arenaHash;
        private readonly byte[] argumentHash;
        private readonly byte[][] stateHashes;
        private int ended;

        internal PrewarmScope(WarpCompiledSourceContext context, WarpCompiledPausedCensus census, object authority)
        {
            if (context is null || !ReferenceEquals(authority, context.prewarmAuthority))
            { throw new InvalidOperationException("Only the exact runtime context can open its source prewarming scope."); }
            context.RequireRemoteCensus(census);
            this.context = context;
            this.census = census;
            arenaHash = Hash(context.Arena);
            argumentHash = WarpCoreCLRReadOnlyWordIdentity.FromArguments(context.inputs, []);
            stateHashes = context.states.Select(Hash).ToArray();
        }

        internal void Finish(WarpCompiledPrewarmedSourceModules modules)
        {
            lock (context.executionIdentityGate)
            {
                if (ended != 0) { throw new InvalidOperationException("A source prewarming scope cannot be completed twice."); }
                context.RequireRemoteCensus(census);
                modules.Validate(context);
                if (context.stopped != 0 || context.Dispatch != census.Dispatch || context.Epoch != census.Epoch ||
                    !CryptographicOperations.FixedTimeEquals(arenaHash, Hash(context.Arena)) ||
                    !CryptographicOperations.FixedTimeEquals(argumentHash, WarpCoreCLRReadOnlyWordIdentity.FromArguments(context.inputs, [])) ||
                    census.Workers.Any(worker => context.tickets[worker.Worker] is not null || context.helpers[worker.Worker] is not null ||
                        context.executionQuanta[worker.Worker] != 0 || !ReferenceEquals(context.states[worker.Worker], worker.SourceState) ||
                        !CryptographicOperations.FixedTimeEquals(stateHashes[worker.Worker], Hash(worker.SourceState))))
                { throw new InvalidOperationException("The exact unused source context changed while its independent modules were prepared."); }
                context.controller.ResumeAfterRemoteCommit(null);
                context.remoteCensus = null;
                context.sourceModules = modules;
                ended = 1;
            }
        }

        internal void Fail()
        {
            lock (context.executionIdentityGate)
            {
                if (ended != 0) { return; }
                ended = 1;
                context.SuppressUnresolvedRemoteFailure();
            }
        }

        private static byte[] Hash(uint[] words) => SHA256.HashData(MemoryMarshal.AsBytes(words.AsSpan()));
    }
}
