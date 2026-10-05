using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class Builder
    {
        internal int ImportService(Type implementation, string name)
        {
            MethodInfo target = implementation.GetMethod(name, BindingFlags.Public | BindingFlags.Static) ?? throw new InvalidOperationException("The portable arithmetic service is missing: " + name);
            string identity = "service/" + implementation.FullName + "." + name;
            if (functionIds.TryGetValue(identity, out int existing)) { return existing; }
            if (target.ReturnType != typeof(uint) || target.GetParameters().Any(parameter => parameter.ParameterType != typeof(uint)))
            {
                throw new InvalidOperationException("Only catalogued exact word arithmetic signatures can be imported.");
            }
            WarpIntegerMapKernel verified = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(target, target.GetParameters().Length));
            WarpControlFlowKernel compiled = verified.ControlFlow;
            string semantics = (string?)implementation.GetField("Semantics", BindingFlags.Static | BindingFlags.NonPublic)?.GetRawConstantValue() ?? throw new InvalidOperationException("A portable arithmetic service must declare its semantic version.");
            services.Add(semantics + "/" + identity);
            int entry = ReserveService(identity);
            var remap = new Dictionary<int, int>();
            foreach (WarpControlFlowFunction helper in compiled.Functions) { remap.Add(helper.Id, ReserveService("service/" + helper.Name)); }
            SetService(entry, identity, target.GetParameters().Length, compiled.Blocks, remap, entryInputs: true);
            foreach (WarpControlFlowFunction helper in compiled.Functions)
            {
                SetService(remap[helper.Id], "service/" + helper.Name, helper.ParameterCount, helper.Blocks, remap, entryInputs: false);
            }
            return entry;
        }

        private int ReserveService(string identity)
        {
            if (functionIds.TryGetValue(identity, out int existing)) { return existing; }
            WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.Functions, functions.Count + 1L, WarpCompilationAdmission.MaximumFunctionsPerEntry);
            int result = functions.Count; functionIds.Add(identity, result); functions.Add(null); metadata.Add(null); return result;
        }

        private void SetService(int id, string identity, int parameters, IReadOnlyList<WarpBasicBlock> input, Dictionary<int, int> remap, bool entryInputs)
        {
            WarpBasicBlock[] lowered = input.Select(block => new WarpBasicBlock(block.Id, block.Parameters,
                block.Instructions.Select(instruction => RemapInstruction(instruction, remap, entryInputs)), block.Terminator)).ToArray();
            var candidate = new WarpControlFlowFunction(id, identity, parameters, lowered);
            if (functions[id] is { } existing)
            {
                if (!WarpPortableWordServiceComparison.Equal(existing, candidate))
                {
                    throw new InvalidOperationException("A reused generated service identity has different exact word IR: " + identity);
                }
                return;
            }
            functions[id] = candidate;
            metadata[id] = new(0, true, Enumerable.Repeat(0, lowered.Length));
        }

        private static WarpIrInstruction RemapInstruction(WarpIrInstruction instruction, Dictionary<int, int> remap, bool entryInputs)
        {
            if (instruction.OpCode == WarpIrOpCode.Call) { return new(instruction.Result, remap[instruction.Callee], instruction.Arguments, instruction.ResultWordCount); }
            WarpIrOpCode operation = entryInputs && instruction.OpCode == WarpIrOpCode.LoadInput ? WarpIrOpCode.LoadArgument : instruction.OpCode;
            return new(instruction.Result, operation, instruction.Left, instruction.Right, instruction.Immediate, instruction.Third, instruction.ResultType);
        }
    }
}
