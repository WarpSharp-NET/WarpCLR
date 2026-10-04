using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private readonly WarpPortableMethodGraph graph;
    private readonly WarpPortableMethodGraphMethod method;
    private readonly WarpPortableTypedTypeCatalog types;
    private readonly Dictionary<string, WarpPortableMethodGraphMethod> methods;
    private readonly Dictionary<string, WarpPortableMethodGraphField> fields;
    private readonly ImmutableArray<string> argumentTypes;
    private readonly Dictionary<int, int> indices;
    private readonly WarpPortableTypedFlowState?[] states;
    private readonly WarpPortableTypedInstruction?[] instructions;
    private readonly ImmutableArray<WarpPortableTypedExceptionMembership>[] memberships;
    private readonly ImmutableArray<int>[] successors;
    private readonly ImmutableArray<int>[] unwind;
    private readonly Dictionary<int, HashSet<int>> finallyContinuations = [];
    private readonly Dictionary<int, Prefix> prefixes = [];
    private readonly Queue<int> pending = new();
    private readonly HashSet<int> queued = [];
    private int maximumStackWords;
    private long analysisSteps;
    private readonly Dictionary<string, WarpPortableTypedReturnSummary> summaries;
    private readonly bool validateLifetimes;
    private readonly HashSet<WarpPortableTypedProvenance> returned = [];
    private bool returnedReadOnly;

    public WarpPortableTypedMethodVerifier(WarpPortableMethodGraph graph, WarpPortableMethodGraphMethod method,
        WarpPortableTypedTypeCatalog types, Dictionary<string, WarpPortableMethodGraphMethod> methods,
        Dictionary<string, WarpPortableTypedReturnSummary> summaries, bool validateLifetimes = true)
    {
        this.summaries = summaries; this.validateLifetimes = validateLifetimes;
        this.graph = graph; this.method = method; this.types = types; this.methods = methods;
        fields = graph.Fields.ToDictionary(field => field.Identity, StringComparer.Ordinal);
        argumentTypes = ArgumentTypes(method);
        indices = method.Instructions.Select((instruction, index) => (instruction.Offset, Index: index))
            .ToDictionary(pair => pair.Offset, pair => pair.Index);
        states = new WarpPortableTypedFlowState?[method.Instructions.Length];
        instructions = new WarpPortableTypedInstruction?[method.Instructions.Length];
        memberships = new ImmutableArray<WarpPortableTypedExceptionMembership>[method.Instructions.Length];
        successors = new ImmutableArray<int>[method.Instructions.Length];
        unwind = new ImmutableArray<int>[method.Instructions.Length];
        long bytes = argumentTypes.Sum(identity => (long)types.Get(identity).ByteSize +
            (types.Get(identity).Category == WarpPortableStackCategory.ManagedByref ? types.Get(types.Get(identity).ElementType!).ByteSize : 0)) + method.LocalTypes.Sum(identity => (long)types.Get(identity).ByteSize);
        Workspace = method.Instructions.Length * (bytes + method.MaximumStack * (long)MaximumValueWords());
    }

    public long Workspace { get; }

    public WarpPortableTypedMethod Verify()
    {
        if (!method.Instructions.IsEmpty)
        {
            PrepareControlFlow();
            Enqueue(0, InitialState());
            while (pending.TryDequeue(out int index))
            {
                queued.Remove(index);
                WarpCLR.IR.WarpCompilationAdmission.Require(method.Identity, WarpCLR.IR.WarpCompilationResourceKind.VerifierWorkspaceSlots,
                    ++analysisSteps, WarpCLR.IR.WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
                Analyze(index);
            }
        }

        var result = ImmutableArray.CreateBuilder<WarpPortableTypedInstruction>(method.Instructions.Length);
        for (int index = 0; index < method.Instructions.Length; index++)
        {
            WarpPortableMethodGraphInstruction source = method.Instructions[index];
            result.Add(instructions[index] ?? new(source.Offset, source.NextOffset, source.OpCode.Value, false,
                [], [], [], [], memberships[index], successors[index], [], unwind[index], [], [], [], null, 0, false, null));
        }

        int privateWords = argumentTypes.Sum(identity => types.Get(identity).WordCount) + method.LocalTypes.Sum(identity => types.Get(identity).WordCount);
        WarpCLR.IR.WarpCompilationAdmission.Require(method.Identity, WarpCLR.IR.WarpCompilationResourceKind.ValueSlots,
            (long)privateWords + maximumStackWords, WarpCLR.IR.WarpCompilationAdmission.MaximumValueSlotsPerEntry);
        return new(method.Identity, argumentTypes, method.LocalTypes, method.ReturnType, result.MoveToImmutable(), maximumStackWords,
            checked(privateWords + maximumStackWords), method.Intrinsic,
            method.Instructions.IsEmpty && summaries.TryGetValue(method.Identity, out WarpPortableTypedReturnSummary? summary) ? summary :
                new(SortOrigins(returned), returnedReadOnly));
    }

    private ImmutableArray<string> ArgumentTypes(WarpPortableMethodGraphMethod target)
    {
        if (target.SourceMethod.IsStatic) { return target.ParameterTypes; }
        Type declaring = target.SourceMethod.DeclaringType!;
        string receiver = types.Get(declaring.IsValueType ? declaring.MakeByRefType() : declaring).Identity;
        return [receiver, .. target.ParameterTypes];
    }

    private WarpVerificationException Error(string message, int offset) =>
        new("WRPCLR2200", $"{method.Identity}: {message}", offset);

    private sealed record Prefix(string? Constrained = null, bool ReadOnly = false, bool Volatile = false, int Alignment = 0);
}
