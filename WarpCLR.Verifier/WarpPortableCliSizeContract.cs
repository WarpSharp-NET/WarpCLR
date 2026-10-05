using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed class WarpPortableCliSizeContract
{
    internal const string Semantics = "warp.cli-size/coreclr64-captured-metadata-numeric-size-native-evaluation-separate-portable-owner-layout/0.2";

    private WarpPortableCliSizeContract(WarpPortableMethodGraph graph, ImmutableArray<WarpPortableCliTypeSize> sizes)
    {
        GraphHash = graph.GraphHash; Sizes = sizes; NativeEvaluationBits = 64;
        RuntimeProfile = RuntimeInformation.FrameworkDescription + "/" + RuntimeInformation.ProcessArchitecture + "/pointer64";
        ContractHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { Semantics, GraphHash, RuntimeProfile, PointerBytes = 8, NativeEvaluationBits, NativeEvaluationSemantics = WarpPortableCliNativeInteger.Semantics, Sizes })));
    }

    internal string GraphHash { get; }
    internal string ContractHash { get; }
    internal string RuntimeProfile { get; }
    internal int NativeEvaluationBits { get; }
    internal ImmutableArray<WarpPortableCliTypeSize> Sizes { get; }

    internal static WarpPortableCliSizeContract Capture(WarpPortableMethodGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (IntPtr.Size != 8 || !RuntimeInformation.FrameworkDescription.StartsWith(".NET 10.", StringComparison.Ordinal) ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new WarpVerificationException("WRPCLR2210", "CLI numeric layout capture requires the explicit .NET10 CoreCLR64 x64 semantic profile.", 0);
        }
        Dictionary<string, WarpPortableMethodGraphType> types = graph.Types.ToDictionary(type => type.Identity, StringComparer.Ordinal);
        var pending = new Queue<Type>();
        foreach (WarpPortableMethodGraphInstruction instruction in graph.Methods.SelectMany(method => method.Instructions)
                     .Where(instruction => instruction.OpCode == OpCodes.Sizeof))
        {
            if (instruction.Type is null || !types.TryGetValue(instruction.Type, out WarpPortableMethodGraphType? captured))
            {
                throw new WarpVerificationException("WRPCLR2210", "A source sizeof token has no captured closed metadata type.", instruction.Offset);
            }
            pending.Enqueue(captured.SourceType);
        }
        var sizes = new Dictionary<string, WarpPortableCliTypeSize>(StringComparer.Ordinal);
        while (pending.TryDequeue(out Type? type))
        {
            string identity = WarpPortableMethodGraphIdentity.Type(type);
            if (sizes.ContainsKey(identity)) { continue; }
            if (type == typeof(void) || type.IsByRef || type.IsPointer || type.IsFunctionPointer || type.IsByRefLike || type.ContainsGenericParameters)
            {
                throw new WarpVerificationException("WRPCLR2210", "CLI size capture cannot admit void/open/byref-like/unsafe source types.", 0);
            }
            uint bytes = CaptureSize(type);
            WarpCompilationAdmission.Require(graph.EntryIdentity, WarpCompilationResourceKind.ValueSlots,
                bytes, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
            if (bytes == 0 || !type.IsValueType && bytes != 8)
            {
                throw new WarpVerificationException("WRPCLR2210", "The captured CLI size violates its CoreCLR64 semantic profile.", 0);
            }
            var fields = ImmutableArray.CreateBuilder<WarpPortableCliFieldSize>();
            if (type.IsValueType && !type.IsPrimitive && !type.IsEnum)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                             .OrderBy(field => field.MetadataToken))
                {
                    WarpPortableMethodGraphIdentity.ValidateTypeShape(field.FieldType);
                    uint offset = CaptureFieldOffset(type, field);
                    uint fieldBytes = CaptureSize(field.FieldType);
                    if (offset > bytes || fieldBytes > bytes - offset)
                    {
                        throw new WarpVerificationException("WRPCLR2210", "A captured CLI field exceeds its exact managed value layout.", 0);
                    }
                    fields.Add(new(WarpPortableMethodGraphIdentity.Field(field), offset, fieldBytes));
                    pending.Enqueue(field.FieldType);
                }
            }
            sizes.Add(identity, new(identity, bytes, type.IsValueType, fields.ToImmutable()));
            WarpCompilationAdmission.Require(graph.EntryIdentity, WarpCompilationResourceKind.VerifierWorkspaceSlots,
                sizes.Count + sizes.Values.Sum(size => (long)size.Fields.Length), WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
        }
        return new(graph, sizes.Values.OrderBy(size => size.Identity, StringComparer.Ordinal).ToImmutableArray());
    }

    internal WarpPortableCliTypeSize TypeSize(string identity, int sourceOffset) =>
        Sizes.FirstOrDefault(size => string.Equals(size.Identity, identity, StringComparison.Ordinal)) ??
        throw new WarpVerificationException("WRPCLR2210", "A source sizeof has no exact captured CLI numeric size binding.", sourceOffset);

    internal void RequireGraph(WarpPortableMethodGraph graph)
    {
        if (!string.Equals(GraphHash, graph.GraphHash, StringComparison.Ordinal))
        {
            throw new WarpVerificationException("WRPCLR2210", "The CLI numeric layout contract belongs to a different captured source closure.", 0);
        }
    }

    private static uint CaptureSize(Type type)
    {
        var method = new DynamicMethod("warp_cli_metadata_size", typeof(uint), Type.EmptyTypes, typeof(WarpPortableCliSizeContract).Module, skipVisibility: true);
        ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Sizeof, type); il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<uint>>()();
    }

    private static uint CaptureFieldOffset(Type type, FieldInfo field)
    {
        // Both addresses refer to the same zeroed private value local; the emitted body has no calls, allocations,
        // static field accesses or constructor invocation. Numeric offsets never expose a source/host address.
        var method = new DynamicMethod("warp_cli_metadata_field_offset", typeof(uint), Type.EmptyTypes, typeof(WarpPortableCliSizeContract).Module, skipVisibility: true);
        ILGenerator il = method.GetILGenerator(); LocalBuilder value = il.DeclareLocal(type);
        il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Ldflda, field); il.Emit(OpCodes.Conv_U);
        il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Conv_U); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Conv_U4); il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<uint>>()();
    }
}
