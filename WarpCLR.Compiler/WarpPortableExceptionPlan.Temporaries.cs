using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableExceptionPlan
{
    private sealed partial class Builder
    {
        private void ValidateTemporaryLayout(WarpPortableExceptionBodyBinding body)
        {
            WarpPortableWordBody planned = WarpPortableWordLowerer.PlanPrivateStorage(graph, typed,
                body.MethodIdentity, Math.Max(1, body.Function));
            if (planned.PrivateTemporaries.IsEmpty && body.PrivateTemporaries.IsEmpty) { return; }
            if (body.PrivateWords != planned.PrivateWordCount || body.EvaluationWordOffset != planned.EvaluationWordOffset ||
                !WarpPortableSnapshotIdentity.Serialize(body.PrivateTemporaries).AsSpan().SequenceEqual(
                    WarpPortableSnapshotIdentity.Serialize(planned.PrivateTemporaries)))
            {
                throw Invalid("Constructor retirement requires the compiler-owned exact original newobj temporary and embedded-owner layout.");
            }
        }

        private void WriteTemporaryOwners()
        {
            words[(int)WarpPortableExceptionLayout.TemporaryOwnerStart] = Next;
            uint count = 0;
            foreach (WarpPortableExceptionBodyBinding body in bodies)
            {
                foreach (WarpPortableWordPrivateTemporary temporary in body.PrivateTemporaries)
                {
                    if (!sites.TryGetValue((body.Function, temporary.SourceOffset), out uint siteId)) { continue; }
                    uint site = words[(int)WarpPortableExceptionLayout.SiteStart] + (siteId - 1) * WarpPortableExceptionLayout.SiteWords;
                    Set(site, WarpPortableExceptionLayout.SiteTemporaryStart, Next);
                    Set(site, WarpPortableExceptionLayout.SiteTemporaryCount, (uint)temporary.Owners.Length);
                    foreach (WarpPortableWordTemporaryOwner owner in temporary.Owners)
                    {
                        uint row = Allocate(WarpPortableExceptionLayout.TemporaryOwnerWords); count++;
                        Set(row, WarpPortableExceptionLayout.TemporaryFunction, (uint)body.Function);
                        Set(row, WarpPortableExceptionLayout.TemporarySite, siteId);
                        Set(row, WarpPortableExceptionLayout.TemporaryIndex, (uint)temporary.Index);
                        Set(row, WarpPortableExceptionLayout.TemporaryPrivateOffset, (uint)owner.PrivateWordOffset);
                        Set(row, WarpPortableExceptionLayout.TemporaryRelativeByteOffset, (uint)owner.RelativeByteOffset);
                        Set(row, WarpPortableExceptionLayout.TemporaryType, schema.TypeId(owner.TypeIdentity));
                        Set(row, WarpPortableExceptionLayout.TemporarySpan, (uint)temporary.Type.WordCount);
                        Set(row, WarpPortableExceptionLayout.TemporaryStart, (uint)temporary.WordOffset);
                    }
                }
            }
            words[(int)WarpPortableExceptionLayout.TemporaryOwnerCount] = count;
        }
    }
}
