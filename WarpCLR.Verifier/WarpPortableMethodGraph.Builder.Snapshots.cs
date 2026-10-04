using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableMethodGraph
{
    private sealed partial class Builder
    {
        private readonly Dictionary<Module, ImmutableArray<byte>> moduleImages = [];
        private long totalModuleBytes;

        private ImmutableArray<byte> ReadInitializedData(FieldInfo field)
        {
            if (!field.Attributes.HasFlag(FieldAttributes.HasFieldRVA))
            {
                return [];
            }

            ImmutableArray<byte> image = ReadModuleImage(field.Module);
            if (image.IsEmpty)
            {
                throw Error("A dynamic RVA field has no immutable portable data snapshot.");
            }

            using var stream = new MemoryStream(image.ToArray(), writable: false);
            using var reader = new PEReader(stream);
            MetadataReader metadata = reader.GetMetadataReader();
            EntityHandle handle = MetadataTokens.EntityHandle(field.MetadataToken);
            if (handle.Kind != HandleKind.FieldDefinition)
            {
                throw Error("An initialized data field has no FieldDef metadata identity.");
            }

            int address = metadata.GetFieldDefinition((FieldDefinitionHandle)handle).GetRelativeVirtualAddress();
            int size = RvaSize(field.FieldType);
            if (address == 0 || size <= 0 || size > WarpCompilationAdmission.MaximumAssemblyBytes)
            {
                throw Error("An initialized data field has no finite declared portable size.");
            }

            try
            {
                return reader.GetSectionData(address).GetContent(0, size);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw Error($"Initialized field data lies outside the PE image: {exception.Message}");
            }
        }

        private static int RvaSize(Type type) => type == typeof(byte) || type == typeof(sbyte) || type == typeof(bool) ? 1 :
            type == typeof(short) || type == typeof(ushort) || type == typeof(char) ? 2 :
            type == typeof(int) || type == typeof(uint) || type == typeof(float) ? 4 :
            type == typeof(long) || type == typeof(ulong) || type == typeof(double) ? 8 : type.StructLayoutAttribute?.Size ?? 0;

        private ImmutableArray<byte> ReadModuleImage(Module module)
        {
            if (moduleImages.TryGetValue(module, out ImmutableArray<byte> snapshot))
            {
                return snapshot;
            }

            if (module.Assembly.IsDynamic)
            {
                moduleImages.Add(module, []);
                return [];
            }

            using FileStream stream = File.OpenRead(module.FullyQualifiedName);
            WarpCompilationAdmission.Require(module.Name, WarpCompilationResourceKind.AssemblyBytes,
                totalModuleBytes + stream.Length, WarpCompilationAdmission.MaximumAssemblyBytes);
            byte[] bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1)
            {
                throw Error("A module image changed length during closure snapshotting.");
            }

            using var metadataStream = new MemoryStream(bytes, writable: false);
            using var reader = new PEReader(metadataStream);
            MetadataReader metadata = reader.GetMetadataReader();
            if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != module.ModuleVersionId)
            {
                throw Error("The loaded module and the immutable closure image have different identities.");
            }

            totalModuleBytes += bytes.LongLength;
            snapshot = ImmutableArray.CreateRange(bytes);
            moduleImages.Add(module, snapshot);
            return snapshot;
        }

        private WarpPortableMethodGraph Finish()
        {
            foreach (string slot in slots.Keys)
            {
                if (!dispatches.Values.Any(dispatch => string.Equals(dispatch.Slot, slot, StringComparison.Ordinal)))
                {
                    throw Error($"Virtual slot '{slot}' has no concrete target in the supplied closed type set.");
                }
            }

            ImmutableArray<WarpPortableMethodGraphMethod> methodNodes = methods.Values.OrderBy(method => method.Identity, StringComparer.Ordinal)
                .Select((method, index) => method with { Id = index }).ToImmutableArray();
            ImmutableArray<WarpPortableMethodGraphType> typeNodes = types.Values.OrderBy(type => type.Identity, StringComparer.Ordinal)
                .Select((type, index) => type with { Id = index, Instantiated = instantiated.Contains(type.SourceType) }).ToImmutableArray();
            ImmutableArray<WarpPortableMethodGraphField> fieldNodes = fields.Values.OrderBy(field => field.Identity, StringComparer.Ordinal)
                .Select((field, index) => field with { Id = index }).ToImmutableArray();
            ImmutableArray<WarpPortableMethodGraphDispatch> dispatchNodes = dispatches.Values.OrderBy(dispatch => dispatch.Slot, StringComparer.Ordinal)
                .ThenBy(dispatch => dispatch.ConcreteType, StringComparer.Ordinal).ToImmutableArray();
            IEnumerable<string> intrinsicIds = methodNodes.Where(method => method.Intrinsic is not null).Select(method => method.Intrinsic!);
            if (methodNodes.Any(method => method.SourceMethod.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.Synchronized)))
            {
                intrinsicIds = intrinsicIds.Append("warp.portable-intrinsic/method.monitor/0.1");
            }

            ImmutableArray<string> requiredIntrinsics = intrinsicIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
            foreach (Type type in typeSources.Values)
            {
                if (permitted.Contains(type.Assembly))
                {
                    ReadModuleImage(type.Module);
                }
            }

            string entryIdentity = WarpPortableMethodGraphIdentity.Method(entry);
            string hash = ComputeHash(entryIdentity, methodNodes, typeNodes, fieldNodes, dispatchNodes, requiredIntrinsics);
            return new WarpPortableMethodGraph(entryIdentity, hash, methodNodes, typeNodes, fieldNodes, dispatchNodes, requiredIntrinsics);
        }

        private string ComputeHash(string entryIdentity, ImmutableArray<WarpPortableMethodGraphMethod> methodNodes,
            ImmutableArray<WarpPortableMethodGraphType> typeNodes, ImmutableArray<WarpPortableMethodGraphField> fieldNodes,
            ImmutableArray<WarpPortableMethodGraphDispatch> dispatchNodes, ImmutableArray<string> requiredIntrinsics)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Version);
                writer.Write(WarpPortableMethodGraphIntrinsics.MathContract);
                writer.Write(entryIdentity);
                WriteModuleHashes(writer);
                writer.Write(methodNodes.Length);
                foreach (WarpPortableMethodGraphMethod method in methodNodes)
                {
                    WriteMethod(writer, method);
                }

                WriteTypes(writer, typeNodes);
                WriteFields(writer, fieldNodes);
                WriteDispatches(writer, dispatchNodes);
                WriteStrings(writer, requiredIntrinsics);
            }

            return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
        }

        private void WriteModuleHashes(BinaryWriter writer)
        {
            writer.Write(moduleImages.Count);
            foreach ((Module module, ImmutableArray<byte> image) in moduleImages.OrderBy(pair =>
                pair.Key.Assembly.FullName + "/" + pair.Key.Name + "/" + pair.Key.ModuleVersionId, StringComparer.Ordinal))
            {
                writer.Write(module.Assembly.FullName ?? string.Empty);
                writer.Write(module.Name);
                writer.Write(module.ModuleVersionId.ToByteArray());
                writer.Write(image.Length);
                if (!image.IsEmpty)
                {
                    writer.Write(SHA256.HashData(image.AsSpan()));
                }
            }
        }

        private static void WriteMethod(BinaryWriter writer, WarpPortableMethodGraphMethod method)
        {
            writer.Write(method.Identity);
            writer.Write(method.SourceMethod.Module.ModuleVersionId.ToByteArray());
            writer.Write(method.SourceMethod.MetadataToken);
            writer.Write((int)method.SourceMethod.Attributes);
            writer.Write((int)method.SourceMethod.GetMethodImplementationFlags());
            writer.Write((int)method.SourceMethod.CallingConvention);
            writer.Write(method.ReturnType);
            WriteStrings(writer, method.ParameterTypes);
            foreach (ParameterInfo parameter in method.SourceMethod.GetParameters())
            {
                WriteModifiers(writer, parameter.GetRequiredCustomModifiers(), parameter.GetOptionalCustomModifiers());
                writer.Write((int)parameter.Attributes);
            }

            if (method.SourceMethod is MethodInfo function)
            {
                WriteModifiers(writer, function.ReturnParameter.GetRequiredCustomModifiers(), function.ReturnParameter.GetOptionalCustomModifiers());
            }

            WriteStrings(writer, method.LocalTypes);
            writer.Write(method.MaximumStack);
            writer.Write(method.InitializeLocals);
            writer.Write(method.Cil.Length);
            writer.Write(method.Cil.AsSpan());
            writer.Write(method.Intrinsic ?? string.Empty);
            WriteStrings(writer, method.Dependencies);
            writer.Write(method.ExceptionRegions.Length);
            foreach (WarpPortableMethodGraphExceptionRegion region in method.ExceptionRegions)
            {
                writer.Write(region.Kind);
                writer.Write(region.TryOffset);
                writer.Write(region.TryLength);
                writer.Write(region.HandlerOffset);
                writer.Write(region.HandlerLength);
                writer.Write(region.FilterOffset);
                writer.Write(region.CatchType ?? string.Empty);
            }

            WriteInstructions(writer, method.Instructions);
        }

        private static void WriteInstructions(BinaryWriter writer, ImmutableArray<WarpPortableMethodGraphInstruction> instructions)
        {
            writer.Write(instructions.Length);
            foreach (WarpPortableMethodGraphInstruction instruction in instructions)
            {
                writer.Write(instruction.Offset);
                writer.Write(instruction.NextOffset);
                writer.Write(instruction.OpCode.Value);
                writer.Write(instruction.Operand);
                writer.Write(instruction.Method ?? string.Empty);
                writer.Write(instruction.Type ?? string.Empty);
                writer.Write(instruction.Field ?? string.Empty);
                writer.Write(instruction.StringLiteral ?? string.Empty);
                writer.Write(instruction.BranchTargets.Length);
                foreach (int target in instruction.BranchTargets)
                {
                    writer.Write(target);
                }
            }
        }

        private static void WriteTypes(BinaryWriter writer, ImmutableArray<WarpPortableMethodGraphType> types)
        {
            writer.Write(types.Length);
            foreach (WarpPortableMethodGraphType type in types)
            {
                writer.Write(type.Identity);
                writer.Write(type.SourceType.Module.ModuleVersionId.ToByteArray());
                writer.Write((int)type.SourceType.Attributes);
                writer.Write(type.BaseType ?? string.Empty);
                WriteStrings(writer, type.Interfaces);
                WriteStrings(writer, type.Fields);
                writer.Write(type.Initializer ?? string.Empty);
                writer.Write(type.Instantiated);
                writer.Write(type.LayoutKind);
                writer.Write(type.PackingSize);
                writer.Write(type.DeclaredSize);
                writer.Write(type.ElementType ?? string.Empty);
                writer.Write(type.ArrayRank);
                writer.Write(type.EnumUnderlyingType ?? string.Empty);
            }
        }

        private static void WriteFields(BinaryWriter writer, ImmutableArray<WarpPortableMethodGraphField> fields)
        {
            writer.Write(fields.Length);
            foreach (WarpPortableMethodGraphField field in fields)
            {
                writer.Write(field.Identity);
                writer.Write(field.DeclaringType);
                writer.Write(field.FieldType);
                writer.Write((int)field.SourceField.Attributes);
                WriteModifiers(writer, field.SourceField.GetRequiredCustomModifiers(), field.SourceField.GetOptionalCustomModifiers());
                writer.Write(field.IsStatic);
                writer.Write(field.IsReadOnly);
                writer.Write(field.IsLiteral);
                writer.Write(field.DeclaredOffset ?? -1);
                writer.Write(field.LiteralBits ?? string.Empty);
                writer.Write(field.InitializedData.Length);
                writer.Write(field.InitializedData.AsSpan());
            }
        }

        private static void WriteDispatches(BinaryWriter writer, ImmutableArray<WarpPortableMethodGraphDispatch> dispatches)
        {
            writer.Write(dispatches.Length);
            foreach (WarpPortableMethodGraphDispatch dispatch in dispatches)
            {
                writer.Write(dispatch.Slot);
                writer.Write(dispatch.ConcreteType);
                writer.Write(dispatch.Target);
            }
        }

        private static void WriteStrings(BinaryWriter writer, ImmutableArray<string> values)
        {
            writer.Write(values.Length);
            foreach (string value in values)
            {
                writer.Write(value);
            }
        }

        private static void WriteModifiers(BinaryWriter writer, Type[] required, Type[] optional)
        {
            WriteStrings(writer, required.Select(WarpPortableMethodGraphIdentity.Type).ToImmutableArray());
            WriteStrings(writer, optional.Select(WarpPortableMethodGraphIdentity.Type).ToImmutableArray());
        }
    }
}
