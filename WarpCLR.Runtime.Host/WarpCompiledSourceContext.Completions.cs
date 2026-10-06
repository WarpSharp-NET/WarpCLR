using WarpCLR.Backend.CoreCLR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourceContext
{
    // This receipt retains one actual completed ordinary source command. It does
    // not issue an executed CIL/call-use origin, entry invocation or Source grant.
    internal sealed class SourceCompletionReceipt
    {
        internal const string Semantics =
            "warp.context-source-completion/private-context-exact-compiler-seal-checkpoint-command-complete-raw-carrier-history-origin-unbound/0.1";
        private readonly WarpCompiledSourceContext context;
        private readonly WarpCompiledWorkerTicket ticket;
        private readonly WarpCoreCLRWorkerLease source;
        private readonly SourceCheckpoint checkpoint;
        private readonly WarpCoreCLRWorkerProcess.WordCheckpoint words;
        private readonly uint[][] arguments;
        private readonly uint[] state;
        private readonly uint[] arena;

        internal static SourceCompletionReceipt? TryCapture(WarpCompiledSourceContext context, WarpCompiledWorkerTicket ticket,
            WarpCoreCLRWorkerLease source, SourceCheckpoint checkpoint, WarpCoreCLRWorkerProcess.WordCheckpoint words, object authority)
        {
            if (context is null || !ReferenceEquals(authority, context.sourceCheckpointAuthority))
            { throw new InvalidOperationException("Only the exact Context may retain its actual source completion."); }
            context.Plan.RequireCompletionCompilerSeal();
            context.ValidateTicket(ticket);
            // Equal-valued outer rebinding was already allowed for ordinary execution.
            // Preserve that behavior; it cannot acquire this stronger object receipt.
            return words.MatchesHistoricalSourceStorage(context.inputs, context.states[ticket.Worker], context.Arena)
                ? new(context, ticket, source, checkpoint, words, authority) : null;
        }

        internal SourceCompletionReceipt(WarpCompiledSourceContext context, WarpCompiledWorkerTicket ticket,
            WarpCoreCLRWorkerLease source, SourceCheckpoint checkpoint, WarpCoreCLRWorkerProcess.WordCheckpoint words, object authority)
        {
            // Check the exact Context secret before any candidate field or bank.
            if (context is null || !ReferenceEquals(authority, context.sourceCheckpointAuthority))
            { throw new InvalidOperationException("Only the exact Context may retain its actual source completion."); }
            context.Plan.RequireCompletionCompilerSeal();
            context.ValidateTicket(ticket);
            state = context.states[ticket.Worker]; arena = context.Arena; arguments = context.inputs;
            source.ValidateCommittedWordCheckpoint(words, state, arena);
            words.ValidateHistoricalSourceStorage(arguments, state, arena);
            this.context = context; this.ticket = ticket; this.source = source; this.checkpoint = checkpoint; this.words = words;
            ReturnedLocation = context.Plan.Location(state);
        }

        // A returned continuation location is data, not proof of the previously
        // executed instruction/call use. Before-body origins remain unissued.
        internal WarpCompiledSourceLocation ReturnedLocation { get; }
        internal ulong Ordinal => words.Ordinal;
        internal ulong Sequence => words.Sequence;
        internal int ProcessId => words.ProcessId;
        internal Guid Module => words.Module;

        internal void ValidateHistorical(WarpCompiledSourceContext exactContext, WarpCompiledWorkerTicket exactTicket,
            WarpCoreCLRWorkerLease exactSource, SourceCheckpoint exactCheckpoint)
        {
            if (!ReferenceEquals(context, exactContext) || !ReferenceEquals(ticket, exactTicket) ||
                !ReferenceEquals(source, exactSource) || !ReferenceEquals(checkpoint, exactCheckpoint) ||
                !ReferenceEquals(checkpoint.Completion, this))
            { throw new InvalidOperationException("A historical completion belongs to one exact Context, ticket, child lease and original SourceCheckpoint."); }
            // Compiler validation reads no caller word banks. Historical storage
            // checks follow; they do not assert a live/current/releasable ticket.
            context.Plan.RequireCompletionCompilerSeal();
            words.ValidateHistoricalSourceStorage(arguments, state, arena);
        }

        internal WarpCoreCLRWorkerWords.Invocation CopyHistoricalRequest()
        {
            ValidateHistorical(context, ticket, source, checkpoint);
            return words.CopyHistoricalRequest();
        }

        internal (uint[] State, uint[] Arena) CopyHistoricalResult()
        {
            ValidateHistorical(context, ticket, source, checkpoint);
            return words.CopyHistoricalResult();
        }
    }
}
