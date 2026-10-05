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
        private readonly WarpPortableCliSizeContract? cliSizes;
        private long workspace;
        private readonly Dictionary<string, WarpPortableTypedReturnSummary> summaries = new(StringComparer.Ordinal);

        public Builder(WarpPortableMethodGraph graph, WarpPortableCliSizeContract? cliSizes)
        {
            this.graph = graph;
            this.cliSizes = cliSizes;
            types = new(graph, cliSizes);
            methods = graph.Methods.ToDictionary(method => method.Identity, StringComparer.Ordinal);
        }

        public WarpPortableTypedProgram Verify()
        {
            ResolveReturnSummaries();
            var verified = ImmutableArray.CreateBuilder<WarpPortableTypedMethod>(graph.Methods.Length);
            foreach (WarpPortableMethodGraphMethod method in graph.Methods)
            {
                var verifier = new WarpPortableTypedMethodVerifier(graph, method, types, methods, summaries, cliSizes: cliSizes);
                workspace += verifier.Workspace;
                WarpCompilationAdmission.Require(method.Identity, WarpCompilationResourceKind.VerifierWorkspaceSlots,
                    workspace, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
                verified.Add(verifier.Verify());
            }

            ImmutableArray<WarpPortableTypedMethod> result = verified.MoveToImmutable();
            ImmutableArray<WarpPortableTypedType> snapshot = types.Snapshot();
            WarpPortableTypedInitializerTrigger? entry = WarpPortableTypeInitialization.ForMethod(graph,
                graph.Methods.First(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal)).SourceMethod);
            string hash = Hash(graph.GraphHash, snapshot, result, cliSizes, entry);
            return new(graph.GraphHash, hash, snapshot, result, cliSizes, entry);
        }

        private static string Hash(string graphHash, ImmutableArray<WarpPortableTypedType> types, ImmutableArray<WarpPortableTypedMethod> methods,
            WarpPortableCliSizeContract? cliSizes, WarpPortableTypedInitializerTrigger? entryInitializerTrigger = null)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                WarpPortableSnapshotIdentity.Write(writer, Version);
                WarpPortableSnapshotIdentity.Write(writer, WarpPortableSnapshotIdentity.Semantics);
                WarpPortableSnapshotIdentity.Write(writer, graphHash);
                WarpPortableSnapshotIdentity.Write(writer, WarpPortableTypeInitialization.Semantics);
                WriteInitializer(writer, entryInitializerTrigger);
                foreach (WarpPortableTypedType type in types)
                {
                    WriteType(writer, type);
                }

                foreach (WarpPortableTypedMethod method in methods)
                {
                    WriteMethod(writer, method);
                }
                if (cliSizes is not null) { WarpPortableSnapshotIdentity.Write(writer, WarpPortableCliSizeContract.Semantics); WarpPortableSnapshotIdentity.Write(writer, cliSizes.ContractHash); }
            }

            return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
        }

        private static void WriteType(BinaryWriter writer, WarpPortableTypedType type)
        {
            WarpPortableSnapshotIdentity.Write(writer, type.Identity); writer.Write((int)type.Category); writer.Write(type.StorageBits);
            writer.Write(type.IsSigned); writer.Write(type.ByteSize); writer.Write(type.Alignment);
            writer.Write(type.WordCount); writer.Write(type.InstanceByteSize); WarpPortableSnapshotIdentity.Write(writer, type.ElementType ?? string.Empty);
            writer.Write(type.Fields.Length);
            foreach (WarpPortableTypedField field in type.Fields)
            {
                WarpPortableSnapshotIdentity.Write(writer, field.Identity); WarpPortableSnapshotIdentity.Write(writer, field.TypeIdentity); writer.Write(field.ByteOffset);
                writer.Write(field.ByteSize); writer.Write(field.IsStatic); writer.Write(field.IsReadOnly);
            }

            WriteIntegers(writer, type.ManagedRootByteOffsets);
        }

        private static void WriteMethod(BinaryWriter writer, WarpPortableTypedMethod method)
        {
            WarpPortableSnapshotIdentity.Write(writer, method.Identity); WriteStrings(writer, method.ArgumentTypes); WriteStrings(writer, method.LocalTypes);
            WarpPortableSnapshotIdentity.Write(writer, method.ReturnType); writer.Write(method.MaximumStackWords); writer.Write(method.PrivateStorageWords);
            WarpPortableSnapshotIdentity.Write(writer, method.Intrinsic ?? string.Empty); WriteProvenance(writer, method.ReturnSummary.Origins);
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
            WarpPortableSnapshotIdentity.Write(writer, instruction.MemoryType ?? string.Empty); writer.Write(instruction.StorageBits);
            writer.Write(instruction.ReadOnlyAccess); WarpPortableSnapshotIdentity.Write(writer, instruction.RequiredIntrinsic ?? string.Empty);
            WriteInitializer(writer, instruction.InitializerTrigger);
        }

        private static void WriteInitializer(BinaryWriter writer, WarpPortableTypedInitializerTrigger? trigger)
        {
            writer.Write(trigger is not null);
            if (trigger is null) { return; }
            WarpPortableSnapshotIdentity.Write(writer, trigger.DeclaringType);
            WarpPortableSnapshotIdentity.Write(writer, trigger.Initializer);
            writer.Write((int)trigger.Kind); writer.Write(trigger.BeforeFieldInit);
        }

        private static void WriteValues(BinaryWriter writer, ImmutableArray<WarpPortableTypedValue> values)
        {
            writer.Write(values.Length);
            foreach (WarpPortableTypedValue value in values)
            {
                WarpPortableSnapshotIdentity.Write(writer, value.TypeIdentity); writer.Write((int)value.Category); writer.Write(value.WordCount);
                writer.Write(value.IsReadOnly); writer.Write(value.ControlledMutability); writer.Write(value.IsUninitializedThis); writer.Write(value.IsNull);
                WarpPortableSnapshotIdentity.Write(writer, value.MethodTarget ?? string.Empty); WarpPortableSnapshotIdentity.Write(writer, value.SourceStorageType ?? string.Empty); WriteProvenance(writer, value.Provenance);
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
                WarpPortableSnapshotIdentity.Write(writer, root.Storage); writer.Write(root.Slot); writer.Write(root.WordOffset);
                writer.Write(root.IsInteriorOwner); WriteProvenance(writer, root.Provenance);
            }
        }

        private static void WriteProvenance(BinaryWriter writer, ImmutableArray<WarpPortableTypedProvenance> provenance)
        {
            writer.Write(provenance.Length);
            foreach (WarpPortableTypedProvenance origin in provenance)
            {
                writer.Write((int)origin.Kind); WarpPortableSnapshotIdentity.Write(writer, origin.OwnerMethod); writer.Write(origin.OwnerIndex);
                WarpPortableSnapshotIdentity.Write(writer, origin.OwnerType); writer.Write(origin.ByteOffset); writer.Write(origin.ByteLength);
            }
        }

        private static void WriteStrings(BinaryWriter writer, ImmutableArray<string> values)
        {
            writer.Write(values.Length);
            foreach (string value in values) { WarpPortableSnapshotIdentity.Write(writer, value); }
        }

        private static void WriteIntegers(BinaryWriter writer, ImmutableArray<int> values)
        {
            writer.Write(values.Length);
            foreach (int value in values) { writer.Write(value); }
        }
    }
}
