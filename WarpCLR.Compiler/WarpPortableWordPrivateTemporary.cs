using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// Compiler-only fixed storage. It is never a source local or CLI sizeof authority.
internal sealed record WarpPortableWordPrivateTemporary(int Index, int SourceOffset, int WordOffset,
    WarpPortableTypedType Type, string ConstructorIdentity, ImmutableArray<WarpPortableWordTemporaryOwner> Owners)
{
    internal const string Semantics = "warp.source-constructor-private-storage/exact-newobj-type-offset-field-owner-projection-null-roots-value-without-heap-allocation/0.1";
}
