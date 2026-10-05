using System.Reflection.Emit;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private void Storage(WarpPortableMethodGraphInstruction instruction, WarpPortableTypedFlowState state, Step step)
    {
        string name = instruction.OpCode.Name!;
        bool argument = name.Contains("arg", StringComparison.Ordinal);
        bool address = name.StartsWith("ldarga", StringComparison.Ordinal) || name.StartsWith("ldloca", StringComparison.Ordinal);
        bool store = name.StartsWith("st", StringComparison.Ordinal);
        WarpPortableTypedValue[] slots = argument ? state.Arguments : state.Locals;
        int index = instruction.OpCode.OperandType is OperandType.InlineVar or OperandType.ShortInlineVar ?
            checked((int)instruction.Operand) : name[^1] - '0';
        Require(index >= 0 && index < slots.Length, "An argument/local index is outside its signature.", step.Offset);
        string declared = argument ? argumentTypes[index] : method.LocalTypes[index];
        if (store)
        {
            WarpPortableTypedValue value = Pop(state, step.Offset);
            RequireAssignable(value, declared, step.Offset);
            Require(value.Category != WarpPortableStackCategory.FunctionTarget, "A method target cannot escape into private storage.", step.Offset);
            slots[index] = value with { TypeIdentity = declared, Category = types.Get(declared).Category,
                WordCount = types.Get(declared).WordCount, SourceStorageType = null };
            if (!argument) { System.Array.Fill(state.InitializedLocals[index], true); }
            step.Effects.Add(WarpPortableTypedEffect.PrivateWrite); step.MemoryType = declared;
            step.StorageBits = types.Get(declared).StorageBits;
            return;
        }

        if (address)
        {
            Require(slots[index].Category != WarpPortableStackCategory.ManagedByref && !slots[index].IsUninitializedThis,
                "A managed-byref slot or uninitialized this cannot be addressed as a nested byref.", step.Offset);
            string byref = types.Get(types.Source(declared).MakeByRefType()).Identity;
            var origin = new WarpPortableTypedProvenance(argument ? WarpPortableProvenanceKind.FrameArgument : WarpPortableProvenanceKind.FrameLocal,
                method.Identity, index, declared, 0, types.Get(declared).ByteSize);
            state.Stack.Add(types.Value(byref) with { Provenance = [origin] });
        }
        else
        {
            Require(argument || state.InitializedLocals[index].All(initialized => initialized), "A local is read before definite initialization.", step.Offset);
            state.Stack.Add(LoadToStack(slots[index]));
        }

        step.Effects.Add(WarpPortableTypedEffect.PrivateRead);
    }

    private void Constants(WarpPortableMethodGraphInstruction instruction, WarpPortableTypedFlowState state, Step step)
    {
        OpCode operation = instruction.OpCode;
        string name = operation.Name!;
        if (operation == OpCodes.Pop) { Pop(state, step.Offset); return; }
        if (operation == OpCodes.Dup)
        {
            WarpPortableTypedValue value = Pop(state, step.Offset); state.Stack.Add(value); state.Stack.Add(value); return;
        }

        if (operation == OpCodes.Ldnull) { state.Stack.Add(Primitive(typeof(object)) with { IsNull = true }); return; }
        if (operation == OpCodes.Ldstr)
        {
            state.Stack.Add(Primitive(typeof(string))); step.Effects.Add(WarpPortableTypedEffect.Allocate); step.Effects.Add(WarpPortableTypedEffect.Safepoint); return;
        }

        if (operation == OpCodes.Ldtoken)
        {
            Type handle = instruction.Type is not null ? typeof(RuntimeTypeHandle) : instruction.Field is not null ? typeof(RuntimeFieldHandle) : typeof(RuntimeMethodHandle);
            state.Stack.Add(Primitive(handle)); return;
        }

        if (operation == OpCodes.Sizeof)
        {
            WarpPortableTypedType type = types.Get(instruction.Type!);
            if (cliSizes is null)
            {
                Require(type.Category is not (WarpPortableStackCategory.Reference or WarpPortableStackCategory.ManagedByref or WarpPortableStackCategory.Handle or WarpPortableStackCategory.FunctionTarget) && type.ManagedRootByteOffsets.IsEmpty,
                    "Sizeof cannot expose the physical managed-reference representation without its separate captured CLI numeric layout.", step.Offset);
            }
            else { _ = cliSizes.TypeSize(type.Identity, step.Offset); }
            state.Stack.Add(Primitive(typeof(uint))); return;
        }

        Type primitive = name is "ldc.r4" ? typeof(float) : name is "ldc.r8" ? typeof(double) : name is "ldc.i8" ? typeof(long) : typeof(int);
        state.Stack.Add(Primitive(primitive));
    }
}
