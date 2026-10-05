using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private void Field(WarpPortableMethodGraphInstruction instruction, WarpPortableTypedFlowState state, Step step)
    {
        WarpPortableMethodGraphField metadata = fields[instruction.Field!];
        WarpPortableTypedField field = types.Field(metadata.Identity);
        bool store = instruction.OpCode == OpCodes.Stfld || instruction.OpCode == OpCodes.Stsfld;
        bool address = instruction.OpCode == OpCodes.Ldflda || instruction.OpCode == OpCodes.Ldsflda;
        bool staticOperation = instruction.OpCode == OpCodes.Ldsfld || instruction.OpCode == OpCodes.Ldsflda || instruction.OpCode == OpCodes.Stsfld;
        Require(metadata.IsStatic == staticOperation, "Field access kind disagrees with static/instance metadata.", step.Offset);
        WarpPortableTypedValue? value = store ? Pop(state, step.Offset) : null;
        if (value is not null) { RequireAssignable(value, field.TypeIdentity, step.Offset); }
        bool initializer = method.SourceMethod is ConstructorInfo && method.SourceMethod.DeclaringType == metadata.SourceField.DeclaringType &&
            method.SourceMethod.IsStatic == metadata.IsStatic;
        Require(!store || !metadata.IsLiteral && (!field.IsReadOnly || initializer), "A literal/readonly field is written outside its initializer.", step.Offset);
        WarpPortableTypedValue pointer;
        if (metadata.IsStatic)
        {
            step.InitializerTrigger = WarpPortableTypeInitialization.ForField(graph, metadata);
            if (step.InitializerTrigger is not null) { step.Effects.Add(WarpPortableTypedEffect.TypeInitialize); }
            pointer = Borrow(field.TypeIdentity, new(WarpPortableProvenanceKind.StaticStorage, string.Empty, metadata.Id,
                metadata.DeclaringType, field.ByteOffset, field.ByteSize), field.IsReadOnly && !initializer);
        }
        else { pointer = InstanceField(Pop(state, step.Offset), metadata, field, store, address, initializer, step); }
        step.MemoryType = field.TypeIdentity; step.StorageBits = types.Get(field.TypeIdentity).StorageBits;
        if (address) { state.Stack.Add(pointer); return; }
        if (store)
        {
            RequireWritable(pointer, step.Offset); WriteBorrow(pointer, state, step.Offset);
            WriteEffect(step); return;
        }

        RequireInitialized(pointer, state, step.Offset);
        ReadEffect(step); state.Stack.Add(LoadToStack(types.Value(field.TypeIdentity)));
    }

    private WarpPortableTypedValue InstanceField(WarpPortableTypedValue owner, WarpPortableMethodGraphField metadata,
        WarpPortableTypedField field, bool store, bool address, bool initializer, Step step)
    {
        bool readOnly = field.IsReadOnly && !initializer;
        if (owner.Category == WarpPortableStackCategory.Reference)
        {
            Require(owner.IsNull || types.Source(metadata.DeclaringType).IsAssignableFrom(types.Source(owner.TypeIdentity)), "Field receiver has the wrong managed class.", step.Offset);
            Require(!owner.IsUninitializedThis || store && method.SourceMethod.DeclaringType == metadata.SourceField.DeclaringType,
                "Uninitialized this can only initialize its own instance fields.", step.Offset);
            step.Effects.Add(WarpPortableTypedEffect.NullCheck);
            return Borrow(field.TypeIdentity, new(WarpPortableProvenanceKind.HeapInterior, string.Empty, -1,
                owner.TypeIdentity, field.ByteOffset, field.ByteSize), readOnly);
        }

        if (owner.Category == WarpPortableStackCategory.ManagedByref)
        {
            Require(string.Equals(types.Get(owner.TypeIdentity).ElementType, metadata.DeclaringType, StringComparison.Ordinal),
                "A value-field byref points to the wrong declaring type.", step.Offset);
            string identity = types.Get(types.Source(field.TypeIdentity).MakeByRefType()).Identity;
            return types.Value(identity) with { IsReadOnly = readOnly || owner.IsReadOnly, ControlledMutability = !readOnly && owner.ControlledMutability, Provenance = owner.Provenance.Select(origin =>
                origin with { ByteOffset = checked(origin.ByteOffset + field.ByteOffset), ByteLength = field.ByteSize }).ToImmutableArray() };
        }

        Require(owner.Category == WarpPortableStackCategory.Value && !store && !address &&
            string.Equals(owner.TypeIdentity, metadata.DeclaringType, StringComparison.Ordinal), "A field address/store cannot escape a temporary value.", step.Offset);
        return Borrow(field.TypeIdentity, new(WarpPortableProvenanceKind.HeapInterior, string.Empty, -1, owner.TypeIdentity, field.ByteOffset, field.ByteSize), readOnly);
    }

    private WarpPortableTypedValue Borrow(string element, WarpPortableTypedProvenance origin, bool readOnly = false, bool controlledMutability = false) =>
        types.Value(types.Get(types.Source(element).MakeByRefType()).Identity) with
        { Provenance = [origin], IsReadOnly = readOnly, ControlledMutability = controlledMutability };

    private static void ReadEffect(Step step)
    {
        step.Effects.Add(WarpPortableTypedEffect.ReadMemory);
        if (step.Prefix.Volatile) { step.Effects.Add(WarpPortableTypedEffect.Acquire); }
    }

    private static void WriteEffect(Step step)
    {
        if (step.Prefix.Volatile) { step.Effects.Add(WarpPortableTypedEffect.Release); }
        step.Effects.Add(WarpPortableTypedEffect.WriteMemory);
    }
}
