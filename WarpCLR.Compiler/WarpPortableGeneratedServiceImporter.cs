using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed class WarpPortableGeneratedServiceImporter
{
    internal const string Semantics = "warp.source-services/verified-word-ir-explicit-bank-schema-binding-zero-source-charge-raw-utf16-snapshot/0.2";
    private readonly string schemaHash;
    private readonly Func<string, int> reserve;
    private readonly Action<WarpControlFlowFunction, WarpLogicalBodyMetadata> store;
    private readonly Dictionary<string, WarpPortableGeneratedServiceImport> imported = new(StringComparer.Ordinal);

    internal WarpPortableGeneratedServiceImporter(string typeSchemaHash, Func<string, int> reserve,
        Action<WarpControlFlowFunction, WarpLogicalBodyMetadata> store)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeSchemaHash); ArgumentNullException.ThrowIfNull(reserve); ArgumentNullException.ThrowIfNull(store);
        if (typeSchemaHash.Length != 64 || !typeSchemaHash.All(Uri.IsHexDigit)) { throw new ArgumentException("A generated source service requires an exact schema SHA256.", nameof(typeSchemaHash)); }
        schemaHash = typeSchemaHash; this.reserve = reserve; this.store = store;
    }

    internal ImmutableArray<WarpPortableGeneratedServiceImport> Imports => imported.Values.OrderBy(service => service.Identity, StringComparer.Ordinal).ToImmutableArray();

    internal WarpPortableGeneratedServiceImport Import(MethodInfo method, string semanticIdentity,
        WarpPortableGeneratedServiceKind kind, IReadOnlyDictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>>? banks = null)
    {
        ArgumentNullException.ThrowIfNull(method); ArgumentException.ThrowIfNullOrEmpty(semanticIdentity);
        Validate(method, kind, banks);
        WarpControlFlowKernel kernel = Compile(method, kind, banks);
        string signature = Signature(method, kind);
        string irHash = WarpIrHash.Compute(kernel);
        string identity = Semantics + "/" + semanticIdentity + "/" + schemaHash + "/" + signature + "/" + irHash;
        if (imported.TryGetValue(identity, out WarpPortableGeneratedServiceImport? existing)) { return existing; }
        int entry = reserve(identity);
        var remap = new Dictionary<int, int>();
        string helperPrefix = Semantics + "/" + semanticIdentity + "/" + schemaHash + "/" + kind.ToString() + "/helper/";
        foreach (WarpControlFlowFunction helper in kernel.Functions) { remap.Add(helper.Id, reserve(helperPrefix + helper.Name)); }
        int parameters = method.GetParameters().Count(parameter => parameter.ParameterType == typeof(uint));
        Store(entry, identity, parameters, kernel.Blocks, remap, true);
        foreach (WarpControlFlowFunction helper in kernel.Functions) { Store(remap[helper.Id], helperPrefix + helper.Name, helper.ParameterCount, helper.Blocks, remap, false); }
        var result = new WarpPortableGeneratedServiceImport(entry, parameters, identity, signature, semanticIdentity, irHash, schemaHash,
            kind is WarpPortableGeneratedServiceKind.State or WarpPortableGeneratedServiceKind.StateAndArena);
        imported.Add(identity, result); return result;
    }

    private static void Validate(MethodInfo method, WarpPortableGeneratedServiceKind kind,
        IReadOnlyDictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>>? banks)
    {
        if (!Enum.IsDefined(kind) || !method.IsStatic || method.ContainsGenericParameters || method.ReturnType != typeof(uint) ||
            method.DeclaringType?.Assembly != typeof(WarpPortableGeneratedServiceImporter).Assembly ||
            method.GetParameters().Any(parameter => parameter.ParameterType != typeof(uint) && parameter.ParameterType != typeof(uint[])))
        {
            throw new ArgumentException("Only explicitly bound generated pure UInt32 compiler-runtime services can be imported.", nameof(method));
        }
        int arrays = method.GetParameters().Count(parameter => parameter.ParameterType == typeof(uint[]));
        bool mixed = kind == WarpPortableGeneratedServiceKind.StateAndArena;
        if (kind == WarpPortableGeneratedServiceKind.Words ? arrays != 0 : mixed ? arrays < 2 : arrays != 1)
        {
            throw new ArgumentException("The exact service signature disagrees with its privileged word bank kind.", nameof(kind));
        }
        if (mixed ? banks is null : banks is not null) { throw new ArgumentException("Mixed State/Arena services require explicit immutable closure bindings.", nameof(banks)); }
        ValidateCatalogBanks(method, kind, banks);
    }

    private static void ValidateCatalogBanks(MethodInfo method, WarpPortableGeneratedServiceKind kind,
        IReadOnlyDictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>>? banks)
    {
        ImmutableDictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>> captured = WarpPortableSourceServiceBanks.Capture(method);
        IReadOnlyList<WarpRuntimeWordBank> entry = captured[method];
        bool state = entry.Contains(WarpRuntimeWordBank.State); bool arena = entry.Contains(WarpRuntimeWordBank.Arena);
        WarpPortableGeneratedServiceKind expected = state ? arena ? WarpPortableGeneratedServiceKind.StateAndArena : WarpPortableGeneratedServiceKind.State :
            arena ? WarpPortableGeneratedServiceKind.Arena : WarpPortableGeneratedServiceKind.Words;
        if (kind != expected) { throw new ArgumentException("A caller cannot relabel a catalogued runtime bank capability.", nameof(kind)); }
        if (banks is not null && (banks.Count != captured.Count || captured.Any(pair =>
            !banks.TryGetValue(pair.Key, out IReadOnlyList<WarpRuntimeWordBank>? supplied) || !pair.Value.SequenceEqual(supplied))))
        {
            throw new ArgumentException("Mixed runtime bank bindings must match the complete exact trusted service catalog.", nameof(banks));
        }
    }

    private static WarpControlFlowKernel Compile(MethodInfo method, WarpPortableGeneratedServiceKind kind,
        IReadOnlyDictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>>? banks) => kind switch
    {
        WarpPortableGeneratedServiceKind.Words => new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(method, Math.Max(1, method.GetParameters().Length))).ControlFlow,
        WarpPortableGeneratedServiceKind.Arena => WarpWordArenaServiceLowerer.Lower(method).Kernel,
        WarpPortableGeneratedServiceKind.State => WarpWordStateServiceLowerer.Lower(method).Kernel,
        WarpPortableGeneratedServiceKind.StateAndArena => WarpWordStateArenaServiceLowerer.Lower(method, banks!).Kernel,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string Signature(MethodInfo method, WarpPortableGeneratedServiceKind kind)
    {
        return WarpPortableSnapshotIdentity.Hash(new { Semantics, Method = WarpPortableMethodGraphIdentity.Method(method), BankKind = (int)kind });
    }

    private void Store(int id, string identity, int parameters, IReadOnlyList<WarpBasicBlock> blocks, Dictionary<int, int> remap, bool entry)
    {
        WarpBasicBlock[] lowered = blocks.Select(block => new WarpBasicBlock(block.Id, block.Parameters,
            block.Instructions.Select(instruction => Remap(instruction, remap, entry)), block.Terminator)).ToArray();
        store(new(id, identity, parameters, lowered), new(0, true, Enumerable.Repeat(0, lowered.Length)));
    }

    private static WarpIrInstruction Remap(WarpIrInstruction instruction, Dictionary<int, int> remap, bool entry)
    {
        if (instruction.OpCode == WarpIrOpCode.Call) { return new(instruction.Result, remap[instruction.Callee], instruction.Arguments, instruction.ResultWordCount); }
        WarpIrOpCode operation = entry && instruction.OpCode == WarpIrOpCode.LoadInput ? WarpIrOpCode.LoadArgument : instruction.OpCode;
        return new(instruction.Result, operation, instruction.Left, instruction.Right, instruction.Immediate, instruction.Third,
            instruction.ResultType, instruction.Callee, instruction.Arguments);
    }
}
