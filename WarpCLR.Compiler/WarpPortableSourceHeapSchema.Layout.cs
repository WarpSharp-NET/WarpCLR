using System.Collections.Immutable;
using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceHeapSchema
{
    private sealed partial class Builder
    {
        private WarpPortableSourceHeapType Layout(string identity, uint id)
        {
            Type source = sources[identity];
            WarpPortableTypedType? typed = storage.GetValueOrDefault(identity);
            bool exception = typeof(Exception).IsAssignableFrom(source);
            bool delegateRecord = typeof(Delegate).IsAssignableFrom(source);
            bool typeRecord = source == typeof(Type);
            bool array = source.IsArray;
            if (array && source.GetArrayRank() > WarpPortableSourceArrayLayout.MaximumRank)
            {
                throw new WarpVerificationException("WRPCLR2400", "The source array rank exceeds its bounded exact rank/index schema.", 0);
            }
            Type? element = array ? source.GetElementType() : null;
            uint kind = source == typeof(string) ? WarpPortableHeapLayout.String : source.IsInterface ? WarpPortableHeapLayout.Interface :
                array ? element!.IsValueType ? WarpPortableHeapLayout.ValueArray : WarpPortableHeapLayout.ReferenceArray :
                source.IsValueType ? WarpPortableHeapLayout.Value : WarpPortableHeapLayout.Class;
            int bytes = source.IsValueType ? typed?.ByteSize ?? throw new WarpVerificationException("WRPCLR2400", "A source value type has no verified storage layout.", 0) :
                kind == WarpPortableHeapLayout.Class ? checked((typed?.InstanceByteSize ?? 0) + Prefix(source)) : 0;
            int staticBytes = typed?.Fields.Where(field => field.IsStatic).Select(field => checked(field.ByteOffset + field.ByteSize)).DefaultIfEmpty(0).Max() ?? 0;
            int elementBytes = array ? element!.IsValueType ? storage[WarpPortableMethodGraphIdentity.Type(element)].ByteSize : 12 : 0;
            uint stride = array ? element!.IsValueType ? Words(elementBytes) : 3 : 0;
            WarpPortableMethodGraphType? captured = metadata.GetValueOrDefault(identity);
            return new(id, identity, kind, bytes, Words(bytes), element is null ? 0 : Id(element), elementBytes, stride,
                staticBytes, Words(staticBytes), exception, delegateRecord, typeRecord,
                (source.Attributes & TypeAttributes.BeforeFieldInit) == TypeAttributes.BeforeFieldInit, captured?.Initializer,
                array ? (uint)source.GetArrayRank() : 0, source.IsSZArray,
                array && source.GetArrayRank() == 1 ? Id(element!.MakeArrayType()) : 0,
                typeIds.Where(pair => sources[pair.Key].IsAssignableFrom(source)).Select(pair => pair.Value).Order().ToImmutableArray(),
                InstanceReferences(typed, source), References(typed?.Fields.Where(field => field.IsStatic) ?? [], 0));
        }

        private ImmutableArray<WarpPortableHeapReferenceLayout> InstanceReferences(WarpPortableTypedType? type, Type source)
        {
            var roots = References(type?.Fields.Where(field => !field.IsStatic) ?? [], Prefix(source)).ToBuilder();
            if (typeof(Exception).IsAssignableFrom(source))
            {
                roots.Add(new(WarpPortableSourceExceptionLayout.MessageWord, Id(typeof(string))));
                roots.Add(new(WarpPortableSourceExceptionLayout.InnerExceptionWord, Id(typeof(Exception))));
                roots.Add(new(WarpPortableSourceExceptionLayout.TraceReferenceWord, Id(typeof(uint[]))));
                roots.Add(new(WarpPortableSourceExceptionLayout.ParamNameWord, Id(typeof(string))));
                roots.Add(new(WarpPortableSourceExceptionLayout.ActualValueWord, Id(typeof(object))));
                roots.Add(new(WarpPortableSourceExceptionLayout.TypeNameWord, Id(typeof(string))));
            }
            if (typeof(Delegate).IsAssignableFrom(source))
            {
                roots.Add(new(WarpPortableSourceDelegateLayout.TargetWord, Id(typeof(object))));
                roots.Add(new(WarpPortableSourceDelegateLayout.InvocationListWord, Id(typeof(Delegate[]))));
            }
            return roots.OrderBy(root => root.Offset).ToImmutableArray();
        }

        private static int Prefix(Type source) => typeof(Exception).IsAssignableFrom(source) ? WarpPortableSourceExceptionLayout.PrefixBytes :
            typeof(Delegate).IsAssignableFrom(source) ? WarpPortableSourceDelegateLayout.PrefixBytes : source == typeof(Type) ? WarpPortableSourceTypeObjectLayout.PrefixBytes : 0;

        private static uint Words(int bytes) => checked(((uint)bytes + 3) / 4);

        private void RequireMetadataBudget(ImmutableArray<WarpPortableSourceHeapType> types, int viewCount, int nullableCount)
        {
            long words = checked(WarpPortableHeapLayout.HeaderWords + (long)types.Length * WarpPortableHeapLayout.TypeWords + (long)types.Length * types.Length +
                types.Sum(type => type.StaticWords + (long)(type.References.Length + type.StaticReferences.Length) * 2) +
                WarpPortableSourceMemoryLayout.HeaderWords + (long)types.Length * WarpPortableSourceMemoryLayout.TypeWords +
                (long)viewCount * WarpPortableSourceMemoryLayout.ViewWords + (long)nullableCount * WarpPortableSourceMemoryLayout.NullableWords +
                (long)types.Length * WarpPortableSourceExceptionLayout.ExceptionTypeWords);
            WarpCompilationAdmission.Require(graph.EntryIdentity, WarpCompilationResourceKind.VerifierWorkspaceSlots, words,
                WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
        }
    }
}
