using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

[TestClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest creates this fixture through discovery.")]
internal sealed class WarpWordArenaCompilerTests
{
    [TestMethod]
    public void EveryOrdinaryHeapServiceCompilesItsActualClosedCilAndMatchesTheWordOracle()
    {
        MethodInfo[] methods = typeof(WarpPortableHeapServices).GetMethods(BindingFlags.Public | BindingFlags.Static);
        foreach (MethodInfo method in methods)
        {
            if (method.GetParameters().Count(parameter => parameter.ParameterType == typeof(uint[])) != 1)
            {
                continue;
            }

            WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(method);
            CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
            foreach (int quantum in new[] { layout.MaximumBlockCost, 1000000 })
            {
                uint[] expected = Arena();
                uint[] actual = (uint[])expected.Clone();
                object[] arguments = method.GetParameters().Select(parameter =>
                    parameter.ParameterType == typeof(uint[]) ? (object)expected : 0u).ToArray();
                uint result = (uint)method.Invoke(null, arguments)!;
                uint[][] inputs = Enumerable.Range(0, layout.Kernel.InputBufferCount).Select(_ => new uint[1]).ToArray();
                uint[] state = layout.CreateInitialState(16, 1000000);
                int iterations = 0;
                while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
                {
                    Assert.IsLessThan(10000, ++iterations);
                    core.ExecuteManagedQuantum(inputs, [], 0, state, 16, quantum, actual);
                }

                Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset], method.Name);
                Assert.AreEqual(result, state[WarpLogicalMachineLayout.ResultOffset], method.Name);
                CollectionAssert.AreEqual(expected, actual, method.Name);
            }
        }
    }

    [TestMethod]
    public void AllocationAndPreciseCollectionExecuteCompiledWordServicesOnPersistentStorage()
    {
        uint[] arena = Arena();
        Assert.AreEqual(0u, Execute(nameof(WarpPortableHeapServices.AllocateObject), arena, 1));
        var reference = new WarpPortableHeapReference(arena[WarpPortableHeapLayout.Result],
            arena[WarpPortableHeapLayout.Result + 1], arena[WarpPortableHeapLayout.Result + 2]);
        Assert.AreEqual(0u, Execute(nameof(WarpPortableHeapServices.AcquireRoot), arena,
            reference.Context, reference.Slot, reference.Generation, WarpPortableHeapLayout.StrongRoot, 0, 0, 0));
        uint root = arena[WarpPortableHeapLayout.Result];
        uint generation = arena[WarpPortableHeapLayout.Result + 1];
        Assert.AreEqual(0u, Execute(nameof(WarpPortableHeapServices.RequestCollection), arena));
        Assert.AreEqual(0u, Execute(nameof(WarpPortableHeapServices.Collect), arena));
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(0u, Execute(nameof(WarpPortableHeapServices.ReleaseRoot), arena, root, generation));
        Assert.AreEqual(0u, Execute(nameof(WarpPortableHeapServices.RequestCollection), arena));
        Assert.AreEqual(0u, Execute(nameof(WarpPortableHeapServices.Collect), arena));
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]);
    }

    [TestMethod]
    public void AWordCannotForgeAnArenaCapabilityAndAnArenaCannotBecomeAWord()
    {
        foreach (int variant in new[] { 0, 1, 2, 3 })
        {
            var name = new AssemblyName("WarpArenaInvalid" + Guid.NewGuid().ToString("N"));
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
            TypeBuilder type = assembly.DefineDynamicModule(name.Name!).DefineType("InvalidArena", TypeAttributes.Public);
            MethodBuilder method = type.DefineMethod("Map", MethodAttributes.Public | MethodAttributes.Static,
                typeof(uint), [typeof(uint[]), typeof(uint)]);
            ILGenerator il = method.GetILGenerator();
            if (variant < 2)
            {
                il.Emit(variant == 1 ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1);
                il.Emit(variant == 1 ? OpCodes.Conv_U4 : OpCodes.Ldlen);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelema, variant == 2 ? typeof(uint) : typeof(ulong));
            }

            il.Emit(OpCodes.Conv_U4);
            il.Emit(OpCodes.Ret);
            MethodInfo source = type.CreateType()!.GetMethod("Map")!;
            WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpWordArenaServiceLowerer.Lower(source));
            Assert.AreEqual("WRPCIL1017", error.Code, StringComparer.Ordinal);
            Assert.IsNotNull(error.IlOffset);
        }
    }

    [TestMethod]
    public void AddressFormationChecksBoundsEvenWhenTheAddressIsDiscarded()
    {
        var kernel = new WarpControlFlowKernel("managed.address.eager-check", 1, 0,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.LoadInput),
                    new WarpIrInstruction(1, WarpManagedMemoryOpCode.WordAddress, left: 0),
                    new WarpIrInstruction(2, WarpIrOpCode.Constant, immediate: 42)],
                new WarpReturnTerminator(2))]);
        var layout = new WarpLogicalMachineLayout(kernel);
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(1, 100);
        core.ExecuteManagedQuantum([[1]], [], 0, state, 1, 100, [99]);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(WarpLogicalMachineLayout.ManagedMemoryBoundsFault, state[WarpLogicalMachineLayout.FaultKindOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]);
    }

    private static uint Execute(string name, uint[] arena, params uint[] arguments)
    {
        MethodInfo method = typeof(WarpPortableHeapServices).GetMethod(name)!;
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(method);
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[][] inputs = arguments.Length == 0 ? [[0]] : arguments.Select(value => new[] { value }).ToArray();
        uint[] state = layout.CreateInitialState(16, 1000000);
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
        {
            core.ExecuteManagedQuantum(inputs, [], 0, state, 16, layout.MaximumBlockCost, arena);
        }

        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset], name);
        return state[WarpLogicalMachineLayout.ResultOffset];
    }

    private static uint[] Arena() => new WarpPortableHeapSchema(
        [new WarpPortableHeapTypeLayout(1, "test:Object", WarpPortableHeapLayout.Class, 1, [])])
        .CreateArena(7, 256, 16, 16, 16, 256);
}
