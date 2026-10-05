using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourcePlan
{
    internal uint[] ProjectRoots(uint[] state, uint[][] inputs, uint worker)
    {
        uint[] roots = new uint[RootBankWords];
        int next = 0;
        int physicalDepth = checked((int)state[WarpLogicalMachineLayout.DepthOffset]);
        for (int depth = 0; depth < physicalDepth; depth++)
        {
            int frame = checked(WarpLogicalMachineLayout.HeaderWords + depth * Layout.FrameWords);
            int function = checked((int)state[frame + WarpLogicalMachineLayout.FrameFunctionOffset]);
            int pc = checked((int)state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]);
            WarpLogicalMachineNode node = Layout.Nodes[pc];
            if (sourceBlocks.TryGetValue((function, node.Block), out WarpPortableWordSourceBlock? block))
            {
                foreach (WarpPortableWordRoot root in block.Roots)
                {
                    if (root.Source.IsInteriorOwner)
                    {
                        uint[] capability = Enumerable.Range(root.PrivateWordOffset, 6)
                            .Select(word => ReadPrivateWord(state, frame, word)).ToArray();
                        CopyFrameInterior(state, frame, capability, RootPointee(bodies[function], block, root),
                            root.Source.Provenance, roots, ref next);
                    }
                    else
                    {
                        uint[] tuple = Enumerable.Range(root.PrivateWordOffset, 3)
                            .Select(word => ReadPrivateWord(state, frame, word)).ToArray();
                        CopyRoot(tuple, roots, ref next);
                    }
                }
                if (depth + 1 == physicalDepth)
                {
                    CopyReturnedRoots(state, frame, node, block, block.ReturnedRoots, roots, ref next);
                }
            }
            else if (function == 0 && depth + 1 == physicalDepth)
            {
                CopyReturnedRoots(state, frame, node, null, Program.EntryProjection.WrapperReturnedRoots, roots, ref next);
            }
        }
        if (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
        {
            foreach (int offset in InputRootWords)
            {
                CopyRoot([inputs[offset][worker], inputs[offset + 1][worker], inputs[offset + 2][worker]], roots, ref next);
            }
        }
        if (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Completed)
        {
            foreach (WarpPortableWordResultRoot root in Program.EntryProjection.ResultRoots)
            {
                uint[] tuple = Enumerable.Range(root.ResultWordOffset, 3)
                    .Select(word => state[Layout.GetResultWordOffset(word, MaximumDepth)]).ToArray();
                CopyRoot(tuple, roots, ref next);
            }
        }
        return roots;
    }

    private void CopyReturnedRoots(uint[] state, int frame, WarpLogicalMachineNode node,
        WarpPortableWordSourceBlock? source,
        System.Collections.Immutable.ImmutableArray<WarpPortableWordTransientRoot> returned, uint[] roots, ref int next)
    {
        if (node.StartsBlock || node.ProgramCounter == 0) { return; }
        WarpLogicalMachineNode predecessor = Layout.Nodes[node.ProgramCounter - 1];
        if (predecessor.Function != node.Function || predecessor.Continuation != node.ProgramCounter ||
            predecessor.Call is not { } call) { return; }
        foreach (WarpPortableWordTransientRoot root in returned)
        {
            int words = root.IsInteriorOwner ? 6 : 3;
            if (root.SsaWordOffset >= call.Result && root.SsaWordOffset <= call.Result + call.ResultWordCount - words)
            {
                ReadOnlySpan<uint> value = state.AsSpan(checked(frame + WarpLogicalMachineLayout.FrameHeaderWords + root.SsaWordOffset), words);
                if (root.IsInteriorOwner)
                {
                    CopyFrameInterior(state, frame, value, ReturnedPointee(source ??
                        throw new InvalidOperationException("An external interior return is outside the admitted source frame domain.")),
                        root.Provenance, roots, ref next);
                }
                else { CopyRoot(value, roots, ref next); }
            }
        }
    }

    private void RequireBoundOwners(WarpPortableWordLoweredProgram program)
    {
        if (program.EntryProjection.WrapperInputRoots.Any(root => root.IsInteriorOwner) ||
            program.EntryProjection.ResultRoots.Any(root => root.IsInteriorOwner))
        {
            throw new WarpVerificationException("WRPCLR2400", "External byref arguments and results require their separately admitted owner domain.");
        }
        var interiors = program.Bodies.SelectMany(body => body.SourceBlocks).SelectMany(block =>
            block.Roots.Where(root => root.Source.IsInteriorOwner).Select(root => root.Source.Provenance)
                .Concat(block.ReturnedRoots.Where(root => root.IsInteriorOwner).Select(root => root.Provenance))).ToArray();
        if (interiors.Length != 0 && (FrameSchema is null || !program.RequiredServices.Any(service =>
                service.StartsWith(WarpPortableClosedFrameSourcePlan.Semantics + "/", StringComparison.Ordinal)) ||
            interiors.SelectMany(origins => origins).Any(origin => origin.Kind is
                WarpPortableProvenanceKind.HeapInterior or WarpPortableProvenanceKind.StaticStorage)))
        {
            throw new WarpVerificationException("WRPCLR2400", "Compiled source frame roots require the exact closed frame binding; heap and static interiors need their own admission.");
        }
    }

    private static void CopyRoot(ReadOnlySpan<uint> source, uint[] destination, ref int next)
    {
        if (next > destination.Length - 3) { throw new InvalidOperationException("Precise source roots exceed their admitted tuple reservation."); }
        source.CopyTo(destination.AsSpan(next, 3));
        next += 3;
    }
}
