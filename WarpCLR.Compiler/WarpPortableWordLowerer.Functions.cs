using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class Builder
    {
        private void DiscoverSourceFunctions()
        {
            var pending = new Queue<string>(); pending.Enqueue(graph.EntryIdentity);
            if (binding is not null)
            {
                foreach (string identity in binding.AdditionalSourceMethods) { pending.Enqueue(identity); }
            }
            while (pending.TryDequeue(out string? identity))
            {
                if (functionIds.ContainsKey(identity)) { continue; }
                WarpPortableMethodGraphMethod source = sources[identity];
                if (source.Intrinsic is not null || source.Instructions.IsEmpty)
                {
                    throw Error(identity, "The source entry/callee requires a registered execution service.", 0);
                }
                CheckInitializer(source, 0);
                int id = functions.Count; functionIds.Add(identity, id); functions.Add(null); metadata.Add(null);
                WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.Functions, functions.Count, WarpCompilationAdmission.MaximumFunctionsPerEntry);
                foreach (WarpPortableTypedInstruction typed in methods[identity].Instructions.Where(instruction => instruction.Reachable))
                {
                    WarpPortableMethodGraphInstruction instruction = source.Instructions.First(item => item.Offset == typed.Offset);
                    if (instruction.OpCode == OpCodes.Call && instruction.Method is { } callee && sources[callee].Intrinsic is null)
                    {
                        pending.Enqueue(callee);
                    }
                }
            }
        }

        private WarpBasicBlock Entry()
        {
            WarpPortableTypedMethod method = methods[graph.EntryIdentity];
            int words = method.ArgumentTypes.Sum(identity => types[identity].WordCount);
            var instructions = new List<WarpIrInstruction>(words + 1);
            for (int word = 0; word < words; word++) { instructions.Add(new(word, WarpIrOpCode.LoadInput, immediate: (uint)word)); }
            int results = types[method.ReturnType].WordCount;
            instructions.Add(new(results == 0 ? -1 : words, functionIds[method.Identity], Enumerable.Range(0, words), results));
            return new(0, [], instructions, Return(Enumerable.Range(words, results)));
        }

        internal void CheckInitializer(WarpPortableMethodGraphMethod method, int offset)
        {
            WarpPortableMethodGraphType owner = graph.Types.First(type => type.SourceType == method.SourceMethod.DeclaringType);
            if (owner.Initializer is not null && !string.Equals(owner.Initializer, method.Identity, StringComparison.Ordinal))
            {
                throw Error(method.Identity, "Type initializer execution must be bound to the generated runtime service.", offset);
            }
        }
    }

    private static WarpBlockTerminator Return(IEnumerable<int> words)
    {
        int[] values = words.ToArray();
        return values.Length == 1 ? new WarpReturnTerminator(values[0]) : new WarpTupleReturnTerminator(values);
    }
}
