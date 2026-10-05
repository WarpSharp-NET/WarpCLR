using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceHeapSchema
{
    private sealed partial class Builder
    {
        private ImmutableArray<WarpPortableSourceMemoryView> MemoryViews(ImmutableArray<WarpPortableSourceHeapType> layouts)
        {
            var views = new HashSet<WarpPortableSourceMemoryView>();
            foreach (WarpPortableSourceHeapType layout in layouts)
            {
                WarpPortableTypedType? type = storage.GetValueOrDefault(layout.Identity);
                if (layout.Kind == WarpPortableHeapLayout.Value)
                {
                    AddView(views, layout.Id, WarpPortableSourceMemoryLayout.Instance, layout.Identity, 0, 0);
                }
                foreach (WarpPortableTypedField field in type?.Fields ?? [])
                {
                    uint kind = field.IsStatic ? WarpPortableSourceMemoryLayout.Static : WarpPortableSourceMemoryLayout.Instance;
                    uint offset = checked((uint)(field.ByteOffset + (field.IsStatic ? 0 : Prefix(sources[layout.Identity]))));
                    AddView(views, layout.Id, kind, field.TypeIdentity, offset, 0);
                }
                if (layout.ElementType != 0)
                {
                    string element = layouts[(int)layout.ElementType - 1].Identity;
                    AddView(views, layout.Id, WarpPortableSourceMemoryLayout.ArrayElement, element, 0, 0);
                }
            }
            return views.OrderBy(view => view.OwnerType).ThenBy(view => view.OwnerKind).ThenBy(view => view.ByteOffset)
                .ThenBy(view => view.ByteSpan).ThenBy(view => view.ElementType).ToImmutableArray();
        }

        private void AddView(HashSet<WarpPortableSourceMemoryView> views, uint owner, uint kind, string identity, uint offset, int depth)
        {
            if (depth > 64) { throw new WarpVerificationException("WRPCLR2400", "A source memory view exceeds its bounded value nesting.", 0); }
            WarpPortableTypedType? type = storage.GetValueOrDefault(identity);
            uint bytes = checked((uint)(type?.ByteSize ?? 12));
            views.Add(new(owner, kind, offset, bytes, typeIds[identity]));
            WarpCompilationAdmission.Require(graph.EntryIdentity, WarpCompilationResourceKind.VerifierWorkspaceSlots,
                checked((long)views.Count * WarpPortableSourceMemoryLayout.ViewWords), WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
            if (type?.Category == WarpPortableStackCategory.Value)
            {
                foreach (WarpPortableTypedField field in type.Fields.Where(field => !field.IsStatic))
                {
                    AddView(views, owner, kind, field.TypeIdentity, checked(offset + (uint)field.ByteOffset), depth + 1);
                }
            }
        }
    }
}
