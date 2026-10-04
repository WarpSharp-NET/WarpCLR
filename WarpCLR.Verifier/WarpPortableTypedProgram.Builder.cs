using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedProgram
{
    private sealed partial class Builder
    {
        private readonly WarpPortableMethodGraph graph;
        private readonly WarpPortableTypedTypeCatalog types;
        private readonly Dictionary<string, WarpPortableMethodGraphMethod> methods;
        private long workspace;
        private readonly Dictionary<string, WarpPortableTypedReturnSummary> summaries = new(StringComparer.Ordinal);

        public Builder(WarpPortableMethodGraph graph)
        {
            this.graph = graph;
            types = new(graph);
            methods = graph.Methods.ToDictionary(method => method.Identity, StringComparer.Ordinal);
        }

        public WarpPortableTypedProgram Verify()
        {
            ResolveReturnSummaries();
            var verified = ImmutableArray.CreateBuilder<WarpPortableTypedMethod>(graph.Methods.Length);
            foreach (WarpPortableMethodGraphMethod method in graph.Methods)
            {
                var verifier = new WarpPortableTypedMethodVerifier(graph, method, types, methods, summaries);
                workspace += verifier.Workspace;
                WarpCompilationAdmission.Require(method.Identity, WarpCompilationResourceKind.VerifierWorkspaceSlots,
                    workspace, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
                verified.Add(verifier.Verify());
            }

            ImmutableArray<WarpPortableTypedMethod> result = verified.MoveToImmutable();
            ImmutableArray<WarpPortableTypedType> snapshot = types.Snapshot();
            string hash = Hash(graph.GraphHash, snapshot, result);
            return new(graph.GraphHash, hash, snapshot, result);
        }

        private static string Hash(string graphHash, ImmutableArray<WarpPortableTypedType> types, ImmutableArray<WarpPortableTypedMethod> methods)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Version);
                writer.Write(graphHash);
                foreach (WarpPortableTypedType type in types)
                {
                    WriteType(writer, type);
                }

                foreach (WarpPortableTypedMethod method in methods)
                {
                    WriteMethod(writer, method);
                }
            }

            return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
        }

        private static void WriteType(BinaryWriter writer, WarpPortableTypedType type)
        {
            writer.Write(type.Identity); writer.Write((int)type.Category); writer.Write(type.StorageBits);
            writer.Write(type.IsSigned); writer.Write(type.ByteSize); writer.Write(type.Alignment);
            writer.Write(type.WordCount); writer.Write(type.InstanceByteSize); writer.Write(type.ElementType ?? string.Empty);
            writer.Write(type.Fields.Length);
            foreach (WarpPortableTypedField field in type.Fields)
            {
                writer.Write(field.Identity); writer.Write(field.TypeIdentity); writer.Write(field.ByteOffset);
                writer.Write(field.ByteSize); writer.Write(field.IsStatic); writer.Write(field.IsReadOnly);
            }

            WriteIntegers(writer, type.ManagedRootByteOffsets);
        }

        private static void WriteMethod(BinaryWriter writer, WarpPortableTypedMethod method)
        {
            writer.Write(method.Identity); WriteStrings(writer, method.ArgumentTypes); WriteStrings(writer, method.LocalTypes);
            writer.Write(method.ReturnType); writer.Write(method.MaximumStackWords); writer.Write(method.PrivateStorageWords);
            writer.Write(method.Intrinsic ?? string.Empty); WriteProvenance(writer, method.ReturnSummary.Origins);
            writer.Write(method.ReturnSummary.ReadOnly); writer.Write(method.Instructions.Length);
            foreach (WarpPortableTypedInstruction instruction in method.Instructions)
            {
                WriteInstruction(writer, instruction);
            }
        }

        private static void WriteInstruction(BinaryWriter writer, WarpPortableTypedInstruction instruction)
        {
            writer.Write(instruction.Offset); writer.Write(instruction.NextOffset); writer.Write(instruction.OpCode); writer.Write(instruction.Reachable);
            WriteValues(writer, instruction.EntryStack); WriteValues(writer, instruction.ExitStack);
            WriteSlots(writer, instruction.EntryArguments); WriteSlots(writer, instruction.EntryLocals);
            writer.Write(instruction.ExceptionMemberships.Length);
            foreach (WarpPortableTypedExceptionMembership membership in instruction.ExceptionMemberships)
            {
                writer.Write(membership.Region); writer.Write((int)membership.Role);
            }

            WriteIntegers(writer, instruction.Successors); WriteIntegers(writer, instruction.ExceptionalSuccessors);
            WriteIntegers(writer, instruction.UnwindRegions); WriteIntegers(writer, instruction.Effects.Select(effect => (int)effect).ToImmutableArray());
            writer.Write(instruction.Faults.Length);
            foreach (WarpPortableTypedFault fault in instruction.Faults)
            {
                writer.Write((int)fault.Kind); writer.Write(fault.SourceOffset); writer.Write(fault.EffectIndex);
            }

            WriteRoots(writer, instruction.Roots);
            writer.Write(instruction.MemoryType ?? string.Empty); writer.Write(instruction.StorageBits);
            writer.Write(instruction.ReadOnlyAccess); writer.Write(instruction.RequiredIntrinsic ?? string.Empty);
        }

        private static void WriteValues(BinaryWriter writer, ImmutableArray<WarpPortableTypedValue> values)
        {
            writer.Write(values.Length);
            foreach (WarpPortableTypedValue value in values)
            {
                writer.Write(value.TypeIdentity); writer.Write((int)value.Category); writer.Write(value.WordCount);
                writer.Write(value.IsReadOnly); writer.Write(value.IsUninitializedThis); writer.Write(value.IsNull);
                writer.Write(value.MethodTarget ?? string.Empty); writer.Write(value.SourceStorageType ?? string.Empty); WriteProvenance(writer, value.Provenance);
            }
        }

        private static void WriteSlots(BinaryWriter writer, ImmutableArray<WarpPortableTypedSlot> slots)
        {
            writer.Write(slots.Length);
            foreach (WarpPortableTypedSlot slot in slots)
            {
                WriteValues(writer, [slot.Value]); writer.Write(slot.InitializedBytes.Length);
                foreach (bool initialized in slot.InitializedBytes) { writer.Write(initialized); }
            }
        }

        private static void WriteRoots(BinaryWriter writer, ImmutableArray<WarpPortableTypedRoot> roots)
        {
            writer.Write(roots.Length);
            foreach (WarpPortableTypedRoot root in roots)
            {
                writer.Write(root.Storage); writer.Write(root.Slot); writer.Write(root.WordOffset);
                writer.Write(root.IsInteriorOwner); WriteProvenance(writer, root.Provenance);
            }
        }

        private static void WriteProvenance(BinaryWriter writer, ImmutableArray<WarpPortableTypedProvenance> provenance)
        {
            writer.Write(provenance.Length);
            foreach (WarpPortableTypedProvenance origin in provenance)
            {
                writer.Write((int)origin.Kind); writer.Write(origin.OwnerMethod); writer.Write(origin.OwnerIndex);
                writer.Write(origin.OwnerType); writer.Write(origin.ByteOffset); writer.Write(origin.ByteLength);
            }
        }

        private static void WriteStrings(BinaryWriter writer, ImmutableArray<string> values)
        {
            writer.Write(values.Length);
            foreach (string value in values) { writer.Write(value); }
        }

        private static void WriteIntegers(BinaryWriter writer, ImmutableArray<int> values)
        {
            writer.Write(values.Length);
            foreach (int value in values) { writer.Write(value); }
        }
    }
}
