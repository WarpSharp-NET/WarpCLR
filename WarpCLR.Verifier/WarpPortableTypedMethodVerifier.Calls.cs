using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private void Call(WarpPortableMethodGraphInstruction instruction, WarpPortableTypedFlowState state, Step step)
    {
        WarpPortableMethodGraphMethod target = methods[instruction.Method!];
        if (instruction.OpCode == OpCodes.Ldftn || instruction.OpCode == OpCodes.Ldvirtftn)
        {
            if (instruction.OpCode == OpCodes.Ldvirtftn)
            {
                ValidateReceiver(Pop(state, step.Offset), target, step, constructor: false);
                step.Effects.Add(WarpPortableTypedEffect.NullCheck);
            }

            state.Stack.Add(types.Value("verified-method-target") with { MethodTarget = target.Identity }); return;
        }

        WarpPortableTypedValue[] arguments = ConsumeArguments(target, state, step);
        bool construction = instruction.OpCode == OpCodes.Newobj;
        WarpPortableTypedValue? receiver = null;
        if (!target.SourceMethod.IsStatic && !construction)
        {
            receiver = Pop(state, step.Offset);
            ValidateReceiver(receiver, target, step, target.SourceMethod is ConstructorInfo);
            if (receiver.Category == WarpPortableStackCategory.ManagedByref && target.SourceMethod is not ConstructorInfo)
            {
                RequireInitialized(receiver, state, step.Offset);
                Require(!receiver.IsReadOnly || receiver.ControlledMutability || !types.Source(types.Get(receiver.TypeIdentity).ElementType!).IsValueType ||
                    target.Intrinsic?.Contains("object.type-of", StringComparison.Ordinal) == true || target.SourceMethod.GetCustomAttributesData().Any(attribute =>
                    attribute.AttributeType.FullName is "System.Runtime.CompilerServices.IsReadOnlyAttribute") ||
                    target.SourceMethod.DeclaringType!.GetCustomAttributesData().Any(attribute => attribute.AttributeType.FullName is "System.Runtime.CompilerServices.IsReadOnlyAttribute"),
                    "A readonly value receiver calls a mutable instance method without a defensive copy.", step.Offset);
            }
            if (instruction.OpCode == OpCodes.Callvirt) { step.Effects.Add(WarpPortableTypedEffect.NullCheck); }
        }

        if (WarpPortableMethodGraphIntrinsics.IsDelegate(target.SourceMethod.DeclaringType!) && construction)
        {
            ValidateDelegate(target, arguments, step.Offset);
        }

        step.InitializerTrigger = WarpPortableTypeInitialization.ForMethod(graph, target.SourceMethod);
        if (step.InitializerTrigger is not null) { step.Effects.Add(WarpPortableTypedEffect.TypeInitialize); }
        step.Intrinsic = target.Intrinsic;
        CallEffects(target, step, construction);
        foreach (WarpPortableTypedValue argument in arguments.Where(argument => argument.Category == WarpPortableStackCategory.ManagedByref && !argument.IsReadOnly))
        {
            WriteBorrow(argument, state, step.Offset);
        }

        if (receiver is { IsUninitializedThis: true } && target.SourceMethod is ConstructorInfo) { InitializeThis(state); }
        if (receiver?.Category == WarpPortableStackCategory.ManagedByref && target.SourceMethod is ConstructorInfo) { WriteBorrow(receiver, state, step.Offset); }
        if (construction)
        {
            string result = types.Get(target.SourceMethod.DeclaringType!).Identity;
            state.Stack.Add(types.Value(result));
        }
        else if (types.Get(target.ReturnType).Category != WarpPortableStackCategory.Void)
        {
            state.Stack.Add(CallResult(target, receiver, arguments, step));
        }
    }

    private WarpPortableTypedValue[] ConsumeArguments(WarpPortableMethodGraphMethod target, WarpPortableTypedFlowState state, Step step)
    {
        var arguments = new WarpPortableTypedValue[target.ParameterTypes.Length];
        ParameterInfo[] metadata = target.SourceMethod.GetParameters();
        for (int index = arguments.Length - 1; index >= 0; index--)
        {
            WarpPortableTypedValue argument = Pop(state, step.Offset);
            string signature = target.ParameterTypes[index];
            RequireAssignable(argument, signature, step.Offset);
            if (argument.Category == WarpPortableStackCategory.ManagedByref)
            {
                RequirePointer(argument, types.Get(signature).ElementType, step.Offset);
                bool readOnly = ReadOnlyParameter(metadata[index]);
                Require(readOnly || !argument.IsReadOnly, "A readonly byref is passed to a mutable ref/out parameter.", step.Offset);
                if (!metadata[index].IsOut) { RequireInitialized(argument, state, step.Offset); }
            }

            arguments[index] = argument;
        }

        return arguments;
    }

    private void ValidateReceiver(WarpPortableTypedValue receiver, WarpPortableMethodGraphMethod target, Step step, bool constructor)
    {
        Type declaring = target.SourceMethod.DeclaringType!;
        if (receiver.Category == WarpPortableStackCategory.ManagedByref)
        {
            string element = types.Get(receiver.TypeIdentity).ElementType!;
            RequirePointer(receiver, null, step.Offset);
            Require(declaring.IsValueType && string.Equals(element, types.Get(declaring).Identity, StringComparison.Ordinal) ||
                step.Prefix.Constrained is { } constrained && string.Equals(element, constrained, StringComparison.Ordinal) && declaring.IsAssignableFrom(types.Source(element)),
                "The value/constrained receiver has an incompatible declaring type.", step.Offset);
            return;
        }

        Require(receiver.Category == WarpPortableStackCategory.Reference && (receiver.IsNull || declaring.IsAssignableFrom(types.Source(receiver.TypeIdentity))),
            "An instance call has an incompatible managed receiver.", step.Offset);
        Require(!receiver.IsUninitializedThis || constructor && method.SourceMethod is ConstructorInfo && declaring.IsAssignableFrom(method.SourceMethod.DeclaringType!),
            "Uninitialized this escapes through a nonconstructor call.", step.Offset);
        if (target.SourceMethod.IsAbstract || target.SourceMethod is MethodInfo { IsVirtual: true } && step.Prefix.Constrained is not null)
        {
            Require(graph.Dispatches.Any(dispatch => string.Equals(dispatch.Slot, target.Identity, StringComparison.Ordinal)),
                "A virtual/interface call lacks closed dispatch targets.", step.Offset);
        }
    }

    private WarpPortableTypedValue CallResult(WarpPortableMethodGraphMethod target, WarpPortableTypedValue? receiver,
        WarpPortableTypedValue[] arguments, Step step)
    {
        WarpPortableTypedValue value = types.Value(target.ReturnType);
        if (value.Category != WarpPortableStackCategory.ManagedByref) { return LoadToStack(value); }
        string element = types.Get(target.ReturnType).ElementType!;
        if (target.Intrinsic is not null && receiver is not null && target.SourceMethod.DeclaringType!.IsArray)
        {
            return Borrow(element, new(WarpPortableProvenanceKind.HeapInterior, string.Empty, -1, receiver.TypeIdentity, 0, types.Get(element).ByteSize),
                step.Prefix.ReadOnly, step.Prefix.ReadOnly);
        }

        WarpPortableTypedReturnSummary summary = summaries[target.Identity];
        var origins = new List<WarpPortableTypedProvenance>();
        foreach (WarpPortableTypedProvenance origin in summary.Origins)
        {
            if (origin.Kind != WarpPortableProvenanceKind.Argument) { origins.Add(origin); continue; }
            int index = origin.OwnerIndex - (target.SourceMethod.IsStatic ? 0 : 1);
            WarpPortableTypedValue actual = index < 0 ? receiver! : arguments[index];
            origins.AddRange(actual.Provenance.Select(parent => parent with
                { ByteOffset = checked(parent.ByteOffset + origin.ByteOffset), ByteLength = origin.ByteLength }));
        }

        Require(origins.Count != 0, "A byref-returning callee has no proved portable lifetime summary.", step.Offset);
        return value with { Provenance = SortOrigins(origins), IsReadOnly = summary.ReadOnly };
    }

    internal static bool ReadOnlyParameter(ParameterInfo parameter) => parameter.GetCustomAttributesData().Any(attribute =>
        attribute.AttributeType.FullName is "System.Runtime.CompilerServices.IsReadOnlyAttribute" or "System.Runtime.CompilerServices.RequiresLocationAttribute") ||
        parameter.GetRequiredCustomModifiers().Any(type => type.FullName is "System.Runtime.InteropServices.InAttribute" or "System.Runtime.CompilerServices.IsReadOnlyAttribute");

    private static void InitializeThis(WarpPortableTypedFlowState state)
    {
        state.InitializedThis = true;
        for (int index = 0; index < state.Arguments.Length; index++) { state.Arguments[index] = state.Arguments[index] with { IsUninitializedThis = false }; }
        for (int index = 0; index < state.Locals.Length; index++) { state.Locals[index] = state.Locals[index] with { IsUninitializedThis = false }; }
        for (int index = 0; index < state.Stack.Count; index++) { state.Stack[index] = state.Stack[index] with { IsUninitializedThis = false }; }
    }
}
