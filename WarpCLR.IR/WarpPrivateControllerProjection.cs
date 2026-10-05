using System.Collections.ObjectModel;

namespace WarpCLR.IR;

// This immutable compiler contract describes a private channel. It does not
// authorize execution; only the runtime's opaque source registry may do that.
internal sealed class WarpPrivateControllerProjection
{
    internal WarpPrivateControllerProjection(IEnumerable<WarpPrivateControllerUse> uses)
    {
        ArgumentNullException.ThrowIfNull(uses);
        Uses = Array.AsReadOnly(WarpCompilationAdmission.Materialize(uses, "<private-controller-uses>",
            WarpCompilationResourceKind.Instructions, WarpCompilationAdmission.MaximumInstructionsPerEntry));
        if (Uses.Count == 0 || Uses.Any(use => use is null || use.Function < 0 || use.Block < 0 || use.Value < 0 ||
            use.Callee < 0 || use.Argument < 0 || use.CallValue < 0 || string.IsNullOrWhiteSpace(use.ServiceIdentity)) ||
            Uses.Select(use => (use.Function, use.Block, use.Value)).Distinct().Count() != Uses.Count)
        {
            throw new ArgumentException("A private controller channel needs unique exact service-use sites.", nameof(uses));
        }
    }

    internal ReadOnlyCollection<WarpPrivateControllerUse> Uses { get; }

    internal void Validate(IReadOnlyList<WarpBasicBlock> blocks, IReadOnlyList<WarpControlFlowFunction> functions,
        IReadOnlyList<WarpLogicalBodyMetadata> metadata)
    {
        var sites = Uses.ToDictionary(use => (use.Function, use.Block, use.Value));
        var observed = new HashSet<(int Function, int Block, int Value)>();
        var helpers = new HashSet<int>();
        for (int function = 0; function <= functions.Count; function++)
        {
            IReadOnlyList<WarpBasicBlock> body = function == 0 ? blocks : functions[function - 1].Blocks;
            foreach (WarpBasicBlock block in body)
            {
                ValidateBlock(function, block, sites, observed, functions, metadata, helpers);
            }
        }
        if (observed.Count != Uses.Count)
        {
            throw new ArgumentException("The private controller contract contains an absent load site.", nameof(blocks));
        }
    }

    private static void ValidateBlock(int function, WarpBasicBlock block,
        Dictionary<(int Function, int Block, int Value), WarpPrivateControllerUse> sites,
        HashSet<(int Function, int Block, int Value)> observed,
        IReadOnlyList<WarpControlFlowFunction> functions, IReadOnlyList<WarpLogicalBodyMetadata> metadata,
        HashSet<int> helpers)
    {
        var loaded = new Dictionary<int, WarpPrivateControllerUse>();
        foreach (WarpIrInstruction instruction in block.Instructions.Where(item => item.OpCode == WarpPrivateControllerOpCode.LoadController))
        {
            var key = (function, block.Id, instruction.Result);
            if (!sites.TryGetValue(key, out WarpPrivateControllerUse? use))
            { throw new ArgumentException("A private controller load has no exact immutable service-use site.", nameof(block)); }
            loaded.Add(instruction.Result, use); observed.Add(key);
            RequirePureService(use, functions, metadata, helpers);
        }
        if (loaded.Count == 0) { return; }
        var references = loaded.Keys.ToDictionary(value => value, _ => 0);
        foreach (WarpIrInstruction instruction in block.Instructions)
        {
            ValidateReferences(instruction, loaded, references);
        }
        if (references.Values.Any(count => count != 1) || TerminatorWords(block.Terminator).Any(loaded.ContainsKey))
        {
            throw new ArgumentException("A private controller value may only enter its one exact service call.", nameof(block));
        }
    }

    private static void ValidateReferences(WarpIrInstruction instruction,
        Dictionary<int, WarpPrivateControllerUse> loaded, Dictionary<int, int> references)
    {
        if (loaded.ContainsKey(instruction.Left) || loaded.ContainsKey(instruction.Right) || loaded.ContainsKey(instruction.Third))
        { throw new ArgumentException("A private controller word cannot be used as an ordinary operand.", nameof(instruction)); }
        for (int argument = 0; argument < instruction.Arguments.Count; argument++)
        {
            int value = instruction.Arguments[argument];
            if (!loaded.TryGetValue(value, out WarpPrivateControllerUse? use)) { continue; }
            if (instruction.OpCode != WarpIrOpCode.Call || instruction.Callee != use.Callee ||
                argument != use.Argument || instruction.Result != use.CallValue)
            { throw new ArgumentException("A private controller word changed its exact service or parameter.", nameof(instruction)); }
            references[value]++;
        }
    }

    private static void RequirePureService(WarpPrivateControllerUse use,
        IReadOnlyList<WarpControlFlowFunction> functions, IReadOnlyList<WarpLogicalBodyMetadata> metadata, HashSet<int> helpers)
    {
        if ((uint)use.Callee >= (uint)functions.Count ||
            !string.Equals(functions[use.Callee].Name, use.ServiceIdentity, StringComparison.Ordinal) ||
            (uint)use.Argument >= (uint)functions[use.Callee].ParameterCount)
        { throw new ArgumentException("A private controller contract changed its captured service signature.", nameof(use)); }
        var pending = new Stack<int>(); pending.Push(use.Callee);
        while (pending.TryPop(out int callee))
        {
            if (!helpers.Add(callee)) { continue; }
            if ((uint)callee >= (uint)functions.Count || !metadata[callee + 1].RuntimeHelper || metadata[callee + 1].CountsSourceDepth ||
                functions[callee].Blocks.Any(block => block.Terminator is WarpStateDispatchTerminator or WarpManagedExceptionTerminator))
            { throw new ArgumentException("A private controller service cannot retain its token through guest dispatch or a terminal publication.", nameof(use)); }
            foreach (int child in functions[callee].Instructions.Where(item => item.OpCode == WarpIrOpCode.Call).Select(item => item.Callee))
            { pending.Push(child); }
        }
    }

    private static IEnumerable<int> TerminatorWords(WarpBlockTerminator terminator) => terminator switch
    {
        WarpReturnTerminator returned => [returned.Value],
        WarpTupleReturnTerminator returned => returned.Values,
        WarpBranchTerminator branch => branch.Target.Arguments,
        WarpConditionalBranchTerminator branch => branch.WhenNonZero.Arguments.Concat(branch.WhenZero.Arguments).Prepend(branch.Condition),
        WarpManagedExceptionTerminator escaped => [escaped.Context, escaped.ObjectId, escaped.Generation],
        WarpStateDispatchTerminator => [],
        _ => throw new ArgumentException("A private controller contract requires a known exact terminator.", nameof(terminator)),
    };
}
