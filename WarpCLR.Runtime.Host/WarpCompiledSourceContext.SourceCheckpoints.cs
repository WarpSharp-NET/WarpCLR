using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourceContext
{
    private readonly object sourceCheckpointAuthority = new();
    private readonly ConditionalWeakTable<uint[], SourceCheckpoint> sourceCheckpoints = new();
    private byte[] sourceArgumentIdentity = [];

    internal SourceCheckpoint RequireCommittedSourceCheckpoint(WarpCompiledWorkerTicket ticket, WarpCoreCLRWorkerLease source)
    {
        lock (executionIdentityGate)
        {
            ValidateTicket(ticket);
            if (!sourceCheckpoints.TryGetValue(states[ticket.Worker], out SourceCheckpoint? checkpoint))
            { throw new InvalidOperationException("This exact source continuation has no runtime-issued committed child checkpoint."); }
            checkpoint.Validate(this, ticket, source);
            return checkpoint;
        }
    }

    private void PublishEntryAllocationArguments(WarpCompiledControllerGrant grant, WarpCompiledWorkerTicket ticket)
    {
        if (!CryptographicOperations.FixedTimeEquals(sourceArgumentIdentity,
            WarpCoreCLRReadOnlyWordIdentity.FromArguments(inputs, [])))
        {
            SuppressUnresolvedRemoteFailure();
            throw new InvalidOperationException("Entry allocation cannot replace changed canonical source argument words.");
        }
        RequireSuccess(Invoke(grant, nameof(WarpPortableSchedulerServices.CaptureServiceResult), [ticket.Worker, ticket.Generation, 1]));
        for (uint word = 0; word < 3; word++)
        {
            inputs[word][ticket.Worker] = Arena[WarpPortableHeapLayout.Result + word];
        }
        sourceArgumentIdentity = WarpCoreCLRReadOnlyWordIdentity.FromArguments(inputs, []);
    }

    internal sealed class SourceCheckpoint
    {
        private readonly WarpCompiledSourceContext context;
        private readonly WarpCompiledWorkerTicket ticket;
        private readonly WarpCoreCLRWorkerLease source;
        private readonly WarpCoreCLRWorkerProcess.WordCheckpoint words;
        private readonly uint[][] arguments;

        internal SourceCheckpoint(WarpCompiledSourceContext context, WarpCompiledWorkerTicket ticket,
            WarpCoreCLRWorkerLease source, WarpCoreCLRWorkerProcess.WordCheckpoint words, object authority)
        {
            if (context is null || !ReferenceEquals(authority, context.sourceCheckpointAuthority))
            { throw new InvalidOperationException("Only the exact runtime source context may issue its committed source checkpoint."); }
            context.ValidateTicket(ticket);
            uint[] state = context.states[ticket.Worker];
            source.ValidateCommittedWordCheckpoint(words, state, context.Arena);
            words.ValidateReadOnlyIdentity(context.sourceArgumentIdentity);
            if (!ticket.Executing || context.remoteCensus is null ||
                !string.Equals(words.IrHash, WarpIrHash.Compute(context.Plan.Layout.Kernel), StringComparison.Ordinal))
            { throw new InvalidOperationException("A source checkpoint requires the executing exact compiler-issued source plan and held census."); }
            this.context = context;
            this.ticket = ticket;
            this.source = source;
            this.words = words;
            arguments = context.inputs;
            words.ValidateReadOnlyIdentity(WarpCoreCLRReadOnlyWordIdentity.FromArguments(arguments, []));
            Dispatch = context.Dispatch;
            Epoch = context.Epoch;
            CompilerIdentity = context.Plan.CompilerIdentity.IdentityHash;
            PlanIdentity = context.Plan.Identity;
            ContextIdentity = context.Identity;
        }

        internal ulong Ordinal => words.Ordinal;
        internal ulong Sequence => words.Sequence;
        internal Guid Module => words.Module;
        internal int ProcessId => words.ProcessId;
        internal uint Dispatch { get; }
        internal uint Epoch { get; }
        internal string CompilerIdentity { get; }
        internal string PlanIdentity { get; }
        internal string ContextIdentity { get; }
        internal string RequestHash => words.RequestHash;
        internal string ResponseHash => words.ResponseHash;

        internal void Validate(WarpCompiledSourceContext exactContext, WarpCompiledWorkerTicket exactTicket, WarpCoreCLRWorkerLease exactSource)
        {
            lock (context.executionIdentityGate)
            {
                if (!ReferenceEquals(context, exactContext) || !ReferenceEquals(ticket, exactTicket) || !ReferenceEquals(source, exactSource) ||
                    !ticket.Executed || context.stopped != 0 || context.remoteCensus is not null || context.Dispatch != Dispatch || context.Epoch != Epoch ||
                    !ReferenceEquals(arguments, context.inputs) ||
                    !context.sourceCheckpoints.TryGetValue(context.states[ticket.Worker], out SourceCheckpoint? current) || !ReferenceEquals(current, this))
                { throw new InvalidOperationException("The source checkpoint is not the current exact context, continuation, child lease and dispatch."); }
                context.ValidateTicket(ticket);
                source.ValidateCommittedWordCheckpoint(words, context.states[ticket.Worker], context.Arena);
                words.ValidateReadOnlyIdentity(WarpCoreCLRReadOnlyWordIdentity.FromArguments(arguments, []));
            }
        }
    }
}
