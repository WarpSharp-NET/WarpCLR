using System.Security.Cryptography;
using WarpCLR.Backend.CoreCLR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerProcess
{
    internal sealed partial class WordCheckpoint
    {
        internal const string HistorySemantics =
            "warp.coreclr.authenticated-word-history/exact-command-session-full-request-response-argument-objects-raw-carrier-data-only/0.1";
        private readonly byte[] historicalRequest;
        private readonly uint[][] inputContainer;
        private readonly uint[][] inputBanks;
        private readonly uint[] scalarContainer;

        // Complete historical bytes, never a claim that these are still the current banks.
        // ReadRequest/ReadResponse return caller-owned arrays; no stored array is exposed.
        internal WarpCoreCLRWorkerWords.Invocation CopyHistoricalRequest()
        {
            _ = RequireHistoricalResponse();
            return WarpCoreCLRWorkerWords.ReadRequest(historicalRequest);
        }

        internal (uint[] State, uint[] Arena) CopyHistoricalResult() =>
            WarpCoreCLRWorkerWords.ReadResponse(RequireHistoricalResponse(), historicalRequest, command.State.Length, command.Arena.Length);

        internal void ValidateHistoricalStorage(uint[][] exactInputs, uint[] exactScalars, uint[] exactState, uint[] exactArena)
        {
            if (!MatchesHistoricalStorage(exactInputs, exactScalars, exactState, exactArena))
            { throw new InvalidOperationException("Historical command storage requires the exact original argument, scalar, state and arena objects."); }
            _ = RequireHistoricalResponse();
        }

        internal void ValidateHistoricalSourceStorage(uint[][] exactInputs, uint[] exactState, uint[] exactArena) =>
            ValidateHistoricalStorage(exactInputs, scalarContainer, exactState, exactArena);

        internal bool MatchesHistoricalSourceStorage(uint[][] exactInputs, uint[] exactState, uint[] exactArena) =>
            MatchesHistoricalStorage(exactInputs, scalarContainer, exactState, exactArena);

        private bool MatchesHistoricalStorage(uint[][] exactInputs, uint[] exactScalars, uint[] exactState, uint[] exactArena) =>
            ReferenceEquals(inputContainer, exactInputs) && ReferenceEquals(scalarContainer, exactScalars) &&
            ReferenceEquals(command.State, exactState) && ReferenceEquals(command.Arena, exactArena) &&
            inputBanks.Length == exactInputs.Length && !inputBanks.Where((bank, index) => !ReferenceEquals(bank, exactInputs[index])).Any();

        private byte[] RequireHistoricalResponse()
        {
            byte[] digest = SHA256.HashData(historicalRequest);
            if (!CryptographicOperations.FixedTimeEquals(command.Digest, digest) ||
                !string.Equals(RequestHash, Convert.ToHexString(digest), StringComparison.Ordinal))
            { throw new InvalidOperationException("The retained historical request no longer matches its exact registered command."); }
            byte[] verified = authenticated.RequirePayload(command.Process.key, WarpCoreCLRWorkerProtocol.Executed, Sequence);
            if (!CryptographicOperations.FixedTimeEquals(responseDigest, SHA256.HashData(verified)))
            { throw new InvalidOperationException("Historical words require the original authenticated session, sequence and complete response."); }
            return verified;
        }
    }
}
