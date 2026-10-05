using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourcePlan
{
    internal const string Semantics = "warp.runtime-host.compiler-sealed-source-controller-exact-frame-owner-root-projection/0.6";
    private readonly Lazy<CoreCLRResumableKernel> kernel;
    private readonly FrozenDictionary<int, WarpPortableWordBody> bodies;
    private readonly FrozenDictionary<(int Function, int Block), WarpPortableWordSourceBlock> sourceBlocks;
    private readonly FrozenDictionary<(int Function, int PC), uint> maps;
    private readonly FrozenDictionary<(int Function, int Block), bool> sourceLoans;
    private readonly FrozenDictionary<string, WarpPortableTypedType> rootTypes;
    private readonly FrozenDictionary<uint, WarpPortableSourceFrameBody> frameBodies;

    internal WarpCompiledSourcePlan(WarpPortableMethodGraph graph, WarpPortableSourceHeapSchema typeSchema,
        WarpPortableWordLoweredProgram program, uint workers, uint residents,
        int maximumDepth, long maximumSteps, int operationalQuantum, WarpCompiledRuntimeServices services)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSteps);
        CompilerIdentity = WarpPortableWordLowerer.RequireRuntimeCompilerSeal(graph, typeSchema, program);
        TypeSchema = typeSchema;
        Program = program;
        FrameSchema = program.RequiredServices.Contains(WarpPortableSourceFrameSchema.Semantics, StringComparer.Ordinal) ||
            CompilerIdentity.ExceptionAttachment is not null ? WarpPortableSourceFrameSchema.Create(typeSchema, program) : null;
        rootTypes = program.VerifiedProgram.Types.ToFrozenDictionary(type => type.Identity, StringComparer.Ordinal);
        frameBodies = FrameSchema is null ? FrozenDictionary<uint, WarpPortableSourceFrameBody>.Empty :
            FrameSchema.Bodies.ToFrozenDictionary(body => body.Function);
        Services = services;
        Layout = new(program.Kernel);
        Quantum = Math.Max(Layout.MaximumBlockCost, operationalQuantum);
        MaximumDepth = maximumDepth;
        MaximumSteps = maximumSteps;
        Workers = workers;
        Residents = residents;
        bodies = program.Bodies.ToFrozenDictionary(body => body.Function);
        sourceBlocks = program.Bodies.SelectMany(body => body.SourceBlocks.SelectMany(block =>
            block.GeneratedBlocks.Select(generated => (Key: (body.Function, generated), Value: block))))
            .ToFrozenDictionary(pair => pair.Key, pair => pair.Value);
        RequireBoundOwners(program);
        sourceLoans = SourceLoanMap(program);
        InputRootWords = program.EntryProjection.WrapperInputRoots.Select(root => root.SsaWordOffset).ToImmutableArray();
        ResultRootWords = program.EntryProjection.ResultRoots.Select(root => root.ResultWordOffset).ToImmutableArray();
        int frameRoots = Math.Max(1, program.Bodies.SelectMany(body => body.SourceBlocks.Select(block =>
            FrameRootReservation(body, block))).Max());
        RootTupleCount = checked(frameRoots * maximumDepth + InputRootWords.Length +
            program.EntryProjection.WrapperInputRoots.Length + program.EntryProjection.WrapperReturnedRoots.Length +
            Math.Max(1, ResultRootWords.Length));
        RootBankWords = checked(RootTupleCount * 3);
        StateWords = checked(RootBankWords + Math.Max(1, Layout.ResultWordCount));
        uint[] rootOffsets = Enumerable.Range(0, RootTupleCount).Select(index => checked((uint)(index * 3))).ToArray();
        var rootMaps = new List<WarpPortableSchedulerRootLayout> { new(0, 0, rootOffsets) };
        var indices = new Dictionary<(int Function, int PC), uint> { [(0, 0)] = 0 };
        foreach (WarpLogicalMachineNode node in Layout.Nodes.Where(node => bodies.ContainsKey(node.Function)))
        {
            indices[(node.Function, node.ProgramCounter)] = checked((uint)rootMaps.Count);
            rootMaps.Add(new(checked((uint)node.Function), checked((uint)node.ProgramCounter), rootOffsets));
        }
        maps = indices.ToFrozenDictionary();
        uint[] outputs = ResultRootWords.Select(offset => checked((uint)(RootBankWords + offset))).ToArray();
        Schema = new(workers, residents, checked((uint)Quantum), checked((uint)maximumDepth),
            unchecked((uint)maximumSteps), checked((uint)((ulong)maximumSteps >> 32)), checked((uint)StateWords), rootMaps, [], outputs);
        string canonical = string.Join('\n', Semantics, CompilerIdentity.IdentityHash, program.GraphHash, program.VerifiedHash, program.LoweredHash, program.MapsHash,
            WarpIrHash.Compute(program.Kernel), WarpLogicalMachineLayout.Version, WarpPortableHeapLayout.Semantics,
            WarpPortableSchedulerLayout.Semantics, WarpManagedAtomicKernels.Semantics, WarpPortableHostPublicationServices.Semantics, services.Identity,
            FrameSchema?.FrameSchemaHash ?? "no-source-frame-schema",
            FrameRootSemantics,
            maximumDepth.ToString(CultureInfo.InvariantCulture), maximumSteps.ToString(CultureInfo.InvariantCulture),
            Quantum.ToString(CultureInfo.InvariantCulture), workers.ToString(CultureInfo.InvariantCulture), residents.ToString(CultureInfo.InvariantCulture),
            string.Join(',', InputRootWords), string.Join(',', ResultRootWords), string.Join('\n', program.RequiredServices),
            string.Join(';', sourceLoans.OrderBy(pair => pair.Key.Function).ThenBy(pair => pair.Key.Block)
                .Select(pair => pair.Key.Function.ToString(CultureInfo.InvariantCulture) + ":" +
                    pair.Key.Block.ToString(CultureInfo.InvariantCulture) + ":" + pair.Value.ToString(CultureInfo.InvariantCulture))));
        Identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        services.PrepareCleanup();
        kernel = new(() => CoreCLRResumableKernel.Compile(Layout));
    }

    internal WarpPortableWordProgramIdentity CompilerIdentity { get; }
    internal WarpPortableSourceHeapSchema TypeSchema { get; }
    internal WarpPortableSourceFrameSchema? FrameSchema { get; }
    internal WarpPortableWordLoweredProgram Program { get; }
    internal WarpCompiledRuntimeServices Services { get; }
    internal WarpLogicalMachineLayout Layout { get; }
    internal CoreCLRResumableKernel Kernel => kernel.Value;
    internal bool HasLocalSourceKernel => kernel.IsValueCreated;
    internal WarpPortableSchedulerSchema Schema { get; }
    internal string Identity { get; }
    internal uint Workers { get; }
    internal uint Residents { get; }
    internal int MaximumDepth { get; }
    internal long MaximumSteps { get; }
    internal int Quantum { get; }
    internal int RootTupleCount { get; }
    internal int RootBankWords { get; }
    internal int StateWords { get; }
    internal ImmutableArray<int> InputRootWords { get; }
    internal ImmutableArray<int> ResultRootWords { get; }

    internal uint[] CreateUnusedHeap(uint context, uint payloadWords, uint maximumObjects, uint maximumRoots, uint quotaWords) =>
        FrameSchema is null ? TypeSchema.CreateArena(context, payloadWords, maximumObjects, maximumRoots, Workers, quotaWords) :
            TypeSchema.CreateArena(context, payloadWords, maximumObjects, maximumRoots, Workers, quotaWords, FrameSchema);

    internal uint RootMap(WarpCompiledSourceLocation location) => maps[(location.Function, location.ProgramCounter)];

    internal WarpCompiledSourceLocation Location(uint[] state)
    {
        for (int depth = checked((int)state[WarpLogicalMachineLayout.DepthOffset]); depth > 0; depth--)
        {
            int frame = checked(WarpLogicalMachineLayout.HeaderWords + (depth - 1) * Layout.FrameWords);
            int function = checked((int)state[frame + WarpLogicalMachineLayout.FrameFunctionOffset]);
            if (bodies.TryGetValue(function, out WarpPortableWordBody? body))
            {
                int pc = checked((int)state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]);
                WarpLogicalMachineNode node = Layout.Nodes[pc];
                sourceBlocks.TryGetValue((function, node.Block), out WarpPortableWordSourceBlock? block);
                return new(function, pc, body, block);
            }
        }
        return new(0, 0, null, null);
    }

    internal ImmutableArray<WarpCompiledSourceLocation> SourceFrames(uint[] state)
    {
        var frames = ImmutableArray.CreateBuilder<WarpCompiledSourceLocation>();
        for (int depth = 0; depth < state[WarpLogicalMachineLayout.DepthOffset]; depth++)
        {
            int frame = checked(WarpLogicalMachineLayout.HeaderWords + depth * Layout.FrameWords);
            int function = checked((int)state[frame + WarpLogicalMachineLayout.FrameFunctionOffset]);
            if (!bodies.TryGetValue(function, out WarpPortableWordBody? body)) { continue; }
            int pc = checked((int)state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]);
            sourceBlocks.TryGetValue((function, Layout.Nodes[pc].Block), out WarpPortableWordSourceBlock? block);
            frames.Add(new(function, pc, body, block));
        }
        return frames.ToImmutable();
    }

    internal WarpCompiledSourceLocation FaultLocation(uint[] state)
    {
        int function = checked((int)state[WarpLogicalMachineLayout.FaultFunctionOffset]);
        int block = checked((int)state[WarpLogicalMachineLayout.FaultBlockOffset]);
        bodies.TryGetValue(function, out WarpPortableWordBody? body);
        sourceBlocks.TryGetValue((function, block), out WarpPortableWordSourceBlock? source);
        return new(function, Layout.GetBlockEntry(function, block), body, source);
    }

}
