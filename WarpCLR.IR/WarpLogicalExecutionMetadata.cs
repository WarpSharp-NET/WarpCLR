using System.Collections.ObjectModel;

namespace WarpCLR.IR;

internal sealed class WarpLogicalExecutionMetadata
{
    internal const string Version = "warp.logical-source-frames/0.6";
    internal const string PrivateControllerVersion = "warp.logical-source-frames/private-controller-service-projection/0.7";

    internal WarpLogicalExecutionMetadata(IEnumerable<WarpLogicalBodyMetadata> bodies, bool recursiveCalls = true,
        bool frameOwners = false, bool runtimeStateAccess = false, bool nonlocalStateDispatch = false,
        bool managedExceptionTermination = false, bool logicalWorkerAccess = false,
        WarpPrivateControllerProjection? privateControllerProjection = null)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        Bodies = Array.AsReadOnly(WarpCompilationAdmission.Materialize(bodies, "<logical-frame-metadata>",
            WarpCompilationResourceKind.Functions, WarpCompilationAdmission.MaximumFunctionsPerEntry + 1));
        if (Bodies.Count == 0 || Bodies.Any(body => body is null))
        {
            throw new ArgumentException("Every physical body requires immutable logical-frame metadata.", nameof(bodies));
        }
        RecursiveCalls = recursiveCalls;
        FrameOwners = frameOwners;
        RuntimeStateAccess = runtimeStateAccess;
        if (nonlocalStateDispatch && (!runtimeStateAccess || !frameOwners))
        {
            throw new ArgumentException("Nonlocal dispatch requires explicit state and frame ownership capabilities.", nameof(nonlocalStateDispatch));
        }
        NonlocalStateDispatch = nonlocalStateDispatch;
        if (managedExceptionTermination && !nonlocalStateDispatch)
        {
            throw new ArgumentException("Managed exception termination requires admitted nonlocal state and frame ownership capabilities.", nameof(managedExceptionTermination));
        }
        ManagedExceptionTermination = managedExceptionTermination;
        LogicalWorkerAccess = logicalWorkerAccess;
        if (privateControllerProjection is not null && (!runtimeStateAccess || !frameOwners))
        {
            throw new ArgumentException("A private controller projection requires exact state and frame ownership capabilities.", nameof(privateControllerProjection));
        }
        PrivateControllerProjection = privateControllerProjection;
    }

    internal ReadOnlyCollection<WarpLogicalBodyMetadata> Bodies { get; }
    internal bool RecursiveCalls { get; }
    internal bool FrameOwners { get; }
    internal bool RuntimeStateAccess { get; }
    internal bool NonlocalStateDispatch { get; }
    internal bool ManagedExceptionTermination { get; }
    internal bool LogicalWorkerAccess { get; }
    internal WarpPrivateControllerProjection? PrivateControllerProjection { get; }
    internal string IdentityVersion => PrivateControllerProjection is null ? Version : PrivateControllerVersion;

    internal int Validate(IReadOnlyList<WarpBasicBlock> blocks, IReadOnlyList<WarpControlFlowFunction> functions)
    {
        if (Bodies.Count != functions.Count + 1)
        {
            throw new ArgumentException("Logical-frame metadata must cover every body in function order.", nameof(functions));
        }
        ValidateAliases(functions);
        ValidateBody(blocks, Bodies[0]);
        for (int index = 0; index < functions.Count; index++)
        {
            ValidateBody(functions[index].Blocks, Bodies[index + 1]);
        }
        PrivateControllerProjection?.Validate(blocks, functions, Bodies);
        return GetHelperExpansion(blocks, functions);
    }

    private void ValidateAliases(IReadOnlyList<WarpControlFlowFunction> functions)
    {
        for (int function = 0; function < Bodies.Count; function++)
        {
            WarpLogicalBodyMetadata metadata = Bodies[function];
            int owner = metadata.AliasOwnerFunction;
            if (owner == -1) { continue; }
            if (!NonlocalStateDispatch || !FrameOwners || !RuntimeStateAccess || function == 0 ||
                owner > functions.Count || owner == function || !Bodies[owner].CountsSourceDepth ||
                metadata.AliasPrefixWords > Bodies[owner].PrivateWordCount)
            {
                throw new ArgumentException("A filter alias requires an exact original source owner and nonlocal runtime capabilities.", nameof(functions));
            }
        }
    }

    private void ValidateBody(IReadOnlyList<WarpBasicBlock> blocks, WarpLogicalBodyMetadata metadata)
    {
        if (!ManagedExceptionTermination && blocks.Any(block => block.Terminator is WarpManagedExceptionTerminator))
        {
            throw new ArgumentException("Managed exception termination requires its exact immutable capability.", nameof(blocks));
        }
        if (!NonlocalStateDispatch && blocks.Any(block => block.Terminator is WarpStateDispatchTerminator))
        {
            throw new ArgumentException("A nonlocal state dispatch requires explicit immutable execution admission.", nameof(blocks));
        }
        if (metadata.SourceBlockCosts.Count != blocks.Count)
        {
            throw new ArgumentException("Each block requires one original-source charge.", nameof(metadata));
        }
        foreach (WarpIrInstruction instruction in blocks.SelectMany(block => block.Instructions))
        {
            if (instruction.OpCode == WarpManagedInvocationOpCode.LoadLogicalWorker && !LogicalWorkerAccess)
            {
                throw new ArgumentException("Logical worker identity requires its exact immutable invocation capability.", nameof(blocks));
            }
            if (instruction.OpCode == WarpIrOpCode.Call && Bodies[instruction.Callee + 1].AliasOwnerFunction != -1)
            {
                throw new ArgumentException("Filter aliases enter through nonlocal dispatch rather than an ordinary call.", nameof(blocks));
            }
            if (WarpManagedFrameOpCode.IsOwner(instruction.OpCode) && !FrameOwners ||
                WarpManagedStateOpCode.IsState(instruction.OpCode) && !RuntimeStateAccess)
            {
                throw new ArgumentException("Runtime word capabilities require explicit immutable execution admission.", nameof(blocks));
            }
            if (WarpManagedFrameOpCode.IsPrivate(instruction.OpCode) && instruction.Immediate >= metadata.PrivateWordCount)
            {
                throw new ArgumentException("A private storage instruction is outside its own body's admitted word bank.", nameof(blocks));
            }
        }
        if (metadata.AliasOwnerFunction != -1 && blocks.Any(block => block.Terminator is WarpReturnTerminator or WarpTupleReturnTerminator))
        {
            throw new ArgumentException("A source filter finishes through runtime state dispatch rather than an ordinary return.", nameof(blocks));
        }
    }

    private int GetHelperExpansion(IReadOnlyList<WarpBasicBlock> blocks, IReadOnlyList<WarpControlFlowFunction> functions)
    {
        var states = new byte[Bodies.Count];
        var lengths = new int[Bodies.Count];
        int helperMaximum = 0;
        int aliasMaximum = 0;
        for (int body = 0; body < Bodies.Count; body++)
        {
            if (Bodies[body].RuntimeHelper) { helperMaximum = Math.Max(helperMaximum, Visit(body)); }
            else if (!Bodies[body].CountsSourceDepth) { aliasMaximum = Math.Max(aliasMaximum, Visit(body)); }
        }
        // One filter may retain a suspended helper chain and run its own helper chain.
        return checked(helperMaximum + aliasMaximum + 1);

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
                if (!Bodies[callee].CountsSourceDepth) { children = Math.Max(children, Visit(callee)); }
            }
            states[body] = 2;
            lengths[body] = checked(children + 1);
            return lengths[body];
        }
    }
}
