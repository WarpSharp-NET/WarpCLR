using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableMetadataUtf16Cases
{
    internal static void CapturedLiteralAndMemberSnapshotsDistinguishRawValuesBeforeAdmission()
    {
        MethodInfo entry = typeof(Sources).GetMethod(nameof(Sources.RawLiteral))!;
        Type builderType = typeof(WarpPortableMethodGraph).GetNestedType("Builder", BindingFlags.NonPublic)!;
        ConstructorInfo[] constructors = builderType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.HasCount(1, constructors);
        object builder = constructors[0].Invoke([entry, null, null]);
        WarpPortableMethodGraph graph = (WarpPortableMethodGraph)builderType.GetMethod("Discover")!.Invoke(builder, null)!;
        WarpPortableMethodGraphMethod method = graph.Methods.First(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal));
        WarpPortableMethodGraphInstruction[] literals = method.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ldstr).ToArray();
        Assert.HasCount(1, literals);
        Assert.AreEqual("\uD800", literals[0].StringLiteral, StringComparer.Ordinal);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        Assert.AreEqual(WarpPortableStackCategory.Reference, typed.Types.First(type => string.Equals(type.Identity, method.ReturnType, StringComparison.Ordinal)).Category);
        Assert.AreEqual(graph.GraphHash, schema.GraphHash, StringComparer.Ordinal);
        Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(graph, typed));
        MethodInfo hash = builderType.GetMethod("ComputeHash", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (string member in new[] { "literal", "method", "field", "type", "dispatch" })
        {
            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in Collisions)
            {
                ImmutableArray<WarpPortableMethodGraphMethod> methods = graph.Methods;
                ImmutableArray<WarpPortableMethodGraphType> types = graph.Types;
                ImmutableArray<WarpPortableMethodGraphField> fields = graph.Fields;
                ImmutableArray<WarpPortableMethodGraphDispatch> dispatches = graph.Dispatches;
                if (string.Equals(member, "literal", StringComparison.Ordinal))
                {
                    methods = methods.Select(current => current with { Instructions = current.Instructions.Select(instruction =>
                        instruction.OpCode == OpCodes.Ldstr ? instruction with { StringLiteral = raw } : instruction).ToImmutableArray() }).ToImmutableArray();
                }
                else if (string.Equals(member, "method", StringComparison.Ordinal)) { methods = methods.SetItem(0, methods[0] with { Identity = raw }); }
                else if (string.Equals(member, "type", StringComparison.Ordinal)) { types = types.SetItem(0, types[0] with { Identity = raw }); }
                else if (string.Equals(member, "field", StringComparison.Ordinal))
                {
                    Assert.IsFalse(fields.IsEmpty); fields = fields.SetItem(0, fields[0] with { LiteralBits = "string:" + raw });
                }
                else { dispatches = [new("slot:" + raw, "concrete", "target")]; }
                // Mutated snapshots prove this exact private hash implementation;
                // they are never constructed into or admitted as executable graphs.
                values.Add((string)hash.Invoke(builder, [graph.EntryIdentity, methods, types, fields, dispatches, graph.Intrinsics])!);
            }
            Assert.HasCount(Collisions.Length, values);
        }
    }

    internal static void TypedSnapshotAndRootProvenanceKeepExactRawIdentityUnits()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Sources).GetMethod(nameof(Sources.Borrow))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        Type builder = typeof(WarpPortableTypedProgram).GetNestedType("Builder", BindingFlags.NonPublic)!;
        MethodInfo hash = builder.GetMethod("Hash", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (string member in new[] { "type", "method", "owner" })
        {
            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in Collisions)
            {
                ImmutableArray<WarpPortableTypedType> types = typed.Types;
                ImmutableArray<WarpPortableTypedMethod> methods = typed.Methods;
                if (string.Equals(member, "type", StringComparison.Ordinal)) { types = types.SetItem(0, types[0] with { Identity = raw }); }
                else if (string.Equals(member, "method", StringComparison.Ordinal)) { methods = methods.SetItem(0, methods[0] with { Identity = raw }); }
                else
                {
                    WarpPortableTypedMethod method = methods[0];
                    method = method with { ReturnSummary = method.ReturnSummary with { Origins = method.ReturnSummary.Origins
                        .Select(origin => origin with { OwnerMethod = raw, OwnerType = raw }).ToImmutableArray() } };
                    Assert.IsFalse(method.ReturnSummary.Origins.IsEmpty); methods = methods.SetItem(0, method);
                }
                values.Add((string)hash.Invoke(null, [graph.GraphHash, types, methods, typed.CliSizes, typed.EntryInitializerTrigger])!);
            }
            Assert.HasCount(Collisions.Length, values);
        }
    }

    private static class Sources
    {
        public const string RawField = "\uD800";
        public static string RawLiteral() => RawField;
        public static ref uint Borrow(ref uint value) => ref value;
    }
}
