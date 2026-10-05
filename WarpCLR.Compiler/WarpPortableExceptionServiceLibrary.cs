using WarpCLR.Verifier;
using System.Reflection;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using WarpCLR.IR;

namespace WarpCLR.Compiler;

// Compile-time graph composition only. No source method or user delegate runs.
internal sealed class WarpPortableExceptionServiceLibrary
{
    private readonly List<WarpControlFlowFunction> functions = [];
    private readonly Dictionary<string, (int Id, string Hash)> closure = new(StringComparer.Ordinal);

    internal IReadOnlyList<WarpControlFlowFunction> Functions => functions.AsReadOnly();

    internal int Import(string name)
    {
        MethodInfo method = WarpPortableExceptionServices.Method(name);
        WarpLogicalMachineLayout lowered = method.GetParameters().Count(parameter => parameter.ParameterType == typeof(uint[])) == 2 ?
            WarpWordStateArenaServiceLowerer.Lower(method, WarpPortableExceptionServices.BankBindings()) : WarpWordArenaServiceLowerer.Lower(method);
        WarpControlFlowKernel kernel = lowered.Kernel; int entry = functions.Count;
        int[] callees = new int[kernel.Functions.Count]; var added = new List<(WarpControlFlowFunction Function, int Id)>();
        functions.Add(Placeholder(entry));
        foreach (WarpControlFlowFunction function in kernel.Functions)
        {
            string hash = BodyHash(function, kernel.Functions);
            if (closure.TryGetValue(function.Name, out (int Id, string Hash) known))
            {
                if (!string.Equals(hash, known.Hash, StringComparison.Ordinal)) { throw WarpPortableExceptionPlan.Invalid("An EH helper identity changed its exact banks or compiled body."); }
                callees[function.Id] = known.Id;
            }
            else
            {
                int id = functions.Count; functions.Add(Placeholder(id)); closure.Add(function.Name, (id, hash));
                callees[function.Id] = id; added.Add((function, id));
            }
        }
        foreach ((WarpControlFlowFunction function, int id) in added)
        {
            functions[id] = new(id, function.Name, function.ParameterCount, Rewrite(function.Blocks, callees, entry: false));
        }
        int parameters = method.GetParameters().Count(parameter => parameter.ParameterType != typeof(uint[]));
        functions[entry] = new(entry, method.DeclaringType!.FullName + "." + name + "/trusted-eh-entry/" + WarpIrHash.Compute(kernel),
            parameters, Rewrite(kernel.Blocks, callees, entry: true));
        return entry;
    }

    private static WarpControlFlowFunction Placeholder(int id) => new(id, "unpublished-eh-construction", 0,
        [new WarpBasicBlock(0, [], [new WarpIrInstruction(0, WarpIrOpCode.Constant)], new WarpReturnTerminator(0))]);

    private static WarpBasicBlock[] Rewrite(IReadOnlyList<WarpBasicBlock> blocks, int[] callees, bool entry) => blocks.Select(block =>
        new WarpBasicBlock(block.Id, block.Parameters, block.Instructions.Select(instruction => Rewrite(instruction, callees, entry)), block.Terminator)).ToArray();

    private static WarpIrInstruction Rewrite(WarpIrInstruction instruction, int[] callees, bool entry)
    {
        if (instruction.OpCode == WarpIrOpCode.Call) { return new(instruction.Result, callees[instruction.Callee], instruction.Arguments, instruction.ResultWordCount); }
        return entry && instruction.OpCode == WarpIrOpCode.LoadInput ? new(instruction.Result, WarpIrOpCode.LoadArgument, immediate: instruction.Immediate) : instruction;
    }

    private static string BodyHash(WarpControlFlowFunction function, ReadOnlyCollection<WarpControlFlowFunction> functions) =>
        Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(new
        {
            function.Name, function.ParameterCount,
            Blocks = function.Blocks.Select(block => new
            {
                block.Id, block.Parameters, Terminator = WarpPortableSnapshotIdentity.Element(block.Terminator, block.Terminator.GetType()),
                Instructions = block.Instructions.Select(instruction => new
                {
                    instruction.Result, instruction.OpCode, instruction.Left, instruction.Right, instruction.Third,
                    instruction.Immediate, instruction.Arguments, instruction.ResultType, instruction.ResultWordCount,
                    Callee = instruction.OpCode == WarpIrOpCode.Call ? functions[instruction.Callee].Name : null,
                }),
            }),
        })));
}
