namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private void RequirePointer(WarpPortableTypedValue value, string? element, int offset)
    {
        Require(value.Category == WarpPortableStackCategory.ManagedByref && !value.Provenance.IsEmpty,
            "An indirect operation requires a initialized, provenance-bound managed byref.", offset);
        if (element is not null)
        {
            Require(string.Equals(types.Get(value.TypeIdentity).ElementType, element, StringComparison.Ordinal),
                "The managed-byref element type disagrees with the indirect instruction.", offset);
        }
    }

    private void RequireWritable(WarpPortableTypedValue pointer, int offset)
    {
        RequirePointer(pointer, null, offset);
        Require(!pointer.IsReadOnly, "A readonly managed byref is used to mutate storage or passed to a mutable callee.", offset);
    }

    private void RequireInitialized(WarpPortableTypedValue pointer, WarpPortableTypedFlowState state, int offset)
    {
        RequirePointer(pointer, null, offset);
        foreach (WarpPortableTypedProvenance origin in pointer.Provenance)
        {
            bool[]? initialized = !string.Equals(origin.OwnerMethod, method.Identity, StringComparison.Ordinal) ? null : origin.Kind == WarpPortableProvenanceKind.FrameLocal ? state.InitializedLocals[origin.OwnerIndex] :
                origin.Kind == WarpPortableProvenanceKind.Argument ? state.InitializedPointees[origin.OwnerIndex] : null;
            if (initialized is null) { continue; }
            Require(origin.ByteOffset >= 0 && origin.ByteLength > 0 && (long)origin.ByteOffset + origin.ByteLength <= initialized.Length,
                "A byref range exceeds its owner-frame/argument storage.", offset);
            Require(Enumerable.Range(origin.ByteOffset, origin.ByteLength).All(part => initialized[part]),
                "Storage is read through a managed byref before definite initialization.", offset);
        }
    }

    private void WriteBorrow(WarpPortableTypedValue pointer, WarpPortableTypedFlowState state, int offset)
    {
        RequireWritable(pointer, offset);
        foreach (WarpPortableTypedProvenance origin in pointer.Provenance)
        {
            if (string.Equals(origin.OwnerMethod, method.Identity, StringComparison.Ordinal) && origin.Kind == WarpPortableProvenanceKind.FrameLocal) { state.Locals[origin.OwnerIndex] = state.Locals[origin.OwnerIndex] with { IsNull = false }; }
            if (string.Equals(origin.OwnerMethod, method.Identity, StringComparison.Ordinal) && origin.Kind == WarpPortableProvenanceKind.FrameArgument) { state.Arguments[origin.OwnerIndex] = state.Arguments[origin.OwnerIndex] with { IsNull = false }; }
            bool[]? initialized = !string.Equals(origin.OwnerMethod, method.Identity, StringComparison.Ordinal) ? null : origin.Kind == WarpPortableProvenanceKind.FrameLocal ? state.InitializedLocals[origin.OwnerIndex] :
                origin.Kind == WarpPortableProvenanceKind.Argument ? state.InitializedPointees[origin.OwnerIndex] : null;
            if (initialized is not null)
            {
                Require(origin.ByteOffset >= 0 && origin.ByteLength > 0 && (long)origin.ByteOffset + origin.ByteLength <= initialized.Length,
                    "A byref write exceeds the lifetime owner's storage bounds.", offset);
                if (pointer.Provenance.Length == 1) { System.Array.Fill(initialized, true, origin.ByteOffset, origin.ByteLength); }
            }
        }
    }
}
