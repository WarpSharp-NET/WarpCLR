using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceHeapSchema
{
    private sealed partial class Builder
    {
        private static readonly Type[] Builtins = [typeof(void), typeof(bool), typeof(byte), typeof(sbyte), typeof(char),
            typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double),
            typeof(object), typeof(string), typeof(Array), typeof(ValueType), typeof(Enum), typeof(Type), typeof(Exception),
            typeof(RuntimeTypeHandle), typeof(RuntimeFieldHandle), typeof(RuntimeMethodHandle), typeof(uint[]), typeof(Delegate), typeof(MulticastDelegate), typeof(Delegate[])];
        private static readonly Type[] FaultTypes = [typeof(NullReferenceException), typeof(IndexOutOfRangeException),
            typeof(OverflowException), typeof(DivideByZeroException), typeof(ArrayTypeMismatchException), typeof(InvalidCastException),
            typeof(TypeInitializationException), typeof(OutOfMemoryException), typeof(ArithmeticException),
            typeof(ArgumentException), typeof(ArgumentOutOfRangeException), typeof(ArgumentNullException), typeof(InvalidOperationException), typeof(StackOverflowException)];

        private Dictionary<string, Type> SourceTypes()
        {
            var result = graph.Types.ToDictionary(type => type.Identity, type => type.SourceType, StringComparer.Ordinal);
            foreach (Type source in Builtins.Concat(FaultTypes))
            {
                for (Type? current = source; current is not null; current = current.BaseType)
                {
                    result.TryAdd(WarpPortableMethodGraphIdentity.Type(current), current);
                }
            }
            int remaining = program.Types.Length;
            foreach (WarpPortableTypedType type in program.Types)
            {
                if (result.ContainsKey(type.Identity) || type.Category is WarpPortableStackCategory.FunctionTarget or WarpPortableStackCategory.CliNativeInteger) { remaining--; }
            }
            while (remaining != 0)
            {
                int before = remaining;
                foreach (WarpPortableTypedType type in program.Types)
                {
                    if (result.ContainsKey(type.Identity) || type.Category is WarpPortableStackCategory.FunctionTarget or WarpPortableStackCategory.CliNativeInteger ||
                        type.ElementType is not { } element || !result.TryGetValue(element, out Type? source)) { continue; }
                    Type? derived = type.Category == WarpPortableStackCategory.ManagedByref ? source.MakeByRefType() :
                        type.Category == WarpPortableStackCategory.Reference ? source.MakeArrayType() : null;
                    if (derived is null || !string.Equals(type.Identity, WarpPortableMethodGraphIdentity.Type(derived), StringComparison.Ordinal)) { continue; }
                    result.Add(type.Identity, derived); remaining--;
                }
                if (before == remaining)
                {
                    throw new WarpVerificationException("WRPCLR2400", "A verified source type cannot be resolved without inventing a layout.", 0);
                }
            }
            foreach (Type array in result.Values.Where(type => type.IsArray && !type.IsSZArray && type.GetArrayRank() == 1).ToArray())
            {
                Type vector = array.GetElementType()!.MakeArrayType();
                result.TryAdd(WarpPortableMethodGraphIdentity.Type(vector), vector);
            }
            WarpCompilationAdmission.Require(graph.EntryIdentity, WarpCompilationResourceKind.VerifierWorkspaceSlots,
                checked((long)result.Count * result.Count), WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
            return result;
        }
    }
}
