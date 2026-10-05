using System.Collections.Immutable;
using System.Reflection.Emit;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableExceptionPlan
{
    private sealed partial class Builder
    {
        internal ImmutableArray<uint> Build()
        {
            ValidateBindings(); WriteBodies(); WriteClauses(); WriteSites(); WritePcs(); WriteLists(); WriteFaults(); WriteTemporaryOwners();
            words[0] = WarpPortableExceptionLayout.Magic; words[1] = WarpPortableExceptionLayout.Version;
            words[(int)WarpPortableExceptionLayout.TraceVersion] = WarpPortableExceptionTraceLayout.Version;
            words[(int)WarpPortableExceptionLayout.MetadataEnd] = Next;
            return words.ToImmutableArray();
        }

        private void ValidateBindings()
        {
            if (bodies.IsEmpty || bodies.Select(body => body.Function).Distinct().Count() != bodies.Length)
            {
                throw Invalid("Each source function requires one immutable EH body binding.");
            }
            foreach (WarpPortableExceptionBodyBinding body in bodies)
            {
                if (body.Function < 0 || body.Function > layout.Kernel.Functions.Count || layout.IsRuntimeHelper(body.Function) ||
                    layout.CountsSourceDepth(body.Function) != (body.AliasOwnerFunction == -1) ||
                    layout.GetAliasOwnerFunction(body.Function) != body.AliasOwnerFunction ||
                    layout.GetAliasPrefixWords(body.Function) != body.AliasPrefixWords ||
                    body.PrivateWords != layout.GetPrivateWordCount(body.Function) || body.EvaluationWordOffset < 0 ||
                    body.EvaluationWordOffset > body.PrivateWords)
                {
                    throw Invalid("EH source bindings require exact nonhelper private storage and a complete exception-owner triple.");
                }
                WarpPortableTypedMethod method = Typed(body.MethodIdentity);
                ValidateTemporaryLayout(body);
                if (Method(body.MethodIdentity).ExceptionRegions.Any(region => region.Kind is 0 or 1) &&
                    body.PrivateWords - body.EvaluationWordOffset < 3)
                {
                    throw Invalid("Catch/filter entries require one complete exception-owner triple in their exact evaluation storage.");
                }
                foreach (WarpPortableExceptionSiteBinding site in body.Sites)
                {
                    if (site.GeneratedBlocks.IsEmpty || !method.Instructions.Any(instruction => instruction.Offset == site.SourceOffset))
                    {
                        throw Invalid("Every generated block must name an exact verified original instruction.");
                    }
                    if (!sourceEntries.TryAdd((body.Function, site.SourceOffset), site.GeneratedBlocks[0]))
                    {
                        throw Invalid("An original EH instruction entry is bound more than once.");
                    }
                    foreach (int block in site.GeneratedBlocks)
                    {
                        if (!layout.Nodes.Any(node => node.Function == body.Function && node.Block == block) ||
                            !sourceOffsets.TryAdd((body.Function, block), site.SourceOffset))
                        {
                            throw Invalid("Generated EH blocks have overlapping or nonexistent source ownership.");
                        }
                    }
                }
                if (layout.Nodes.Any(node => node.Function == body.Function && !sourceOffsets.ContainsKey((body.Function, node.Block))))
                {
                    throw Invalid("A generated call continuation or auxiliary block lacks its original CIL/EH membership.");
                }
            }
            if (layout.Kernel.Execution!.Bodies.Select((body, function) => (body, function))
                .Any(pair => !pair.body.RuntimeHelper && !bodies.Any(binding => binding.Function == pair.function)))
            {
                throw Invalid("All source bodies in the generated machine must participate in the EH/trace map.");
            }
        }

        private void WriteBodies()
        {
            words[(int)WarpPortableExceptionLayout.BodyCount] = checked((uint)layout.Kernel.Functions.Count + 1);
            words[(int)WarpPortableExceptionLayout.BodyStart] = Next;
            for (int function = 0; function <= layout.Kernel.Functions.Count; function++)
            {
                WarpPortableExceptionBodyBinding? body = bodies.FirstOrDefault(body => body.Function == function);
                uint row = Allocate(WarpPortableExceptionLayout.BodyWords);
                Set(row, WarpPortableExceptionLayout.BodyFunction, (uint)function);
                Set(row, WarpPortableExceptionLayout.BodyPrivateWords, (uint)layout.GetPrivateWordCount(function));
                Set(row, WarpPortableExceptionLayout.BodyEvaluationOffset, (uint)(body?.EvaluationWordOffset ?? 0));
                Set(row, WarpPortableExceptionLayout.BodyMethod, body is null ? uint.MaxValue : (uint)Method(body.MethodIdentity).Id);
                Set(row, WarpPortableExceptionLayout.BodyCountsDepth, layout.CountsSourceDepth(function) ? 1u : 0u);
                Set(row, WarpPortableExceptionLayout.BodyAliasOwner, unchecked((uint)layout.GetAliasOwnerFunction(function)));
                Set(row, WarpPortableExceptionLayout.BodyAliasPrefix, (uint)layout.GetAliasPrefixWords(function));
                if (function != 0 && string.Equals(layout.Kernel.Functions[function - 1].Name, WarpPortableExceptionSourceDriver.Semantics, StringComparison.Ordinal))
                {
                    words[(int)WarpPortableExceptionLayout.DriverFunction] = (uint)function;
                    words[(int)WarpPortableExceptionLayout.DriverPc] = (uint)layout.GetBlockEntry(function, 0);
                }
            }
        }

        private void WriteClauses()
        {
            words[(int)WarpPortableExceptionLayout.ClauseStart] = Next;
            uint count = 0;
            foreach (WarpPortableExceptionBodyBinding body in bodies)
            {
                WarpPortableMethodGraphMethod method = Method(body.MethodIdentity);
                for (int index = 0; index < method.ExceptionRegions.Length; index++)
                {
                    WarpPortableMethodGraphExceptionRegion region = method.ExceptionRegions[index];
                    if (!sourceEntries.ContainsKey((body.Function, region.HandlerOffset)) || !sourceEntries.ContainsKey((body.Function, region.TryOffset))) { continue; }
                    uint row = Allocate(WarpPortableExceptionLayout.ClauseWords); clauses.Add((body.Function, index), ++count);
                    Set(row, WarpPortableExceptionLayout.ClauseFunction, (uint)body.Function);
                    Set(row, WarpPortableExceptionLayout.ClauseMethod, (uint)method.Id);
                    Set(row, WarpPortableExceptionLayout.ClauseOrdinal, (uint)index);
                    Set(row, WarpPortableExceptionLayout.ClauseKind, (uint)region.Kind);
                    Set(row, WarpPortableExceptionLayout.TryStart, (uint)region.TryOffset);
                    Set(row, WarpPortableExceptionLayout.TryEnd, checked((uint)(region.TryOffset + region.TryLength)));
                    Set(row, WarpPortableExceptionLayout.HandlerStart, (uint)region.HandlerOffset);
                    Set(row, WarpPortableExceptionLayout.HandlerEnd, checked((uint)(region.HandlerOffset + region.HandlerLength)));
                    Set(row, WarpPortableExceptionLayout.FilterStart, region.FilterOffset < 0 ? uint.MaxValue : (uint)region.FilterOffset);
                    Set(row, WarpPortableExceptionLayout.CatchType, region.CatchType is null ? 0 : schema.TypeId(region.CatchType));
                    Set(row, WarpPortableExceptionLayout.HandlerPc, Entry(body.Function, region.HandlerOffset));
                    WarpPortableExceptionBodyBinding? alias = region.FilterOffset < 0 || body.AliasOwnerFunction != -1 ? null : bodies.FirstOrDefault(candidate =>
                        candidate.AliasOwnerFunction == body.Function && candidate.Sites.Any(site => site.SourceOffset == region.FilterOffset));
                    Set(row, WarpPortableExceptionLayout.FilterPc, alias is null ? region.FilterOffset < 0 ? uint.MaxValue : Entry(body.Function, region.FilterOffset) : (uint)layout.GetBlockEntry(alias.Function, 0));
                    Set(row, WarpPortableExceptionLayout.FilterFunction, alias is null ? uint.MaxValue : (uint)alias.Function);
                    Set(row, WarpPortableExceptionLayout.FilterPrefix, (uint)(alias?.AliasPrefixWords ?? 0));
                    Set(row, WarpPortableExceptionLayout.ExceptionPrivateOffset, (uint)body.EvaluationWordOffset);
                    int group = method.ExceptionRegions.Take(index + 1).Select((clause, ordinal) => (clause, ordinal))
                        .First(pair => pair.clause.TryOffset == region.TryOffset && pair.clause.TryLength == region.TryLength).ordinal;
                    if (!clauses.TryGetValue((body.Function, group), out uint groupId)) { throw Invalid("A captured EH group is incomplete in this body."); }
                    Set(row, WarpPortableExceptionLayout.ClauseGroup, groupId);
                }
            }
            words[(int)WarpPortableExceptionLayout.ClauseCount] = count;
        }

        private void WriteSites()
        {
            words[(int)WarpPortableExceptionLayout.SiteStart] = Next;
            uint count = 0;
            foreach (WarpPortableExceptionBodyBinding body in bodies)
            {
                WarpPortableMethodGraphMethod original = Method(body.MethodIdentity);
                foreach (WarpPortableTypedInstruction instruction in Instructions(body))
                {
                    uint row = Allocate(WarpPortableExceptionLayout.SiteWords); sites.Add((body.Function, instruction.Offset), ++count);
                    Set(row, WarpPortableExceptionLayout.SiteFunction, (uint)body.Function);
                    Set(row, WarpPortableExceptionLayout.SiteOffset, (uint)instruction.Offset);
                    Set(row, WarpPortableExceptionLayout.SiteOpCode, unchecked((ushort)instruction.OpCode));
                    Set(row, WarpPortableExceptionLayout.SiteMethod, (uint)original.Id);
                    Set(row, WarpPortableExceptionLayout.SiteEffectCount, (uint)instruction.Effects.Length);
                    Set(row, WarpPortableExceptionLayout.LeaveTargetPc, uint.MaxValue);
                    WarpPortableMethodGraphInstruction source = original.Instructions.First(value => value.Offset == instruction.Offset);
                    if (source.OpCode == OpCodes.Leave || source.OpCode == OpCodes.Leave_S)
                    {
                        Set(row, WarpPortableExceptionLayout.LeaveTargetPc, Entry(body.Function, source.BranchTargets[0]));
                    }
                }
            }
            words[(int)WarpPortableExceptionLayout.SiteCount] = count;
        }

        private void WritePcs()
        {
            words[(int)WarpPortableExceptionLayout.PcStart] = Next; uint count = 0;
            for (int pc = 0; pc < layout.Nodes.Count; pc++)
            {
                WarpCLR.IR.WarpLogicalMachineNode node = layout.Nodes[pc];
                uint row = Allocate(WarpPortableExceptionLayout.PcWords); count++;
                Set(row, WarpPortableExceptionLayout.PcValue, (uint)pc);
                Set(row, WarpPortableExceptionLayout.PcFunction, (uint)node.Function);
                Set(row, WarpPortableExceptionLayout.PcSite, sourceOffsets.TryGetValue((node.Function, node.Block), out int offset) ? sites[(node.Function, offset)] : 0);
            }
            words[(int)WarpPortableExceptionLayout.PcCount] = count;
        }

        private void WriteLists()
        {
            uint start = Next; words[(int)WarpPortableExceptionLayout.ListStart] = start;
            foreach (WarpPortableExceptionBodyBinding body in bodies)
            {
                WarpPortableMethodGraphMethod method = Method(body.MethodIdentity);
                foreach (WarpPortableTypedInstruction instruction in Instructions(body))
                {
                    uint site = words[(int)WarpPortableExceptionLayout.SiteStart] + (sites[(body.Function, instruction.Offset)] - 1) * WarpPortableExceptionLayout.SiteWords;
                    int[] active = instruction.ExceptionMemberships.Where(member => member.Role == WarpPortableExceptionRole.Try)
                        .OrderBy(member => method.ExceptionRegions[member.Region].TryLength)
                        .ThenBy(member => member.Region).Select(member => member.Region).Where(region => clauses.ContainsKey((body.Function, region))).ToArray();
                    Set(site, WarpPortableExceptionLayout.TryList, Next); Set(site, WarpPortableExceptionLayout.TryCount, (uint)active.Length);
                    foreach (int clause in active) { words.Add(clauses[(body.Function, clause)]); }
                    int[] unwinding = instruction.UnwindRegions.Where(region => clauses.ContainsKey((body.Function, region))).ToArray();
                    Set(site, WarpPortableExceptionLayout.LeaveList, Next); Set(site, WarpPortableExceptionLayout.LeaveCount, (uint)unwinding.Length);
                    foreach (int clause in unwinding) { words.Add(clauses[(body.Function, clause)]); }
                }
            }
            words[(int)WarpPortableExceptionLayout.ListCount] = Next - start;
        }

        private void WriteFaults()
        {
            words[(int)WarpPortableExceptionLayout.FaultStart] = Next;
            foreach (WarpPortableExceptionBodyBinding body in bodies)
            {
                foreach (WarpPortableSourceHeapFault fault in schema.Faults.Where(fault => string.Equals(fault.MethodIdentity, body.MethodIdentity, StringComparison.Ordinal) && sites.ContainsKey((body.Function, fault.SourceOffset))))
                {
                    faults.Add(new((uint)faults.Count + 1, fault.MethodIdentity, fault.SourceOffset, fault.SourceOpCode, fault.EffectIndex,
                        fault.ExceptionType, fault.UncatchableRuntimeTermination, null, 0, []));
                    WriteFault(body.Function, faults[^1]);
                }
                foreach (WarpPortableSourceHeapServiceFault fault in schema.ServiceFaults.Where(fault => string.Equals(fault.MethodIdentity, body.MethodIdentity, StringComparison.Ordinal) && sites.ContainsKey((body.Function, fault.SourceOffset))))
                {
                    faults.Add(new((uint)faults.Count + 1, fault.MethodIdentity, fault.SourceOffset, fault.SourceOpCode, fault.EffectIndex,
                        fault.ExceptionType, false, fault.BindingIdentity, fault.Descriptor, fault.FaultOperandWords));
                    WriteFault(body.Function, faults[^1]);
                }
            }
            words[(int)WarpPortableExceptionLayout.FaultCount] = (uint)faults.Count;
        }

        private void WriteFault(int function, WarpPortableExceptionFaultBinding fault)
        {
            uint row = Allocate(WarpPortableExceptionLayout.FaultWords);
            Set(row, WarpPortableExceptionLayout.FaultSite, sites[(function, fault.SourceOffset)]);
            Set(row, WarpPortableExceptionLayout.FaultEffect, (uint)fault.EffectIndex);
            Set(row, WarpPortableExceptionLayout.FaultType, fault.ExceptionType);
            Set(row, WarpPortableExceptionLayout.FaultPolicy, fault.UncatchableRuntimeTermination ? 1u : 0u);
            Set(row, WarpPortableExceptionLayout.FaultDescriptor, fault.Descriptor);
        }

        private IEnumerable<WarpPortableTypedInstruction> Instructions(WarpPortableExceptionBodyBinding body) =>
            Typed(body.MethodIdentity).Instructions.Where(instruction => body.Sites.Any(site => site.SourceOffset == instruction.Offset));
    }
}
