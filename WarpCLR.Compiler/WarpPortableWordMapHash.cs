using WarpCLR.Verifier;
using System.Security.Cryptography;

namespace WarpCLR.Compiler;

internal static class WarpPortableWordMapHash
{
    internal const string Version = "warp.portable-word-roots/declared-null-private-constructor-storage-live-stack-ssa-owner-effects-original-alias-prefix-root-invocation-prelude-raw-utf16-snapshot/0.8";

    internal static string Compute(IEnumerable<WarpPortableWordBody> bodies, WarpPortableWordEntryProjection entry)
    {
        byte[] payload = WarpPortableSnapshotIdentity.Serialize(new
        {
            Version,
            Effects = WarpPortableSourceOperationMetadata.Version,
            Bodies = bodies.Select(body => new
            {
                body.Function, body.MethodIdentity, body.EvaluationWordOffset, body.MaximumStackWords, body.PrivateWordCount,
                body.AliasOwnerFunction, body.AliasPrefixWords,
                body.PrivateTemporaries,
                body.InvocationPrelude,
                Arguments = body.Arguments.Select(argument => new { argument.Index, argument.WordOffset, argument.Type.Identity, argument.Type.WordCount }),
                Locals = body.Locals.Select(local => new { local.Index, local.WordOffset, local.Type.Identity, local.Type.WordCount }),
                Blocks = body.SourceBlocks.Select(block => new { block.Block, block.GeneratedBlocks, block.Instruction.Offset, block.Roots, block.ReturnedRoots, block.Operation }),
            }),
            Entry = entry,
        });
        return Convert.ToHexString(SHA256.HashData(payload));
    }
}
