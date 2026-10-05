using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Security.Cryptography;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// This internal binding has an explicit invocation domain: all throw operands
// must be prepared nonnull exception capabilities. It is not general throw or
// implicit-fault admission. The Host must validate its exact ticket, owner bank,
// schema, controller grant and operand domain before invoking generated code.
internal sealed partial class WarpPortableExceptionSourceBinding : WarpPortableWordExecutionBinding
{
    internal const string SourceSemantics = "warp.exception.source/prevalidated-nonnull-explicit-throw-two-pass-owned-ir-raw-utf16-snapshot/0.2";
    internal const string LeaseSemantics = "warp.exception.source-lease/all-bound-method-instruction-effects/0.1";
    private readonly WarpPortableSourceHeapSchema schema;
    private readonly uint controller;
    private readonly Dictionary<string, int> entries = new(StringComparer.Ordinal);
    private int transfer;
    private int terminal;
    private int driving;
    private int filtering = -1;

    internal WarpPortableExceptionSourceBinding(WarpPortableMethodGraph graph, WarpPortableTypedProgram program,
        WarpPortableSourceHeapSchema schema, uint controller)
        : base(graph.GraphHash, program.VerifiedHash, schema.SchemaHash, SourceSemantics,
            Identity(graph, program, schema, controller), new(true, true, true, true, true),
            exceptionMethods: graph.Methods.Where(method => !method.ExceptionRegions.IsEmpty).Select(method => method.Identity))
    {
        ArgumentOutOfRangeException.ThrowIfZero(controller);
        this.schema = schema; this.controller = controller;
        InstructionOwnership = Ownership(graph, program);
        InstructionOwnershipHash = OwnershipHash(graph, program, schema.SchemaHash);
    }

    internal WarpPortableExceptionPlan Plan => preparedPlan ?? throw new InvalidOperationException("The exact source EH plan is not finalized.");
    private WarpPortableExceptionPlan? preparedPlan;

