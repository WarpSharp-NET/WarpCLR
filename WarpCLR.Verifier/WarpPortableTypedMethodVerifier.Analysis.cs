using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private WarpPortableTypedFlowState InitialState()
    {
        WarpPortableTypedValue[] arguments = argumentTypes.Select(types.Value).ToArray();
        var pointees = new bool[arguments.Length][];
        bool constructor = method.SourceMethod is ConstructorInfo && !method.SourceMethod.IsStatic;
        ParameterInfo[] parameters = method.SourceMethod.GetParameters();
        for (int index = 0; index < arguments.Length; index++)
        {
            WarpPortableTypedValue argument = arguments[index];
            if (argument.Category != WarpPortableStackCategory.ManagedByref) { pointees[index] = []; continue; }
            string element = types.Get(argument.TypeIdentity).ElementType!;
            int parameter = index - (method.SourceMethod.IsStatic ? 0 : 1);
            bool output = parameter >= 0 ? parameters[parameter].IsOut && !parameters[parameter].IsIn : constructor;
            bool readOnly = parameter >= 0 ? ReadOnlyParameter(parameters[parameter]) : !constructor &&
                (method.SourceMethod.GetCustomAttributesData().Any(attribute => attribute.AttributeType.FullName is "System.Runtime.CompilerServices.IsReadOnlyAttribute") ||
                    method.SourceMethod.DeclaringType!.GetCustomAttributesData().Any(attribute => attribute.AttributeType.FullName is "System.Runtime.CompilerServices.IsReadOnlyAttribute"));
            arguments[index] = argument with { Provenance = [new(WarpPortableProvenanceKind.Argument, method.Identity, index,
                element, 0, types.Get(element).ByteSize)], IsReadOnly = readOnly };
            pointees[index] = Initialization(types.Get(element), !output);
        }

        bool initializedThis = !constructor || method.SourceMethod.DeclaringType!.IsValueType;
        if (!initializedThis) { arguments[0] = arguments[0] with { IsUninitializedThis = true }; }
        WarpPortableTypedValue[] locals = method.LocalTypes.Select(identity => types.Value(identity) with
            { IsNull = method.InitializeLocals && types.Get(identity).Category == WarpPortableStackCategory.Reference }).ToArray();
        bool[][] masks = method.LocalTypes.Select(identity => Initialization(types.Get(identity), method.InitializeLocals)).ToArray();
        return new(arguments, locals, masks, pointees, initializedThis);
    }

    private static bool[] Initialization(WarpPortableTypedType type, bool initialized)
    {
        var result = new bool[type.ByteSize];
        System.Array.Fill(result, initialized);
        if (!initialized && type.Category == WarpPortableStackCategory.Value && !type.Fields.IsEmpty)
        {
            for (int part = 0; part < result.Length; part++)
            {
                result[part] = !type.Fields.Any(field => !field.IsStatic && part >= field.ByteOffset && part < field.ByteOffset + field.ByteSize);
            }
        }

        return result;
    }

    private void Analyze(int index)
    {
        WarpPortableTypedFlowState before = states[index]!;
        WarpPortableTypedFlowState after = before.Clone();
        WarpPortableMethodGraphInstruction source = method.Instructions[index];
        var step = new Step(source.Offset, prefixes.GetValueOrDefault(index) ?? new Prefix());
        Evaluate(index, after, step);
        if (after.Stack.Count > method.MaximumStack) { throw Error("Evaluation stack exceeds declared maxstack.", source.Offset); }
        maximumStackWords = Math.Max(maximumStackWords, Math.Max(before.Stack.Sum(value => value.WordCount), after.Stack.Sum(value => value.WordCount)));
        var exceptional = ImmutableArray.CreateBuilder<int>();
        if (step.Effects.Any(MayThrow)) { PropagateExceptions(index, before, exceptional); }
        if (source.OpCode == OpCodes.Endfilter) { PropagateFilter(index, after, exceptional); }
        foreach (int successor in successors[index])
        {
            if (memberships[successor].Any(member => !memberships[index].Contains(member) && member.Role == WarpPortableExceptionRole.Try) &&
                after.Stack.Count != 0) { throw Error("A fallthrough enters a try region with a nonempty stack.", source.Offset); }
            Enqueue(successor, after);
        }

        instructions[index] = new(source.Offset, source.NextOffset, source.OpCode.Value, true,
            before.Stack.ToImmutableArray(), after.Stack.ToImmutableArray(), before.ArgumentSnapshot(), before.LocalSnapshot(),
            memberships[index], successors[index].Select(successor => method.Instructions[successor].Offset).ToImmutableArray(), exceptional.ToImmutable(),
            unwind[index], step.Effects.ToImmutableArray(), Faults(source, step), Roots(before), step.MemoryType, step.StorageBits, step.Prefix.ReadOnly, step.Intrinsic);
    }

    private void Enqueue(int index, WarpPortableTypedFlowState incoming)
    {
        bool changed;
        if (states[index] is null) { states[index] = incoming.Clone(); changed = true; }
        else { changed = states[index]!.MergeFrom(incoming, types, method.Instructions[index].Offset); }
        if (changed && queued.Add(index)) { pending.Enqueue(index); }
    }

    private void PropagateExceptions(int index, WarpPortableTypedFlowState before, ImmutableArray<int>.Builder targets)
    {
        foreach (WarpPortableTypedExceptionMembership membership in memberships[index].Where(member => member.Role == WarpPortableExceptionRole.Try))
        {
            WarpPortableMethodGraphExceptionRegion region = method.ExceptionRegions[membership.Region];
            int offset = region.FilterOffset >= 0 ? region.FilterOffset : region.HandlerOffset;
            WarpPortableTypedFlowState handler = before.Clone();
            handler.Stack.Clear();
            if (region.Kind is (int)ExceptionHandlingClauseOptions.Clause or (int)ExceptionHandlingClauseOptions.Filter)
            {
                handler.Stack.Add(types.Value(region.CatchType ?? types.Get(typeof(Exception)).Identity));
            }

            Enqueue(indices[offset], handler); targets.Add(offset);
        }
    }

    private void PropagateFilter(int index, WarpPortableTypedFlowState after, ImmutableArray<int>.Builder targets)
    {
        WarpPortableTypedExceptionMembership? filter = memberships[index].LastOrDefault(member => member.Role == WarpPortableExceptionRole.Filter);
        if (filter is null) { throw Error("Endfilter appears outside an exception filter.", method.Instructions[index].Offset); }
        int offset = method.ExceptionRegions[filter.Region].HandlerOffset;
        WarpPortableTypedFlowState handler = after.Clone();
        handler.Stack.Add(types.Value(types.Get(typeof(Exception)).Identity));
        Enqueue(indices[offset], handler); targets.Add(offset);
    }

    private static bool MayThrow(WarpPortableTypedEffect effect) => effect is WarpPortableTypedEffect.NullCheck or WarpPortableTypedEffect.BoundsCheck or
        WarpPortableTypedEffect.TypeCheck or WarpPortableTypedEffect.OverflowCheck or WarpPortableTypedEffect.DivideByZeroCheck or
        WarpPortableTypedEffect.TypeInitialize or WarpPortableTypedEffect.Allocate or WarpPortableTypedEffect.Call;

    private sealed class Step(int offset, Prefix prefix)
    {
        public int Offset { get; } = offset;
        public Prefix Prefix { get; } = prefix;
        public List<WarpPortableTypedEffect> Effects { get; } = [];
        public string? MemoryType { get; set; }
        public int StorageBits { get; set; }
        public string? Intrinsic { get; set; }
    }
}
