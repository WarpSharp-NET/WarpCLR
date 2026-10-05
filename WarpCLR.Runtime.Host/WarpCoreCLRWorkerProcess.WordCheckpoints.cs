using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerProcess
{
    private static readonly ConditionalWeakTable<uint[], WordCheckpoint> WordCheckpoints = new();

    private static WordCheckpoint PrepareWordCheckpoint(WarpCoreCLRStoppedCommands.Command command,
        uint[] returnedState, uint[] returnedArena, byte[] response, byte[] request)
    {
        lock (RegistrySync)
        {
            RequireCurrent(command);
            if (command.Process.IsFaulted || command.Process.IsClosed || command.Process.CompiledModule == Guid.Empty || !command.Process.IsCollectible)
            { throw new InvalidOperationException("A stopping or unauthenticated module cannot issue a committed word checkpoint."); }
            return new(command, returnedState, returnedArena, response, RegistryAuthority, request);
        }
    }

    private static void PublishWordCheckpoint(WarpCoreCLRStoppedCommands.Command command, WordCheckpoint checkpoint)
    {
        lock (RegistrySync)
        {
            checkpoint.ValidateCommand(command, RegistryAuthority);
            WordCheckpoints.Remove(command.State);
            WordCheckpoints.Add(command.State, checkpoint);
        }
    }

    internal WordCheckpoint RequireCommittedWordCheckpoint(uint[] state, uint[] arena)
    {
        lock (RegistrySync)
        {
            if (!WordCheckpoints.TryGetValue(state, out WordCheckpoint? checkpoint))
            { throw new InvalidOperationException("No runtime-issued committed checkpoint exists for this exact state."); }
            checkpoint.ValidateExactCurrent(this, state, arena);
            return checkpoint;
        }
    }

    internal void ValidateCommittedWordCheckpoint(WordCheckpoint checkpoint, uint[] state, uint[] arena) =>
        checkpoint.ValidateExactCurrent(this, state, arena);

    internal sealed class WordCheckpoint
    {
        private readonly WarpCoreCLRStoppedCommands.Command command;
        private readonly byte[] stateDigest;
        private readonly byte[] arenaDigest;
        private readonly byte[] readOnlyIdentity;

        internal WordCheckpoint(WarpCoreCLRStoppedCommands.Command command, uint[] returnedState,
            uint[] returnedArena, byte[] response, object authority, byte[]? request = null)
        {
            ValidateRegistryAuthority(authority);
            ArgumentNullException.ThrowIfNull(request);
            if (!CryptographicOperations.FixedTimeEquals(command.Digest, SHA256.HashData(request)))
            { throw new InvalidOperationException("The readonly input receipt must bind the exact registered request bytes."); }
            readOnlyIdentity = WarpCoreCLRReadOnlyWordIdentity.FromSerializedRequest(request);
            this.command = command;
            Module = command.Process.CompiledModule;
            ProcessId = command.Process.ProcessId;
            Ordinal = command.Ordinal;
            Sequence = command.Sequence;
            IrHash = command.IrHash;
            RequestHash = Convert.ToHexString(command.Digest);
            ResponseHash = Convert.ToHexString(SHA256.HashData(response));
            stateDigest = SHA256.HashData(MemoryMarshal.AsBytes(returnedState.AsSpan()));
            arenaDigest = SHA256.HashData(MemoryMarshal.AsBytes(returnedArena.AsSpan()));
        }

        internal ulong Ordinal { get; }
        internal ulong Sequence { get; }
        internal Guid Module { get; }
        internal int ProcessId { get; }
        internal string IrHash { get; }
        internal string RequestHash { get; }
        internal string ResponseHash { get; }

        internal void ValidateReadOnlyIdentity(byte[] identity)
        {
            if (!CryptographicOperations.FixedTimeEquals(readOnlyIdentity, identity))
            { throw new InvalidOperationException("The source arguments differ from the exact serialized authenticated request words."); }
        }

        internal void ValidateCommand(WarpCoreCLRStoppedCommands.Command exactCommand, object authority)
        {
            ValidateRegistryAuthority(authority);
            if (!ReferenceEquals(command, exactCommand))
            { throw new InvalidOperationException("A word checkpoint belongs to one exact registered authenticated command."); }
        }

        internal void ValidateExactCurrent(WarpCoreCLRWorkerProcess process, uint[] state, uint[] arena)
        {
            lock (RegistrySync)
            {
                if (!ReferenceEquals(command.Process, process) || !ReferenceEquals(command.State, state) || !ReferenceEquals(command.Arena, arena) ||
                    !WordCheckpoints.TryGetValue(state, out WordCheckpoint? current) || !ReferenceEquals(current, this) ||
                    RegistryCommands.Values.Any(active => !active.Stopped && (ReferenceEquals(active.State, state) ||
                        arena.Length != 0 && ReferenceEquals(active.Arena, arena))) ||
                    process.IsFaulted || process.IsClosed || process.CompiledModule != Module || process.ProcessId != ProcessId ||
                    !CryptographicOperations.FixedTimeEquals(stateDigest, SHA256.HashData(MemoryMarshal.AsBytes(state.AsSpan()))) ||
                    !CryptographicOperations.FixedTimeEquals(arenaDigest, SHA256.HashData(MemoryMarshal.AsBytes(arena.AsSpan()))))
                { throw new InvalidOperationException("The checkpoint is not the latest exact live module, transaction and complete word storage."); }
            }
        }
    }
}
