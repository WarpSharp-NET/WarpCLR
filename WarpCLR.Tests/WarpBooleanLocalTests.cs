using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Sdk;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpBooleanLocalTests
{
    private static readonly uint[] Values =
        [0, 1, 2, 127, 128, 254, 255, 256, 257, 258, 511, 512, 513, 65535, 65536, 65537, 0x7FFFFFFF, 0x80000000, 0xFFFFFF00, uint.MaxValue];

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void CilMethodRequiresCompleteSupportedLocalStorageTypes()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new WarpIntegerMapMethodBody("missing-types", 1, 1, 1, default, []));
        WarpVerificationException unsupported = Assert.ThrowsExactly<WarpVerificationException>(() =>
            new WarpIntegerMapMethodBody("unsupported-types", 1, 1, 1, [WarpMetadataType.Unsupported], []));
        Assert.AreEqual("WRPCIL1000", unsupported.Code, StringComparer.Ordinal);
        WarpCompilationResourceException oversized = Assert.ThrowsExactly<WarpCompilationResourceException>(() =>
            new WarpIntegerMapMethodBody("local-limit", 1, 1, 1,
                Enumerable.Repeat(WarpMetadataType.Boolean, WarpCompilationAdmission.MaximumLocalsPerBody + 1).ToImmutableArray(), []));
        Assert.AreEqual(WarpCompilationResourceKind.Locals, oversized.Resource);
    }

    [TestMethod]
    public void BooleanStoreExpansionStillObeysCommonLoweredInstructionAdmission()
    {
        int stores = (WarpCompilationAdmission.MaximumInstructionsPerEntry / 3) + 1;
        var il = new byte[(stores * 2) + 2];
        for (int store = 0; store < stores; store++)
        {
            il[store * 2] = 0x16; // ldc.i4.0
            il[(store * 2) + 1] = 0x0A; // stloc.0
        }

        il[^2] = 0x06; // ldloc.0
        il[^1] = 0x2A; // ret
        var body = new WarpIntegerMapMethodBody("boolean-expansion", 0, 0, 1, [WarpMetadataType.Boolean], il);
        WarpCompilationResourceException error = Assert.ThrowsExactly<WarpCompilationResourceException>(() =>
            WarpIntegerMapCilVerifier.Verify(body));
        Assert.AreEqual(WarpCompilationResourceKind.Instructions, error.Resource);
        Assert.IsGreaterThan(WarpCompilationAdmission.MaximumInstructionsPerEntry, error.Requested);
    }

    [TestMethod]
    [DataRow(WarpBackendKind.NVPTX)]
    [DataRow(WarpBackendKind.AMDGPU)]
    [DataRow(WarpBackendKind.SPIRV)]
    public void ProductionNativeMachineEmissionIncludesTheSharedBooleanByteMask(WarpBackendKind backend)
    {
        using var fixture = new WarpBooleanProofFixture();
        WarpRuntimeModule module = Load(fixture);
        foreach (string name in WarpBooleanProofFixture.MethodNames)
        {
            WarpRuntimeEntry entry = module.Entries[WarpBooleanProofFixture.Identity(name)];
            string source = WarpPortableMachineEmitter.Emit(entry.Layout, backend);
            if (name is not "Initialized" and not "UInt32Control")
            {
                StringAssert.Contains(source, " = and i32 ", StringComparison.Ordinal);
                StringAssert.Contains(source, "store i32 255, ptr ", StringComparison.Ordinal);
                IEnumerable<WarpBasicBlock> blocks = entry.Kernel.Blocks.Concat(entry.Kernel.Functions.SelectMany(function => function.Blocks));
                Assert.IsTrue(blocks.Any(block => block.Instructions.Any(instruction =>
                    instruction.OpCode == WarpIrOpCode.BitwiseAnd && block.Instructions.Any(mask =>
                        mask.Result == instruction.Right && mask.OpCode == WarpIrOpCode.Constant && mask.Immediate == byte.MaxValue))));
            }
        }
    }

    [TestMethod]
    public void PersistedRawCilOriginalCoreClrHasByteStorageNotTruthNormalization()
    {
        using var fixture = new WarpBooleanProofFixture();
        MethodBody body = fixture.Method("Map").GetMethodBody()!;
        Assert.AreEqual(1, body.MaxStackSize);
        Assert.IsTrue(body.InitLocals);
        Assert.HasCount(1, body.LocalVariables);
        Assert.AreEqual(typeof(bool), body.LocalVariables[0].LocalType);
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x0A, 0x06, 0x2A }, body.GetILAsByteArray());
        foreach (string name in WarpBooleanProofFixture.MethodNames)
        {
            foreach (uint rounds in Rounds(name))
            {
                uint[] original = Original(fixture.Method(name), rounds);
                uint[] expected = Values.Select(value => Expected(name, value, rounds)).ToArray();
                CollectionAssert.AreEqual(expected, original, name);
            }
        }

        // Retain the exact admitted PE as a test result for subsequent device-free native compiler validation.
        string path = Path.Combine(TestContext.ResultsDirectory!, "BooleanLocalProof.dll");
        File.WriteAllBytes(path, fixture.Bytes);
        TestContext.AddResultFile(path);
        TestContext.WriteLine($"Persisted PE SHA256: {Convert.ToHexString(SHA256.HashData(fixture.Bytes))}; path: {path}");
    }

    [TestMethod]
    [DataRow("Map")]
    [DataRow("Uninitialized")]
    [DataRow("Branch")]
    [DataRow("Helper")]
    [DataRow("Merge")]
    [DataRow("Mixed")]
    [DataRow("Wide")]
    [DataRow("Loop")]
    [DataRow("Initialized")]
    [DataRow("UInt32Control")]
    public void BothIntakesDirectJitAndAllSharedBackendArtifactsMatchOriginalClr(string name)
    {
        using var fixture = new WarpBooleanProofFixture();
        MethodInfo source = fixture.Method(name);
        WarpCompilation reflection = new WarpBuildPipeline().CompileIntegerMap(source, 1);
        WarpModuleCompilation module = new WarpBuildPipeline().CompileModule(fixture.Bytes);
        WarpCompilation metadata = module.Entries[WarpBooleanProofFixture.Identity(name)];
        Assert.HasCount(WarpBooleanProofFixture.MethodNames.Length, module.Entries);
        foreach (WarpCompilation compilation in new[] { reflection, metadata })
        {
            CoreCLRJitKernel executable = CoreCLRJitKernel.Compile(compilation.Kernel);
            Assert.AreNotEqual(IntPtr.Zero, executable.CompiledEntryPoint.MethodHandle.GetFunctionPointer());
            foreach (uint rounds in Rounds(name))
            {
                uint[] original = Original(source, rounds);
                uint[] scalars = Scalars(name, rounds);
                var actual = new uint[Values.Length];
                for (int worker = 0; worker < actual.Length; worker++)
                {
                    actual[worker] = executable.Invoke([Values], scalars, worker, new CoreCLRExecutionBudget(100000, 8));
                }

                CollectionAssert.AreEqual(original, actual, name);
                foreach (WarpBackendKind backend in WarpBackendCatalog.Required)
                {
                    WarpBackendArtifact artifact = compilation.Artifacts[backend];
                    BackendArtifactAssertions.IsValid(artifact, backend, compilation.Kernel);
                    uint[] emulated = new WarpIntegerMapSemanticEmulator().Execute(artifact, compilation.Kernel, [Values], scalars);
                    CollectionAssert.AreEqual(original, emulated, $"{name}/{backend} (development emulator, not hardware)");
                }
            }
        }
    }

    [TestMethod]
    public void ResumableJitPreservesBooleanStorageAcrossHelperFramesMergesBackedgesAndQuanta()
    {
        using var fixture = new WarpBooleanProofFixture();
        WarpRuntimeModule module = Load(fixture);
        foreach (string name in WarpBooleanProofFixture.MethodNames)
        {
            WarpLogicalMachineLayout layout = module.Entries[WarpBooleanProofFixture.Identity(name)].Layout;
            CoreCLRResumableKernel executable = CoreCLRResumableKernel.Compile(layout);
            foreach (uint rounds in Rounds(name))
            {
                uint[] original = Original(fixture.Method(name), rounds);
                for (int worker = 0; worker < Values.Length; worker++)
                {
                    uint[] baseline = Run(executable, Scalars(name, rounds), worker, layout.MaximumBlockCost);
                    Assert.AreEqual(original[worker], baseline[WarpLogicalMachineLayout.ResultOffset], name);
                    foreach (int quantum in new[] { layout.MaximumBlockCost + 1, layout.MaximumBlockCost * 3, 65536 })
                    {
                        CollectionAssert.AreEqual(baseline, Run(executable, Scalars(name, rounds), worker, quantum), name);
                    }
                }
            }
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(65536)]
    public async Task TrustedPeProductionDispatchMatchesOriginalCoreClrAtDifferentQuanta(int quantumVariant)
    {
        using var fixture = new WarpBooleanProofFixture();
        WarpRuntimeModule module = Load(fixture);
        int maximumBlockCost = module.Entries.Values.Max(entry => entry.Layout.MaximumBlockCost);
        int quantum = quantumVariant switch
        {
            0 => maximumBlockCost,
            1 => maximumBlockCost + 1,
            3 => maximumBlockCost * 3,
            _ => quantumVariant,
        };
        var options = new WarpRuntimeOptions { ExecutionQuantum = quantum, MaximumResidentWorkers = 3, MaximumParallelWorkers = 2 };
        var context = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, options);
        await using var lease = context.ConfigureAwait(false);
        foreach (string name in WarpBooleanProofFixture.MethodNames)
        {
            foreach (uint rounds in Rounds(name))
            {
                uint[] actual = await context.DispatchIntegerMapAsync(WarpBooleanProofFixture.Identity(name),
                    [Values], Scalars(name, rounds)).ConfigureAwait(false);
                CollectionAssert.AreEqual(Original(fixture.Method(name), rounds), actual, name);
            }
        }

        Assert.AreEqual(WarpRuntimeContextState.Ready, context.State);
        Assert.AreEqual(WarpBooleanProofFixture.MethodNames.Length, context.JitStatistics.CompilationCount);
    }

    private static WarpRuntimeModule Load(WarpBooleanProofFixture fixture) =>
        WarpRuntimeModule.Load(fixture.Bytes, new WarpModuleTrust([Convert.ToHexString(SHA256.HashData(fixture.Bytes))]));

    private static uint[] Run(CoreCLRResumableKernel executable, uint[] scalars, int worker, int quantum)
    {
        uint[] state = executable.Layout.CreateInitialState(8, 100000);
        for (int iteration = 0; iteration < 100000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; iteration++)
        {
            executable.ExecuteQuantum([Values], scalars, worker, state, 8, quantum);
        }

        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return state;
    }

    private static uint[] Original(MethodInfo method, uint rounds) => method.GetParameters().Length == 1
        ? Values.Select(method.CreateDelegate<Func<uint, uint>>()).ToArray()
        : Values.Select(value => method.CreateDelegate<Func<uint, uint, uint>>()(value, rounds)).ToArray();

    private static uint[] Scalars(string name, uint rounds) => string.Equals(name, "Loop", StringComparison.Ordinal) ? [rounds] : [];

    private static uint[] Rounds(string name) => string.Equals(name, "Loop", StringComparison.Ordinal) ? [0, 1, 2, 17, 256, 257] : [0];

    private static uint Expected(string name, uint value, uint rounds) => name switch
    {
        "Initialized" => 0,
        "UInt32Control" => value,
        "Branch" => (value & 255) == 0 ? 0xCAFEu : value & 255,
        "Merge" => unchecked(value + ((value & 1) == 0 ? 1u : 0u)) & 255,
        "Mixed" => value ^ (value & 255),
        "Loop" => unchecked(value + rounds) & 255,
        _ => value & 255,
    };
}
