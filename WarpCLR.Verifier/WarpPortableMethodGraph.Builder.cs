using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableMethodGraph
{
    private sealed partial class Builder
    {
        private const int MaximumTypes = 4096;
        private const int MaximumFields = 16384;
        private const BindingFlags DeclaredMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        private readonly MethodInfo entry;
        private readonly HashSet<Assembly> permitted;
        private readonly Dictionary<string, MethodBase> methodSources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Type> typeSources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, WarpPortableMethodGraphMethod> methods = new(StringComparer.Ordinal);
        private readonly Dictionary<string, WarpPortableMethodGraphType> types = new(StringComparer.Ordinal);
        private readonly Dictionary<string, WarpPortableMethodGraphField> fields = new(StringComparer.Ordinal);
        private readonly Queue<MethodBase> pendingMethods = new();
        private readonly Queue<Type> pendingTypes = new();
        private readonly Dictionary<string, MethodInfo> slots = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Slot, string Type), WarpPortableMethodGraphDispatch> dispatches = [];
        private readonly HashSet<Type> instantiated = [];
        private readonly WarpCilCompilationAdmission admission;

        public Builder(MethodInfo entry, IEnumerable<Assembly>? permittedAssemblies, IEnumerable<Type>? concreteTypes)
        {
            this.entry = entry;
            if (entry.IsAbstract)
            {
                throw Error("A portable entry must be a concrete method.");
            }
            permitted = permittedAssemblies is null ? [entry.Module.Assembly] :
                new HashSet<Assembly>(WarpCompilationAdmission.Materialize(permittedAssemblies, "<portable-closure>",
                    WarpCompilationResourceKind.Functions, WarpCompilationAdmission.MaximumFunctionsPerEntry));
            if (!permitted.Contains(entry.Module.Assembly))
            {
                throw Error("The entry assembly is absent from the caller's permitted closure.");
            }

            admission = new WarpCilCompilationAdmission(WarpPortableMethodGraphIdentity.Method(entry));
            AddMethod(entry);
            if (!entry.IsStatic)
            {
                AddConcreteType(entry.DeclaringType!);
            }

            foreach (ParameterInfo parameter in entry.GetParameters())
            {
                Type type = parameter.ParameterType;
                if (type.IsByRef)
                {
                    type = type.GetElementType()!;
                }

                if (!type.IsAbstract && !type.IsInterface && !type.IsPrimitive && type != typeof(void))
                {
                    AddConcreteType(type);
                }
            }

            if (concreteTypes is not null)
            {
                foreach (Type type in WarpCompilationAdmission.Materialize(concreteTypes, "<portable-concrete-types>",
                    WarpCompilationResourceKind.ValueSlots, MaximumTypes))
                {
                    AddConcreteType(type);
                }
            }
        }

        public WarpPortableMethodGraph Discover()
        {
            while (true)
            {
                while (pendingTypes.TryDequeue(out Type? type))
                {
                    ReadType(type);
                }

                while (pendingMethods.TryDequeue(out MethodBase? method))
                {
                    ReadMethod(method);
                }

                bool added = ResolveDispatches();
                if (pendingMethods.Count == 0 && pendingTypes.Count == 0 && !added)
                {
                    return Finish();
                }
            }
        }

        private string AddMethod(MethodBase method)
        {
            ValidateMethod(method);
            string identity = WarpPortableMethodGraphIdentity.Method(method);
            if (methodSources.TryAdd(identity, method))
            {
                WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.Functions,
                    methodSources.Count, WarpCompilationAdmission.MaximumFunctionsPerEntry);
                AddType(method.DeclaringType!);
                if (method is MethodInfo function)
                {
                    AddType(function.ReturnType);
                    if (function.ReturnType.IsSealed && !function.ReturnType.IsValueType && !function.ReturnType.IsByRef) { AddConcreteType(function.ReturnType); }
                }

                bool delegateConstructor = method is ConstructorInfo && WarpPortableMethodGraphIntrinsics.IsDelegate(method.DeclaringType!);
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    if (!delegateConstructor || parameter.ParameterType != typeof(IntPtr))
                    {
                        AddType(parameter.ParameterType);
                    }
                }

                pendingMethods.Enqueue(method);
            }

            return identity;
        }

        private string AddType(Type type)
        {
            string identity = WarpPortableMethodGraphIdentity.Type(type);
            if (!WarpPortableMethodGraphIntrinsics.IsLeafType(type) && !WarpPortableMethodGraphIntrinsics.IsStructuralTuple(type) && !type.HasElementType && !permitted.Contains(type.Assembly))
            {
                throw Error($"Type '{identity}' belongs to an assembly outside the caller's permitted closure.");
            }

            if (typeSources.TryAdd(identity, type))
            {
                WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.ValueSlots, typeSources.Count, MaximumTypes);
                if (type.HasElementType)
                {
                    AddType(type.GetElementType()!);
                }

                if (type.IsGenericType)
                {
                    foreach (Type argument in type.GetGenericArguments())
                    {
                        AddType(argument);
                    }
                }

                pendingTypes.Enqueue(type);
            }

            return identity;
        }

        private void AddConcreteType(Type type)
        {
            if (type.IsInterface || type.IsAbstract || type.IsByRef || type == typeof(void))
            {
                throw Error("An instantiated type must be a concrete managed object or value type.");
            }

            AddType(type);
            instantiated.Add(type);
        }

        private void ReadType(Type type)
        {
            string identity = WarpPortableMethodGraphIdentity.Type(type);
            bool leaf = WarpPortableMethodGraphIntrinsics.IsLeafType(type) || type.HasElementType;
            string? baseType = type.BaseType is null ? null : AddType(type.BaseType);
            ImmutableArray<string> interfaces = leaf && !type.IsInterface ? [] : type.GetInterfaces().Select(AddType).Order(StringComparer.Ordinal).ToImmutableArray();
            var typeFields = ImmutableArray.CreateBuilder<string>();
            string? initializer = null;
            if (!leaf)
            {
                foreach (FieldInfo field in type.GetFields(DeclaredMembers).OrderBy(field => field.MetadataToken))
                {
                    typeFields.Add(AddField(field));
                }

                if (type.TypeInitializer is { } constructor)
                {
                    initializer = AddMethod(constructor);
                }
            }

            StructLayoutAttribute? layout = type.StructLayoutAttribute;
            types.Add(identity, new WarpPortableMethodGraphType(-1, identity, type, baseType, interfaces,
                typeFields.ToImmutable(), initializer, false, (int)(layout?.Value ?? LayoutKind.Auto), layout?.Pack ?? 0, layout?.Size ?? 0,
                type.HasElementType ? AddType(type.GetElementType()!) : null, type.IsArray ? type.GetArrayRank() : 0,
                type.IsEnum ? AddType(type.GetEnumUnderlyingType()) : null));
        }

        private string AddField(FieldInfo field)
        {
            string identity = WarpPortableMethodGraphIdentity.Field(field);
            if (fields.ContainsKey(identity))
            {
                return identity;
            }

            if (!permitted.Contains(field.Module.Assembly) && !WarpPortableMethodGraphIntrinsics.IsStructuralTuple(field.DeclaringType!))
            {
                throw Error($"Field '{identity}' is outside the permitted managed closure.");
            }

            if (field.FieldType.IsByRef || field.GetCustomAttributesData().Any(attribute => attribute.AttributeType == typeof(ThreadStaticAttribute)))
            {
                throw Error($"Field '{identity}' has an unsupported escaping byref or physical-thread storage contract.");
            }

            string declaringType = AddType(field.DeclaringType!);
            string fieldType = AddType(field.FieldType);
            int? offset = null;
            foreach (CustomAttributeData attribute in field.GetCustomAttributesData())
            {
                if (attribute.AttributeType == typeof(FieldOffsetAttribute))
                {
                    offset = (int)attribute.ConstructorArguments[0].Value!;
                }
            }

            WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.ValueSlots, fields.Count + 1L, MaximumFields);
            fields.Add(identity, new WarpPortableMethodGraphField(-1, identity, field, declaringType, fieldType, field.IsStatic,
                field.IsInitOnly, field.IsLiteral, offset, field.IsLiteral ? WarpPortableMethodGraphIdentity.Literal(field.GetRawConstantValue()) : null,
                ReadInitializedData(field)));
            return identity;
        }

        private void ReadMethod(MethodBase method)
        {
            string identity = WarpPortableMethodGraphIdentity.Method(method);
            string? intrinsic = WarpPortableMethodGraphIntrinsics.Resolve(method);
            bool delegateConstructor = intrinsic is not null && method is ConstructorInfo && WarpPortableMethodGraphIntrinsics.IsDelegate(method.DeclaringType!);
            ImmutableArray<string> parameters = method.GetParameters().Select(parameter =>
                delegateConstructor && parameter.ParameterType == typeof(IntPtr) ? "verified-method-target" : WarpPortableMethodGraphIdentity.Type(parameter.ParameterType)).ToImmutableArray();
            string returnType = method is MethodInfo function ? WarpPortableMethodGraphIdentity.Type(function.ReturnType) : WarpPortableMethodGraphIdentity.Type(typeof(void));
            if (intrinsic is not null || method.IsAbstract)
            {
                methods.Add(identity, new WarpPortableMethodGraphMethod(-1, identity, method, returnType, parameters, [], [], [], [], [], 0, false, intrinsic));
                return;
            }

            MethodBody body = method.GetMethodBody() ?? throw Error($"Method '{identity}' has no portable CIL body and no catalogued intrinsic.");
            byte[] cil = body.GetILAsByteArray() ?? throw Error($"Method '{identity}' has no CIL bytes.");
            admission.AdmitMethod(identity, parameters.Length + (method.IsStatic ? 0 : 1), body.MaxStackSize, body.LocalVariables.Count,
                cil.Length, isEntry: method == entry);
            ImmutableArray<WarpPortableMethodGraphInstruction> decoded = WarpPortableMethodGraphDecoder.Decode(cil, identity);
            admission.AdmitDecodedInstructions(decoded.Length);
            var instructions = ImmutableArray.CreateBuilder<WarpPortableMethodGraphInstruction>(decoded.Length);
            var dependencies = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<int, WarpPortableMethodGraphInstruction> byOffset = decoded.ToDictionary(instruction => instruction.Offset);
            Dictionary<int, WarpPortableMethodGraphInstruction> byEnd = decoded.ToDictionary(instruction => instruction.NextOffset);
            var branchTargets = new HashSet<int>(decoded.SelectMany(instruction => instruction.BranchTargets));
            foreach (WarpPortableMethodGraphInstruction instruction in decoded)
            {
                ValidateOperation(method, instruction, byOffset, byEnd, branchTargets);
                WarpPortableMethodGraphInstruction resolved = ResolveOperand(method, instruction, dependencies);
                instructions.Add(resolved);
            }

            var locals = ImmutableArray.CreateBuilder<string>(body.LocalVariables.Count);
            foreach (LocalVariableInfo local in body.LocalVariables)
            {
                if (local.IsPinned)
                {
                    throw Error($"Method '{identity}' has a pinned local outside the portable reference model.");
                }

                locals.Add(AddType(local.LocalType));
            }

            ImmutableArray<WarpPortableMethodGraphExceptionRegion> regions = ReadExceptionRegions(body, decoded, cil.Length);
            methods.Add(identity, new WarpPortableMethodGraphMethod(-1, identity, method, returnType, parameters, locals.MoveToImmutable(),
                ImmutableArray.CreateRange(cil), instructions.MoveToImmutable(), regions,
                dependencies.Order(StringComparer.Ordinal).ToImmutableArray(), body.MaxStackSize, body.InitLocals, null));
        }

        private WarpPortableMethodGraphInstruction ResolveOperand(MethodBase source, WarpPortableMethodGraphInstruction instruction,
            HashSet<string> dependencies)
        {
            Type[]? typeArguments = source.DeclaringType?.IsGenericType == true ? source.DeclaringType.GetGenericArguments() : null;
            Type[]? methodArguments = source is MethodInfo { IsGenericMethod: true } function ? function.GetGenericArguments() : null;
            int token = unchecked((int)instruction.Operand);
            try
            {
                switch (instruction.OpCode.OperandType)
                {
                    case OperandType.InlineMethod:
                        return ResolveMethodOperand(source, instruction, dependencies, typeArguments, methodArguments);
                    case OperandType.InlineType:
                        return ResolveTypeOperand(source, instruction, typeArguments, methodArguments);
                    case OperandType.InlineField:
                        FieldInfo field = source.Module.ResolveField(token, typeArguments, methodArguments) ?? throw Error("A field token is unresolved.", instruction.Offset);
                        return instruction with { Field = AddField(field) };
                    case OperandType.InlineTok:
                        MemberInfo? member = source.Module.ResolveMember(token, typeArguments, methodArguments);
                        return member switch
                        {
                            Type typeMember => instruction with { Type = AddType(typeMember) },
                            FieldInfo fieldMember => instruction with { Field = AddField(fieldMember) },
                            MethodBase methodMember => instruction with { Method = AddMethod(methodMember) },
                            _ => throw Error("A metadata token has no portable target.", instruction.Offset),
                        };
                    case OperandType.InlineString:
                        AddConcreteType(typeof(string));
                        return instruction with { StringLiteral = source.Module.ResolveString(token) };
                    case OperandType.InlineSig:
                        throw Error("Indirect calls and standalone function-pointer signatures are outside the portable profile.", instruction.Offset);
                    default:
                        return instruction;
                }
            }
            catch (ArgumentException exception)
            {
                throw Error($"A metadata token cannot be resolved in its closed generic context: {exception.Message}", instruction.Offset);
            }
            catch (BadImageFormatException exception)
            {
                throw Error($"Malformed metadata prevents closure resolution: {exception.Message}", instruction.Offset);
            }
        }

        private WarpPortableMethodGraphInstruction ResolveMethodOperand(MethodBase source, WarpPortableMethodGraphInstruction instruction,
            HashSet<string> dependencies, Type[]? typeArguments, Type[]? methodArguments)
        {
            MethodBase target = source.Module.ResolveMethod(unchecked((int)instruction.Operand), typeArguments, methodArguments) ??
                throw Error("A method token is unresolved.", instruction.Offset);
            string identity = AddMethod(target);
            dependencies.Add(identity);
            if (target is MethodInfo { IsVirtual: true } slot &&
                (instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Ldvirtftn))
            {
                slots.TryAdd(identity, slot);
                if (typeof(Exception).IsAssignableFrom(slot.DeclaringType!)) { AddImplicitExceptionTypes(); }
            }
            else if (target.IsAbstract)
            {
                throw Error("A direct call cannot target an abstract method.", instruction.Offset);
            }

            if (instruction.OpCode == OpCodes.Newobj)
            {
                AddConcreteType(target.DeclaringType!);
            }

            if (target is MethodInfo { IsGenericMethod: true, DeclaringType: { } declaring } factory && declaring == typeof(Activator) &&
                factory.Name is nameof(Activator.CreateInstance) && factory.GetParameters().Length == 0)
            {
                AddDefaultConstructor(factory.ReturnType, dependencies, instruction.Offset);
            }

            return instruction with { Method = identity };
        }

        private void AddImplicitExceptionTypes()
        {
            Type[] faultTypes = [typeof(NullReferenceException), typeof(IndexOutOfRangeException), typeof(OverflowException),
                typeof(DivideByZeroException), typeof(InvalidCastException), typeof(ArrayTypeMismatchException),
                typeof(TypeInitializationException), typeof(OutOfMemoryException), typeof(OperationCanceledException)];
            foreach (Type type in faultTypes) { AddConcreteType(type); }
        }

        private void AddDefaultConstructor(Type type, HashSet<string> dependencies, int offset)
        {
            AddConcreteType(type);
            ConstructorInfo? constructor = type.GetConstructor(Type.EmptyTypes);
            if (constructor is not null)
            {
                dependencies.Add(AddMethod(constructor));
            }
            else if (!type.IsValueType)
            {
                throw Error("The closed generic default construction has no public parameterless constructor.", offset);
            }
        }

        private WarpPortableMethodGraphInstruction ResolveTypeOperand(MethodBase source, WarpPortableMethodGraphInstruction instruction,
            Type[]? typeArguments, Type[]? methodArguments)
        {
            Type type = source.Module.ResolveType(unchecked((int)instruction.Operand), typeArguments, methodArguments);
            string identity = AddType(type);
            if (typeof(Exception).IsAssignableFrom(type) && !type.IsAbstract) { AddConcreteType(type); }
            if (instruction.OpCode == OpCodes.Newarr)
            {
                if (type.IsByRefLike || type.IsByRef) { throw Error("An array cannot store escaping managed byrefs/byref-like values.", instruction.Offset); }
                AddConcreteType(type.MakeArrayType());
            }
            else if (instruction.OpCode == OpCodes.Box || instruction.OpCode == OpCodes.Initobj)
            {
                AddConcreteType(type);
            }
            else if (instruction.OpCode == OpCodes.Constrained && !type.IsAbstract && !type.IsInterface)
            {
                AddConcreteType(type);
            }

            return instruction with { Type = identity };
        }

        private ImmutableArray<WarpPortableMethodGraphExceptionRegion> ReadExceptionRegions(MethodBody body,
            ImmutableArray<WarpPortableMethodGraphInstruction> instructions, int cilLength)
        {
            var boundaries = new HashSet<int>(instructions.Select(instruction => instruction.Offset)) { cilLength };
            var regions = ImmutableArray.CreateBuilder<WarpPortableMethodGraphExceptionRegion>(body.ExceptionHandlingClauses.Count);
            foreach (ExceptionHandlingClause clause in body.ExceptionHandlingClauses)
            {
                if (clause.TryOffset < 0 || clause.HandlerOffset < 0 || clause.TryLength <= 0 || clause.HandlerLength <= 0 ||
                    (long)clause.TryOffset + clause.TryLength > cilLength || (long)clause.HandlerOffset + clause.HandlerLength > cilLength ||
                    !boundaries.Contains(clause.TryOffset) ||
                    !boundaries.Contains(clause.TryOffset + clause.TryLength) || !boundaries.Contains(clause.HandlerOffset) ||
                    !boundaries.Contains(clause.HandlerOffset + clause.HandlerLength))
                {
                    throw Error("An exception region has invalid instruction boundaries.");
                }

                int filter = clause.Flags == ExceptionHandlingClauseOptions.Filter ? clause.FilterOffset : -1;
                if (filter >= 0 && (!boundaries.Contains(filter) || filter >= clause.HandlerOffset))
                {
                    throw Error("An exception filter has an invalid instruction boundary.");
                }

                string? catchType = clause.Flags == ExceptionHandlingClauseOptions.Clause && clause.CatchType is { } type ? AddType(type) : null;
                if (clause.Flags == ExceptionHandlingClauseOptions.Clause && clause.CatchType is { IsAbstract: false } concreteCatch) { AddConcreteType(concreteCatch); }
                regions.Add(new WarpPortableMethodGraphExceptionRegion((int)clause.Flags, clause.TryOffset, clause.TryLength,
                    clause.HandlerOffset, clause.HandlerLength, filter, catchType));
            }

            return regions.MoveToImmutable();
        }

        private bool ResolveDispatches()
        {
            bool added = false;
            foreach ((string identity, MethodInfo slot) in slots.ToArray())
            {
                Type declaringType = slot.DeclaringType!;
                foreach (Type concrete in instantiated.ToArray())
                {
                    if (!declaringType.IsAssignableFrom(concrete))
                    {
                        continue;
                    }

                    string typeIdentity = WarpPortableMethodGraphIdentity.Type(concrete);
                    if (dispatches.ContainsKey((identity, typeIdentity)))
                    {
                        continue;
                    }

                    MethodInfo target;
                    if (declaringType.IsInterface)
                    {
                        InterfaceMapping mapping = concrete.GetInterfaceMap(declaringType);
                        int index = Array.FindIndex(mapping.InterfaceMethods, method => method == slot ||
                            method.MetadataToken == slot.MetadataToken && method.Module == slot.Module);
                        if (index < 0)
                        {
                            throw Error("An interface slot has no target in a concrete type.");
                        }

                        target = mapping.TargetMethods[index];
                    }
                    else
                    {
                        MethodInfo definition = slot.GetBaseDefinition();
                        target = concrete.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            .FirstOrDefault(method => method.IsVirtual && method.GetBaseDefinition().MetadataToken == definition.MetadataToken &&
                                method.GetBaseDefinition().Module == definition.Module) ?? slot;
                    }

                    if (target.IsGenericMethodDefinition && slot.IsGenericMethod)
                    {
                        target = target.MakeGenericMethod(slot.GetGenericArguments());
                    }

                    if (target.IsAbstract)
                    {
                        throw Error("An instantiated type has an abstract dispatch target.");
                    }

                    string targetIdentity = AddMethod(target);
                    dispatches.Add((identity, typeIdentity), new WarpPortableMethodGraphDispatch(identity, typeIdentity, targetIdentity));
                    added = true;
                }
            }

            return added;
        }

        private void ValidateMethod(MethodBase method)
        {
            if (method.ContainsGenericParameters || method.CallingConvention.HasFlag(CallingConventions.VarArgs) ||
                method.Attributes.HasFlag(MethodAttributes.PinvokeImpl) || method.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.InternalCall) &&
                WarpPortableMethodGraphIntrinsics.Resolve(method) is null)
            {
                throw Error("Open generics, varargs, unmanaged interop and uncatalogued runtime calls are not portable.");
            }

            if (!permitted.Contains(method.Module.Assembly) && WarpPortableMethodGraphIntrinsics.Resolve(method) is null && !method.IsAbstract)
            {
                throw Error($"Method '{method.Name}' is outside the permitted assemblies and has no explicit portable intrinsic.");
            }

        }

        private static void ValidateOperation(MethodBase method, WarpPortableMethodGraphInstruction instruction,
            Dictionary<int, WarpPortableMethodGraphInstruction> byOffset,
            Dictionary<int, WarpPortableMethodGraphInstruction> byEnd, HashSet<int> branchTargets)
        {
            OpCode opCode = instruction.OpCode;
            if (opCode == OpCodes.Calli || opCode == OpCodes.Jmp || opCode == OpCodes.Localloc || opCode == OpCodes.Cpblk ||
                opCode == OpCodes.Initblk || opCode == OpCodes.Arglist || opCode == OpCodes.Mkrefany || opCode == OpCodes.Refanyval ||
                opCode == OpCodes.Refanytype || opCode == OpCodes.Conv_I || opCode == OpCodes.Conv_U ||
                opCode == OpCodes.Conv_Ovf_I || opCode == OpCodes.Conv_Ovf_U || opCode == OpCodes.Conv_Ovf_I_Un || opCode == OpCodes.Conv_Ovf_U_Un)
            {
                throw Error("Native-address arithmetic, indirect calls and unsafe memory operations are not portable.", instruction.Offset);
            }

            if (opCode == OpCodes.Newobj)
            {
                ValidateDelegateConstructor(method, instruction, byEnd, branchTargets);
            }

            if (opCode == OpCodes.Ldftn || opCode == OpCodes.Ldvirtftn)
            {
                if (!byOffset.TryGetValue(instruction.NextOffset, out WarpPortableMethodGraphInstruction? next) || next.OpCode != OpCodes.Newobj)
                {
                    throw Error("A method target must bind immediately into a verified delegate; it cannot escape as a native address.", instruction.Offset);
                }

                MethodBase? constructor = method.Module.ResolveMethod(unchecked((int)next.Operand),
                    method.DeclaringType?.GetGenericArguments(), method is MethodInfo generic && generic.IsGenericMethod ? generic.GetGenericArguments() : null);
                if (constructor?.DeclaringType is not { } type || !WarpPortableMethodGraphIntrinsics.IsDelegate(type))
                {
                    throw Error("A method target may only bind a portable delegate constructor.", instruction.Offset);
                }
            }
        }

        private static void ValidateDelegateConstructor(MethodBase source, WarpPortableMethodGraphInstruction instruction,
            Dictionary<int, WarpPortableMethodGraphInstruction> byEnd, HashSet<int> branchTargets)
        {
            MethodBase? constructor = source.Module.ResolveMethod(unchecked((int)instruction.Operand),
                source.DeclaringType?.GetGenericArguments(), source is MethodInfo function && function.IsGenericMethod ? function.GetGenericArguments() : null);
            if (constructor?.DeclaringType is not { } type || !WarpPortableMethodGraphIntrinsics.IsDelegate(type))
            {
                return;
            }

            if (branchTargets.Contains(instruction.Offset))
            {
                throw Error("A branch cannot bypass the verified method target of a delegate binding.", instruction.Offset);
            }

            if (!byEnd.TryGetValue(instruction.Offset, out WarpPortableMethodGraphInstruction? predecessor) ||
                predecessor.OpCode != OpCodes.Ldftn && predecessor.OpCode != OpCodes.Ldvirtftn)
            {
                throw Error("A delegate constructor must consume a verified closed method target.", instruction.Offset);
            }
        }

        private static WarpVerificationException Error(string message, int? offset = null) => new("WRPCLR2100", message, offset);
    }
}
