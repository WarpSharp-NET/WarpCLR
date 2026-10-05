using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Security.Cryptography;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableExceptionSourceBinding
{
    internal const string OwnershipSemantics = "warp.exception.source-instruction-ownership/exact-captured-site-effects-full-typed-instruction-private-nonnull-domain-required-raw-utf16-snapshot/0.2";
    internal ImmutableArray<WarpPortableExceptionInstructionOwnership> InstructionOwnership { get; }
    internal string InstructionOwnershipHash { get; }

    internal bool OwnsInstruction(string methodIdentity, WarpPortableTypedInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        WarpPortableExceptionInstructionOwnership? row = InstructionOwnership.FirstOrDefault(row =>
            string.Equals(row.MethodIdentity, methodIdentity, StringComparison.Ordinal) && row.SourceOffset == instruction.Offset &&
            row.SourceOpCode == unchecked((ushort)instruction.OpCode));
        return row is not null && string.Equals(row.InstructionHash, InstructionHash(instruction), StringComparison.Ordinal);
    }

    private static ImmutableArray<WarpPortableExceptionInstructionOwnership> Ownership(WarpPortableMethodGraph graph, WarpPortableTypedProgram program)
    {
        bool filters = graph.Methods.Any(method => method.ExceptionRegions.Any(region => region.Kind == 1));
        return program.Methods.OrderBy(method => method.Identity, StringComparer.Ordinal).SelectMany(method =>
            method.Instructions.Where(instruction => instruction.Reachable && OwnedOpcode(instruction.OpCode, filters))
            .OrderBy(instruction => instruction.Offset).Select(instruction => new WarpPortableExceptionInstructionOwnership(
                method.Identity, instruction.Offset, unchecked((ushort)instruction.OpCode), instruction.Effects,
                instruction.ExceptionMemberships, InstructionHash(instruction),
                instruction.OpCode == OpCodes.Throw.Value && !instruction.EntryStack.IsEmpty ? instruction.EntryStack[^1] : null,
                instruction.OpCode == OpCodes.Throw.Value))).ToImmutableArray();
    }

    private static bool OwnedOpcode(short opcode, bool filters) => opcode == OpCodes.Throw.Value || opcode == OpCodes.Rethrow.Value ||
        opcode == OpCodes.Leave.Value || opcode == OpCodes.Leave_S.Value || opcode == OpCodes.Endfinally.Value ||
        opcode == OpCodes.Endfilter.Value || opcode == OpCodes.Ret.Value || filters && opcode == OpCodes.Isinst.Value;

    private static string OwnershipHash(WarpPortableMethodGraph graph, WarpPortableTypedProgram program, string schemaHash) =>
        Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(new
        {
            OwnershipSemantics, graph.GraphHash, program.VerifiedHash, SchemaHash = schemaHash,
            Rows = Ownership(graph, program),
            RequiredAuthority = "private-invocation-validation-of-exact-prepared-nonnull-throw-owner-generations;compilation-mints-no-grant",
        })));

    private static string InstructionHash(WarpPortableTypedInstruction instruction) =>
        Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(instruction)));
}
