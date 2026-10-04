using System.Collections.Immutable;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private ImmutableArray<WarpPortableTypedRoot> Roots(WarpPortableTypedFlowState state)
    {
        var roots = ImmutableArray.CreateBuilder<WarpPortableTypedRoot>();
        for (int index = 0; index < state.Arguments.Length; index++) { AddRoots(roots, "argument", index, state.Arguments[index], null); }
        for (int index = 0; index < state.Locals.Length; index++) { AddRoots(roots, "local", index, state.Locals[index], state.InitializedLocals[index]); }
        for (int index = 0; index < state.Stack.Count; index++) { AddRoots(roots, "stack", index, state.Stack[index], null); }
        return roots.ToImmutable();
    }

    private void AddRoots(ImmutableArray<WarpPortableTypedRoot>.Builder roots, string storage, int slot,
        WarpPortableTypedValue value, bool[]? initialized)
    {
        if (value.Category == WarpPortableStackCategory.ManagedByref)
        {
            if (!value.Provenance.IsEmpty && (initialized is null || initialized.All(part => part)))
            {
                roots.Add(new(storage, slot, 0, true, value.Provenance));
            }

            return;
        }

        ImmutableArray<int> offsets = value.Category == WarpPortableStackCategory.Reference ? [0] : types.Get(value.TypeIdentity).ManagedRootByteOffsets;
        foreach (int offset in offsets)
        {
            if (initialized is null || Enumerable.Range(offset, 12).All(part => part < initialized.Length && initialized[part]))
            {
                roots.Add(new(storage, slot, offset / 4, false, []));
            }
        }
    }

    private WarpPortableTypedValue Pop(WarpPortableTypedFlowState state, int offset)
    {
        if (state.Stack.Count == 0) { throw Error("Evaluation stack underflow.", offset); }
        WarpPortableTypedValue value = state.Stack[^1]; state.Stack.RemoveAt(state.Stack.Count - 1);
        return value;
    }

    private void Require(bool condition, string message, int offset)
    {
        if (!condition) { throw Error(message, offset); }
    }

    private WarpPortableTypedValue Primitive(Type type) => types.Value(types.Get(type).Identity);

    private WarpPortableTypedValue LoadToStack(WarpPortableTypedValue value)
    {
        WarpPortableTypedType type = types.Get(value.TypeIdentity);
        if (value.Category == WarpPortableStackCategory.I4 && (type.StorageBits < 32 || types.Source(value.TypeIdentity).IsEnum))
        {
            return Primitive(type.StorageBits == 32 && !type.IsSigned ? typeof(uint) : typeof(int)) with { SourceStorageType = type.Identity };
        }

        if (value.Category == WarpPortableStackCategory.I8 && types.Source(value.TypeIdentity).IsEnum)
        {
            return Primitive(type.IsSigned ? typeof(long) : typeof(ulong)) with { SourceStorageType = type.Identity };
        }

        return value;
    }

    private void RequireAssignable(WarpPortableTypedValue value, string destination, int offset)
    {
        Require(!value.IsUninitializedThis && types.Assignable(value, destination),
            $"A value of '{value.TypeIdentity}' cannot be stored/passed as exact managed type '{destination}'.", offset);
    }
}
