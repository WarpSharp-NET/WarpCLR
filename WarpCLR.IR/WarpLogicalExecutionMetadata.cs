using System.Collections.ObjectModel;

namespace WarpCLR.IR;

internal sealed class WarpLogicalExecutionMetadata
{
    internal const string Version = "warp.logical-source-frames/0.1";

    internal WarpLogicalExecutionMetadata(IEnumerable<WarpLogicalBodyMetadata> bodies, bool recursiveCalls = true)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        Bodies = Array.AsReadOnly(WarpCompilationAdmission.Materialize(bodies, "<logical-frame-metadata>",
            WarpCompilationResourceKind.Functions, WarpCompilationAdmission.MaximumFunctionsPerEntry + 1));
        if (Bodies.Count == 0 || Bodies.Any(body => body is null))
        {
            throw new ArgumentException("Every physical body requires immutable logical-frame metadata.", nameof(bodies));
        }
        RecursiveCalls = recursiveCalls;
    }

    internal ReadOnlyCollection<WarpLogicalBodyMetadata> Bodies { get; }
    internal bool RecursiveCalls { get; }

    internal int Validate(IReadOnlyList<WarpBasicBlock> blocks, IReadOnlyList<WarpControlFlowFunction> functions)
    {
        if (Bodies.Count != functions.Count + 1)
        {
            throw new ArgumentException("Logical-frame metadata must cover every body in function order.", nameof(functions));
        }
        ValidateBody(blocks, Bodies[0]);
        for (int index = 0; index < functions.Count; index++)
        {
            ValidateBody(functions[index].Blocks, Bodies[index + 1]);
        }
        return GetHelperExpansion(blocks, functions);
    }

    private static void ValidateBody(IReadOnlyList<WarpBasicBlock> blocks, WarpLogicalBodyMetadata metadata)
    {
        if (metadata.SourceBlockCosts.Count != blocks.Count)
        {
            throw new ArgumentException("Each block requires one original-source charge.", nameof(metadata));
        }
        foreach (WarpIrInstruction instruction in blocks.SelectMany(block => block.Instructions))
        {
            if (WarpManagedFrameOpCode.IsPrivate(instruction.OpCode) && instruction.Immediate >= metadata.PrivateWordCount)
            {
                throw new ArgumentException("A private storage instruction is outside its own body's admitted word bank.", nameof(blocks));
            }
        }
    }

    private int GetHelperExpansion(IReadOnlyList<WarpBasicBlock> blocks, IReadOnlyList<WarpControlFlowFunction> functions)
    {
        var states = new byte[Bodies.Count];
        var lengths = new int[Bodies.Count];
        int maximum = 0;
        for (int body = 0; body < Bodies.Count; body++)
        {
            if (Bodies[body].RuntimeHelper) { maximum = Math.Max(maximum, Visit(body)); }
        }
        return checked(maximum + 1);

        int Visit(int body)
        {
            if (states[body] == 1)
            {
                throw new ArgumentException("Helper-only recursion cannot consume unbounded uncharged physical frames.", nameof(functions));
            }
            if (states[body] == 2) { return lengths[body]; }
            states[body] = 1;
            int children = 0;
            IReadOnlyList<WarpBasicBlock> current = body == 0 ? blocks : functions[body - 1].Blocks;
            foreach (int callee in current.SelectMany(block => block.Instructions)
                .Where(instruction => instruction.OpCode == WarpIrOpCode.Call).Select(instruction => instruction.Callee + 1))
            {
                if (Bodies[callee].RuntimeHelper) { children = Math.Max(children, Visit(callee)); }
            }
            states[body] = 2;
            lengths[body] = checked(children + 1);
            return lengths[body];
        }
    }
}
