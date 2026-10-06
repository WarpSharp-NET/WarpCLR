using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using WarpCLR.Backend.CoreCLR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerProcess
{
    private static readonly ConditionalWeakTable<uint[], WordCheckpoint> WordCheckpoints = new();

    private static WordCheckpoint PrepareWordCheckpoint(WarpCoreCLRStoppedCommands.Command command,
        uint[] returnedState, uint[] returnedArena, WarpCoreCLRAuthenticatedResponse authenticated, byte[] response, byte[] request,
        uint[][] inputs, uint[][] capturedInputs, uint[] scalars)
    {
        lock (RegistrySync)
        {
            RequireCurrent(command);
            if (command.Process.IsFaulted || command.Process.IsClosed || command.Process.CompiledModule == Guid.Empty || !command.Process.IsCollectible)
            { throw new InvalidOperationException("A stopping or unauthenticated module cannot issue a committed word checkpoint."); }
            return new(command, returnedState, returnedArena, response, RegistryAuthority, request, authenticated, inputs, capturedInputs, scalars);
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

    internal sealed partial class WordCheckpoint
    {
        private readonly WarpCoreCLRStoppedCommands.Command command;
        private readonly byte[] stateDigest;
        private readonly byte[] arenaDigest;
        private readonly byte[] readOnlyIdentity;
        private readonly WarpCoreCLRAuthenticatedResponse authenticated;
        private readonly byte[] responseDigest;

        internal WordCheckpoint(WarpCoreCLRStoppedCommands.Command command, uint[] returnedState,
            uint[] returnedArena, byte[] response, object authority, byte[]? request = null,
            WarpCoreCLRAuthenticatedResponse? authenticatedResponse = null, uint[][]? inputs = null,
            uint[][]? capturedInputs = null, uint[]? scalars = null)
        {
            ValidateRegistryAuthority(authority);
            // Optional syntax preserves the existing wrong-authority-first witness.
            // Valid registry authority still requires the actual authenticated holder.
            ArgumentNullException.ThrowIfNull(authenticatedResponse);
            byte[] verified = authenticatedResponse.RequirePayload(command.Process.key, WarpCoreCLRWorkerProtocol.Executed, command.Sequence);
            if (!CryptographicOperations.FixedTimeEquals(verified, response))
            { throw new InvalidOperationException("The checkpoint response differs from the exact authenticated read payload."); }
            ArgumentNullException.ThrowIfNull(request);
            if (!CryptographicOperations.FixedTimeEquals(command.Digest, SHA256.HashData(request)))
            { throw new InvalidOperationException("The readonly input receipt must bind the exact registered request bytes."); }
            readOnlyIdentity = WarpCoreCLRReadOnlyWordIdentity.FromSerializedRequest(request);
            ArgumentNullException.ThrowIfNull(inputs);
            ArgumentNullException.ThrowIfNull(capturedInputs);
            ArgumentNullException.ThrowIfNull(scalars);
            // Retain the actual admission snapshot, not the possibly rebound outer
            // container. Existing ordinary execution consumes capturedInputs only.
            historicalRequest = request;
            inputContainer = inputs;
            inputBanks = (uint[][])capturedInputs.Clone();
            scalarContainer = scalars;
            this.command = command;
            authenticated = authenticatedResponse;
            responseDigest = SHA256.HashData(verified);
            Module = command.Process.CompiledModule;
            ProcessId = command.Process.ProcessId;
            Ordinal = command.Ordinal;
            Sequence = command.Sequence;
            IrHash = command.IrHash;
            RequestHash = Convert.ToHexString(command.Digest);
            ResponseHash = Convert.ToHexString(responseDigest);
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
            ValidateAuthenticatedResponse();
            if (!CryptographicOperations.FixedTimeEquals(readOnlyIdentity, identity))
            { throw new InvalidOperationException("The source arguments differ from the exact serialized authenticated request words."); }
        }

        internal void ValidateCommand(WarpCoreCLRStoppedCommands.Command exactCommand, object authority)
        {
            ValidateRegistryAuthority(authority);
            if (!ReferenceEquals(command, exactCommand))
            { throw new InvalidOperationException("A word checkpoint belongs to one exact registered authenticated command."); }
            ValidateAuthenticatedResponse();
        }

        internal void ValidateExactCurrent(WarpCoreCLRWorkerProcess process, uint[] state, uint[] arena)
        {
            lock (RegistrySync)
            {
                if (!ReferenceEquals(command.Process, process) || !ReferenceEquals(command.State, state) || !ReferenceEquals(command.Arena, arena) ||
                    !WordCheckpoints.TryGetValue(state, out WordCheckpoint? current) || !ReferenceEquals(current, this) ||
                    RegistryCommands.Values.Any(active => !active.Stopped && (ReferenceEquals(active.State, state) ||
                        arena.Length != 0 && ReferenceEquals(active.Arena, arena))) ||
                    process.IsFaulted || process.IsClosed || process.CompiledModule != Module || process.ProcessId != ProcessId)
                { throw new InvalidOperationException("The checkpoint is not the latest exact live module, transaction and complete word storage."); }
                ValidateAuthenticatedResponse();
                if (!CryptographicOperations.FixedTimeEquals(stateDigest, SHA256.HashData(MemoryMarshal.AsBytes(state.AsSpan()))) ||
                    !CryptographicOperations.FixedTimeEquals(arenaDigest, SHA256.HashData(MemoryMarshal.AsBytes(arena.AsSpan()))))
                { throw new InvalidOperationException("The checkpoint is not the latest exact live module, transaction and complete word storage."); }
            }
        }

        private void ValidateAuthenticatedResponse()
        {
            byte[] verified = authenticated.RequirePayload(command.Process.key, WarpCoreCLRWorkerProtocol.Executed, Sequence);
            if (!CryptographicOperations.FixedTimeEquals(responseDigest, SHA256.HashData(verified)))
            { throw new InvalidOperationException("The checkpoint no longer has its exact authenticated response payload."); }
        }
    }
}
