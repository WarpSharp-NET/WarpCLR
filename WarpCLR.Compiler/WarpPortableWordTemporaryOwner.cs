using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// Exact owner fields to retain through search, then clear on abandonment of
// the enclosing original instruction. The private offset is a three-word ref.
internal sealed record WarpPortableWordTemporaryOwner(int PrivateWordOffset, int RelativeByteOffset,
    string TypeIdentity, ImmutableArray<string> FieldPath, ImmutableArray<WarpPortableTypedProvenance> Provenance);
