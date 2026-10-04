using System.Collections.Immutable;

namespace WarpCLR.Verifier;

internal sealed class WarpPortableTypedFlowState
{
    public WarpPortableTypedFlowState(WarpPortableTypedValue[] arguments, WarpPortableTypedValue[] locals,
        bool[][] initializedLocals, bool[][] initializedPointees, bool initializedThis)
    {
        Arguments = arguments;
        Locals = locals;
        InitializedLocals = initializedLocals;
        InitializedPointees = initializedPointees;
        InitializedThis = initializedThis;
    }

    public List<WarpPortableTypedValue> Stack { get; } = [];
    public WarpPortableTypedValue[] Arguments { get; }
    public WarpPortableTypedValue[] Locals { get; }
    public bool[][] InitializedLocals { get; }
    public bool[][] InitializedPointees { get; }
    public bool InitializedThis { get; set; }

    public WarpPortableTypedFlowState Clone()
    {
        var clone = new WarpPortableTypedFlowState((WarpPortableTypedValue[])Arguments.Clone(), (WarpPortableTypedValue[])Locals.Clone(),
            InitializedLocals.Select(mask => (bool[])mask.Clone()).ToArray(),
            InitializedPointees.Select(mask => (bool[])mask.Clone()).ToArray(), InitializedThis);
        clone.Stack.AddRange(Stack);
        return clone;
    }

    public ImmutableArray<WarpPortableTypedSlot> LocalSnapshot() => Locals.Select((value, index) =>
        new WarpPortableTypedSlot(value, ImmutableArray.CreateRange(InitializedLocals[index]))).ToImmutableArray();

    public ImmutableArray<WarpPortableTypedSlot> ArgumentSnapshot() => Arguments.Select((value, index) =>
        new WarpPortableTypedSlot(value, ImmutableArray.CreateRange(InitializedPointees[index]))).ToImmutableArray();

    public bool MergeFrom(WarpPortableTypedFlowState incoming, WarpPortableTypedTypeCatalog types, int offset)
    {
        if (Stack.Count != incoming.Stack.Count || InitializedThis != incoming.InitializedThis)
        {
            throw new WarpVerificationException("WRPCLR2201", "A control-flow merge changes stack height or constructor initialization.", offset);
        }

        bool changed = false;
        for (int index = 0; index < Stack.Count; index++)
        {
            WarpPortableTypedValue merged = types.Merge(Stack[index], incoming.Stack[index], offset);
            changed |= !WarpPortableTypedTypeCatalog.Same(Stack[index], merged);
            Stack[index] = merged;
        }

        changed |= MergeStorage(Arguments, incoming.Arguments, InitializedPointees, incoming.InitializedPointees, types, offset);
        changed |= MergeStorage(Locals, incoming.Locals, InitializedLocals, incoming.InitializedLocals, types, offset);
        return changed;
    }

    private static bool MergeStorage(WarpPortableTypedValue[] values, WarpPortableTypedValue[] incoming,
        bool[][] initialized, bool[][] incomingInitialized, WarpPortableTypedTypeCatalog types, int offset)
    {
        bool changed = false;
        for (int index = 0; index < values.Length; index++)
        {
            WarpPortableTypedValue merged = types.Merge(values[index], incoming[index], offset);
            changed |= !WarpPortableTypedTypeCatalog.Same(values[index], merged);
            values[index] = merged;
            for (int part = 0; part < initialized[index].Length; part++)
            {
                bool definite = initialized[index][part] && incomingInitialized[index][part];
                changed |= definite != initialized[index][part];
                initialized[index][part] = definite;
            }
        }

        return changed;
    }
}
