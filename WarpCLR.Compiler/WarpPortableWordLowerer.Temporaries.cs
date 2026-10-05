using System.Collections.Immutable;
using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    internal static WarpPortableWordBody PlanPrivateStorage(WarpPortableMethodGraph graph, WarpPortableTypedProgram program,
        string methodIdentity, int logicalFunction)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodIdentity); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(logicalFunction);
        if (!string.Equals(graph.GraphHash, program.GraphHash, StringComparison.Ordinal))
        {
            throw new WarpVerificationException("WRPCLR2300", "Private source storage requires its exact verified closure.", 0);
        }
        WarpPortableMethodGraphMethod captured = graph.Methods.FirstOrDefault(method => string.Equals(method.Identity, methodIdentity, StringComparison.Ordinal)) ??
            throw new WarpVerificationException("WRPCLR2300", "The private storage method is outside its captured closure.", 0);
        WarpPortableTypedMethod typed = program.Methods.FirstOrDefault(method => string.Equals(method.Identity, methodIdentity, StringComparison.Ordinal)) ??
            throw new WarpVerificationException("WRPCLR2300", "The private storage method has no exact typed source body.", 0);
        return new MethodLowerer(new(graph, program), captured, typed, logicalFunction - 1).Body;
    }

    private sealed partial class MethodLowerer
    {
        private readonly ImmutableArray<WarpPortableWordPrivateTemporary> temporaries;

        private ImmutableArray<WarpPortableWordPrivateTemporary> ConstructorTemporaries(ref int word)
        {
            var result = ImmutableArray.CreateBuilder<WarpPortableWordPrivateTemporary>();
            foreach (WarpPortableTypedInstruction instruction in method.Instructions.Where(instruction => instruction.Reachable && instruction.OpCode == OpCodes.Newobj.Value))
            {
                if (instruction.RequiredIntrinsic?.Contains("value-tuple.construct", StringComparison.Ordinal) == true) { continue; }
                WarpPortableMethodGraphInstruction original = source.Instructions.First(item => item.Offset == instruction.Offset);
                if (instruction.ExitStack.IsEmpty || original.Method is null ||
                    !owner.Types.TryGetValue(instruction.ExitStack[^1].TypeIdentity, out WarpPortableTypedType? type) ||
                    type.WordCount <= 0 || type.Category is WarpPortableStackCategory.ManagedByref or WarpPortableStackCategory.CliNativeInteger)
                {
                    throw Error("A constructor temporary requires the exact captured source object/value storage type.", instruction.Offset);
                }
                result.Add(new(result.Count, instruction.Offset, word, type, original.Method, TemporaryOwners(type, word, instruction.Offset)));
                word = checked(word + type.WordCount);
                WarpCompilationAdmission.Require(method.Identity, WarpCompilationResourceKind.ValueSlots,
                    word, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
            }
            return result.ToImmutable();
        }

        private void AddTemporaryRoots(Dictionary<int, WarpPortableWordRoot> roots)
        {
            foreach (WarpPortableWordPrivateTemporary temporary in temporaries)
            {
                foreach (WarpPortableWordTemporaryOwner field in temporary.Owners)
                {
                    roots.TryAdd(field.PrivateWordOffset,
                        new(field.PrivateWordOffset, new("temporary", temporary.Index, field.RelativeByteOffset / 4, false, field.Provenance)));
                }
            }
        }

        private ImmutableArray<WarpPortableWordTemporaryOwner> TemporaryOwners(WarpPortableTypedType type, int word, int sourceOffset)
        {
            var owners = ImmutableArray.CreateBuilder<WarpPortableWordTemporaryOwner>(type.ManagedRootByteOffsets.Length);
            foreach (int byteOffset in type.ManagedRootByteOffsets)
            {
                if (byteOffset < 0 || byteOffset % 4 != 0 || byteOffset + 12 > type.WordCount * 4)
                {
                    throw Error("A constructor owner must be an exact aligned three-word reference within its private storage.", sourceOffset);
                }
                (string leaf, ImmutableArray<string> path) = TemporaryOwnerType(type, byteOffset, sourceOffset);
                owners.Add(new(checked(word + byteOffset / 4), byteOffset, leaf, path, []));
            }
            return owners.MoveToImmutable();
        }

        private (string Identity, ImmutableArray<string> Path) TemporaryOwnerType(WarpPortableTypedType type, int byteOffset, int sourceOffset)
        {
            var path = ImmutableArray.CreateBuilder<string>();
            int depth = 0;
            while (type.Category == WarpPortableStackCategory.Value)
            {
                if (++depth > 64) { throw Error("A constructor owner exceeds bounded embedded value-field nesting.", sourceOffset); }
                WarpPortableTypedField[] fields = type.Fields.Where(field => !field.IsStatic && byteOffset >= field.ByteOffset &&
                    owner.Types[field.TypeIdentity].ManagedRootByteOffsets.Contains(byteOffset - field.ByteOffset)).ToArray();
                if (fields.Length != 1) { throw Error("A constructor owner requires one exact captured managed field path.", sourceOffset); }
                WarpPortableTypedField field = fields[0]; path.Add(field.Identity); byteOffset -= field.ByteOffset; type = owner.Types[field.TypeIdentity];
            }
            if (type.Category != WarpPortableStackCategory.Reference || byteOffset != 0)
            {
                throw Error("A constructor owner field must retain its exact managed reference type.", sourceOffset);
            }
            return (type.Identity, path.ToImmutable());
        }
    }
}