    internal override void Prepare(WarpPortableWordBindingPreparation context)
    {
        string[] mixed = [nameof(WarpPortableExceptionServices.CaptureFrames), nameof(WarpPortableExceptionServices.ApplyAction),
            nameof(WarpPortableExceptionServices.PrepareFilterEnd), nameof(WarpPortableExceptionServices.RefreshContinuation), nameof(WarpPortableExceptionServices.ReleaseCaughtForReturn)];
        string[] pure = [nameof(WarpPortableExceptionServices.RaiseReference), nameof(WarpPortableExceptionServices.AdvanceSearch),
            nameof(WarpPortableExceptionServices.AdvanceUnwind), nameof(WarpPortableExceptionServices.Rethrow),
            nameof(WarpPortableExceptionServices.BeginLeave), nameof(WarpPortableExceptionServices.EndCleanupAt)];
        foreach (string name in mixed.Concat(pure))
        {
            if (!Required(context, name)) { continue; }
            System.Reflection.MethodInfo method = WarpPortableExceptionServices.Method(name);
            bool dual = mixed.Contains(name, StringComparer.Ordinal);
            WarpPortableGeneratedServiceImport imported = context.Services.Import(method, WarpPortableExceptionLayout.Semantics,
                dual ? WarpPortableGeneratedServiceKind.StateAndArena : WarpPortableGeneratedServiceKind.Arena,
                dual ? WarpPortableSourceServiceBanks.Capture(method) : null);
            entries.Add(name, imported.Function);
        }
        if (Required(context, nameof(WarpPortableExceptionServices.RaiseReference)))
        {
            System.Reflection.MethodInfo getType = typeof(WarpPortableHeapServices).GetMethod(nameof(WarpPortableHeapServices.GetType),
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            entries.Add(nameof(WarpPortableHeapServices.GetType), context.Services.Import(getType, WarpPortableHeapLayout.Semantics,
                WarpPortableGeneratedServiceKind.Arena).Function);
        }
        transfer = context.ReserveFunction(WarpPortableExceptionTransferLowerer.Semantics);
        terminal = context.ReserveFunction(WarpPortableExceptionTerminalLowerer.Semantics);
        driving = context.ReserveFunction(WarpPortableExceptionSourceDriver.Semantics);
        if (context.Graph.Methods.Any(method => method.ExceptionRegions.Any(region => region.Kind == 1)))
        {
            filtering = context.ReserveFunction(WarpPortableExceptionFilterLowerer.Semantics);
            System.Reflection.MethodInfo isInstance = typeof(WarpPortableHeapServices).GetMethod(nameof(WarpPortableHeapServices.IsInstance),
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            entries.Add(nameof(WarpPortableHeapServices.IsInstance), context.Services.Import(isInstance, WarpPortableHeapLayout.Semantics,
                WarpPortableGeneratedServiceKind.Arena).Function);
        }
        context.RequireService(LeaseSemantics);
        context.RequireService(WarpManagedInvocationOpCode.Version);
        context.RequireService(SourceSemantics + "/required-host-domain/non-null-prepared-exception-operands-exact-owned-ticket");
        context.RequireService(OwnershipSemantics + "/" + InstructionOwnershipHash);
    }

    internal override WarpBlockTerminator? LowerInstruction(WarpPortableWordInstructionContext context)
    {
        OpCode operation = context.SourceInstruction.OpCode;
        if (!OwnsInstruction(context.SourceMethod.Identity, context.Instruction)) { return null; }
        if (operation == OpCodes.Throw) { return Throw(context); }
        if (operation == OpCodes.Rethrow) { return Rethrow(context); }
        if (operation == OpCodes.Leave || operation == OpCodes.Leave_S) { return Leave(context); }
        if (operation == OpCodes.Endfinally) { return EndCleanup(context); }
        if (operation == OpCodes.Endfilter) { return EndFilter(context); }
        if (operation == OpCodes.Ret) { return Return(context); }
        if (operation == OpCodes.Isinst && filtering >= 0) { return IsInstance(context); }
        return null;
    }

    internal override void AfterSourceLowering(WarpPortableWordBindingPreparation context)
    {
        ImmutableArray<WarpPortableWordBody> aliases = InstallAliases(context);
        WarpStateDispatchTarget[] destinations = context.SourceBodies.Concat(aliases).SelectMany(body => body.SourceBlocks.Select(block =>
            new WarpStateDispatchTarget(body.Function, block.Block))).Append(new(driving + 1, 0))
            .Concat(aliases.Select(body => new WarpStateDispatchTarget(body.Function, 0))).ToArray();
        Install(context, WarpPortableExceptionTransferLowerer.Create(transfer, destinations));
        Install(context, WarpPortableExceptionTerminalLowerer.Create(terminal, destinations));
        Install(context, WarpPortableExceptionSourceDriver.Create(driving, entries, transfer, terminal, controller, destinations, filtering));
        if (filtering >= 0) { Install(context, WarpPortableExceptionFilterLowerer.Create(filtering, aliases.Max(body => body.PrivateWordCount), destinations)); }
    }

    internal override string Complete(WarpPortableWordBindingCompletion context)
    {
        var layout = new WarpLogicalMachineLayout(context.StructuralKernel);
        ImmutableArray<WarpPortableExceptionBodyBinding> bodies = context.Bodies.Select(body => new WarpPortableExceptionBodyBinding(
            body.MethodIdentity, body.Function, body.PrivateWordCount, body.EvaluationWordOffset,
            body.SourceBlocks.Select((block, index) => new WarpPortableExceptionSiteBinding(block.Instruction.Offset,
                index == 0 ? [.. block.GeneratedBlocks, 0] : block.GeneratedBlocks)).ToImmutableArray())
            { AliasOwnerFunction = body.AliasOwnerFunction, AliasPrefixWords = body.AliasPrefixWords,
                PrivateTemporaries = body.PrivateTemporaries }).ToImmutableArray();
        preparedPlan = WarpPortableExceptionPlan.Create(context.Graph, context.Program, schema, layout, bodies);
        context.AttachExceptionPlan(preparedPlan);
        return preparedPlan.PlanHash;
    }

    private static void Install(WarpPortableWordBindingPreparation context, WarpControlFlowFunction function) =>
        context.InstallFunction(function, new(0, true, new int[function.Blocks.Count]));

    private static bool Required(WarpPortableWordBindingPreparation context, string name)
    {
        short? operation = name switch
        {
            nameof(WarpPortableExceptionServices.RaiseReference) => OpCodes.Throw.Value,
            nameof(WarpPortableExceptionServices.Rethrow) => OpCodes.Rethrow.Value,
            nameof(WarpPortableExceptionServices.EndCleanupAt) => OpCodes.Endfinally.Value,
            nameof(WarpPortableExceptionServices.PrepareFilterEnd) => OpCodes.Endfilter.Value,
            nameof(WarpPortableExceptionServices.RefreshContinuation) => OpCodes.Endfilter.Value,
            nameof(WarpPortableExceptionServices.ReleaseCaughtForReturn) => OpCodes.Ret.Value,
            nameof(WarpPortableExceptionServices.BeginLeave) => OpCodes.Leave.Value,
            _ => null,
        };
        return operation is null || context.SourceBodies.Any(body => body.SourceBlocks.Any(block =>
            block.Instruction.OpCode == operation || operation == OpCodes.Leave.Value && block.Instruction.OpCode == OpCodes.Leave_S.Value));
    }

    private static string Identity(WarpPortableMethodGraph graph, WarpPortableTypedProgram program, WarpPortableSourceHeapSchema schema, uint controller) =>
        Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(new
        {
            SourceSemantics, LeaseSemantics, graph.GraphHash, program.VerifiedHash, schema.SchemaHash, Controller = controller,
            Invocation = WarpManagedInvocationOpCode.Version,
            Runtime = WarpPortableExceptionLayout.Semantics, Transfer = WarpPortableExceptionTransferLowerer.Semantics,
            InstructionOwnership = OwnershipHash(graph, program, schema.SchemaHash),
            Terminal = WarpPortableExceptionTerminalLowerer.Semantics, Machine = WarpLogicalMachineLayout.Version,
            Domain = "prepared-nonnull-exception-operands;general-null-and-implicit-factory-admission-denied",
        })));
}
