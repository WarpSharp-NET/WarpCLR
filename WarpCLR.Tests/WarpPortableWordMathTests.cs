using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpPortableWordMathTests
{
    private static readonly Type[] ShortPair = [typeof(short), typeof(short)];

    [TestMethod]
    public void AllThirtyOneMissingFixedWidthAndEnumSignaturesDiscoverAndBindExactly()
    {
        Type[] narrowAndWide = [typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(uint), typeof(long), typeof(ulong)];
        var methods = new List<MethodInfo>();
        foreach (Type type in narrowAndWide)
        {
            methods.Add(Standard(typeof(Math), "Min", [type, type])); methods.Add(Standard(typeof(Math), "Max", [type, type]));
            methods.Add(Standard(typeof(Math), "Clamp", [type, type, type]));
        }
        foreach (Type type in new[] { typeof(sbyte), typeof(short), typeof(long) })
        {
            methods.Add(Standard(typeof(Math), "Abs", [type])); methods.Add(Standard(typeof(Math), "Sign", [type]));
        }
        methods.Add(Standard(typeof(Math), "Round", [typeof(double), typeof(MidpointRounding)]));
        methods.Add(Standard(typeof(Math), "Round", [typeof(double), typeof(int), typeof(MidpointRounding)]));
        methods.Add(Standard(typeof(MathF), "Round", [typeof(float), typeof(MidpointRounding)]));
        methods.Add(Standard(typeof(MathF), "Round", [typeof(float), typeof(int), typeof(MidpointRounding)]));
        Assert.HasCount(31, methods);
        foreach (ref readonly MethodInfo method in CollectionsMarshal.AsSpan(methods))
        {
            WarpPortableWordMathBinding binding = WarpPortableWordMathCatalog.Resolve(method)!;
            Assert.IsNotNull(binding);
            WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(Wrapper(method));
            WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
            string operation = "math.strict." + method.Name;
            Assert.IsTrue(graph.Intrinsics.Any(identity => identity.Contains(operation, StringComparison.Ordinal)));
            Assert.IsTrue(binding.Identity.Contains(method.ReturnType.FullName!, StringComparison.Ordinal));
            foreach (string entry in binding.ValueEntrypoints.Append(binding.FaultEntrypoint).OfType<string>())
            {
                MethodInfo implementation = binding.Implementation.GetMethod(entry, BindingFlags.Public | BindingFlags.Static)!;
                Assert.IsNotNull(implementation); Assert.AreEqual(typeof(uint), implementation.ReturnType);
                Assert.IsTrue(implementation.GetParameters().All(parameter => parameter.ParameterType == typeof(uint)));
            }
            Assert.AreEqual(graph.GraphHash, typed.GraphHash, StringComparer.Ordinal);
        }
    }

    [TestMethod]
    public void GeneratedIntegerAbsFaultsAtEachDeclaredMinimumWithoutWidening()
    {
        foreach ((Type type, uint minimum, uint nearby, uint magnitude) in new[]
        {
            (typeof(sbyte), 0xFFFFFF80u, 0xFFFFFF81u, 127u),
            (typeof(short), 0xFFFF8000u, 0xFFFF8001u, 32767u),
            (typeof(int), 0x80000000u, 0x80000001u, 0x7FFFFFFFu),
        })
        {
            WarpPortableWordMathBinding binding = WarpPortableWordMathCatalog.Resolve(Standard(typeof(Math), "Abs", [type]))!;
            uint width = binding.AdditionalValueWords[0];
            Assert.AreEqual(2u, Service(binding, binding.FaultEntrypoint!, [minimum, width]));
            Assert.AreEqual(0u, Service(binding, binding.FaultEntrypoint!, [nearby, width]));
            Assert.AreEqual(magnitude, Service(binding, binding.ValueEntrypoints[0], [nearby, width]));
            Assert.AreEqual(typeof(OverflowException).FullName, binding.Faults[0].ExceptionType, StringComparer.Ordinal);
        }
        WarpPortableWordMathBinding wide = WarpPortableWordMathCatalog.Resolve(Standard(typeof(Math), "Abs", [typeof(long)]))!;
        Assert.AreEqual(2u, Service(wide, wide.FaultEntrypoint!, [0, 0x80000000]));
        Assert.AreEqual(uint.MaxValue, Service(wide, wide.ValueEntrypoints[0], [1, 0x80000000]));
        Assert.AreEqual(0x7FFFFFFFu, Service(wide, wide.ValueEntrypoints[1], [1, 0x80000000]));
    }

    [TestMethod]
    public void IntegerMinMaxAndSignExecuteWithTheirDeclaredSignednessAndReturnWidth()
    {
        foreach (Type type in new[] { typeof(sbyte), typeof(short), typeof(int), typeof(long) })
        {
            bool wide = type == typeof(long);
            uint[] arguments = wide ? [uint.MaxValue, uint.MaxValue, 1, 0] : [uint.MaxValue, 1];
            CollectionAssert.AreEqual(wide ? new uint[] { uint.MaxValue, uint.MaxValue } : [uint.MaxValue], Execute(Lower(Wrapper(Standard(typeof(Math), "Min", [type, type]), typeof(int), wide)), arguments));
            CollectionAssert.AreEqual(wide ? new uint[] { 1, 0 } : [1], Execute(Lower(Wrapper(Standard(typeof(Math), "Max", [type, type]), typeof(int), wide)), arguments));
            CollectionAssert.AreEqual(new uint[] { uint.MaxValue }, Execute(Lower(Wrapper(Standard(typeof(Math), "Sign", [type]))), wide ? [uint.MaxValue, uint.MaxValue] : [uint.MaxValue]));
        }
        CollectionAssert.AreEqual(new uint[] { 1, 0 }, Execute(Lower(Wrapper(Standard(typeof(Math), "Min", [typeof(ulong), typeof(ulong)]))), [0, 0x80000000, 1, 0]));
        CollectionAssert.AreEqual(new uint[] { 0, 0x80000000 }, Execute(Lower(Wrapper(Standard(typeof(Math), "Max", [typeof(ulong), typeof(ulong)]))), [0, 0x80000000, 1, 0]));
        foreach ((Type type, uint maximum) in new[] { (typeof(byte), 255u), (typeof(ushort), 65535u), (typeof(uint), uint.MaxValue) })
        {
            Assert.AreEqual(1u, Execute(Lower(Wrapper(Standard(typeof(Math), "Min", [type, type]))), [maximum, 1])[0]);
            Assert.AreEqual(maximum, Execute(Lower(Wrapper(Standard(typeof(Math), "Max", [type, type]))), [maximum, 1])[0]);
        }
    }

    [TestMethod]
    public void SmallMathCallArgumentsTruncateAndResultsPromoteThroughTheirDeclaredStorage()
    {
        MethodInfo minimum = Standard(typeof(Math), "Min", ShortPair);
        MethodInfo source = Dynamic("NarrowCall", typeof(int), Array.Empty<Type>(), il =>
        {
            il.Emit(OpCodes.Ldc_I4, 0x180); il.Emit(OpCodes.Ldc_I4, 0x181); il.Emit(OpCodes.Call, minimum); il.Emit(OpCodes.Ret);
        });
        CollectionAssert.AreEqual(new uint[] { 0x180 }, Execute(Lower(source), []));
        MethodInfo signed = Dynamic("NarrowSignedCall", typeof(int), Array.Empty<Type>(), il =>
        {
            il.Emit(OpCodes.Ldc_I4, 0x180); il.Emit(OpCodes.Ldc_I4, 0x181); il.Emit(OpCodes.Call, Standard(typeof(Math), "Min", [typeof(sbyte), typeof(sbyte)])); il.Emit(OpCodes.Ret);
        });
        Assert.AreEqual(0xFFFFFF80u, Execute(Lower(signed), Array.Empty<uint>())[0]);
    }

    [TestMethod]
    public void GeneratedIntegerClampUsesSignedAndUnsignedBoundsAndArgumentFaults()
    {
        WarpPortableWordMathBinding signed = WarpPortableWordMathCatalog.Resolve(Standard(typeof(Math), "Clamp", [typeof(long), typeof(long), typeof(long)]))!;
        Assert.AreEqual(0u, Service(signed, signed.FaultEntrypoint!, [0, 0x80000000, uint.MaxValue, 0x7FFFFFFF]));
        Assert.AreEqual(3u, Service(signed, signed.FaultEntrypoint!, [1, 0, uint.MaxValue, uint.MaxValue]));
        Assert.AreEqual(0u, Service(signed, signed.ValueEntrypoints[0], [uint.MaxValue, uint.MaxValue, 0, 0, 5, 0]));
        WarpPortableWordMathBinding unsigned = WarpPortableWordMathCatalog.Resolve(Standard(typeof(Math), "Clamp", [typeof(ulong), typeof(ulong), typeof(ulong)]))!;
        Assert.AreEqual(0u, Service(unsigned, unsigned.FaultEntrypoint!, [0, 0x80000000, uint.MaxValue, uint.MaxValue]));
        Assert.AreEqual(0x80000000u, Service(unsigned, unsigned.ValueEntrypoints[1], [1, 0, 0, 0x80000000, uint.MaxValue, uint.MaxValue]));
        Assert.AreEqual(typeof(ArgumentException).FullName, unsigned.Faults[0].ExceptionType, StringComparer.Ordinal);
    }

    [TestMethod]
    public void GeneratedRoundEnumFaultsRetainDigitBeforeModeOrderingAndRawEnumBits()
    {
        WarpPortableWordMathBinding single = WarpPortableWordMathCatalog.Resolve(Standard(typeof(MathF), "Round", [typeof(float), typeof(MidpointRounding)]))!;
        Assert.AreEqual(0x40000000u, Service(single, single.ValueEntrypoints[0], [0x40200000, 0]));
        Assert.AreEqual(0x40400000u, Service(single, single.ValueEntrypoints[0], [0x40200000, 1]));
        Assert.AreEqual(3u, Service(single, single.FaultEntrypoint!, [uint.MaxValue]));
        WarpPortableWordMathBinding digits = WarpPortableWordMathCatalog.Resolve(Standard(typeof(Math), "Round", [typeof(double), typeof(int), typeof(MidpointRounding)]))!;
        Assert.AreEqual(2u, Service(digits, digits.FaultEntrypoint!, [16, uint.MaxValue]));
        Assert.AreEqual(3u, Service(digits, digits.FaultEntrypoint!, [15, uint.MaxValue]));
        Assert.AreEqual(typeof(ArgumentOutOfRangeException).FullName, digits.Faults[0].ExceptionType, StringComparer.Ordinal);
        Assert.AreEqual(typeof(ArgumentException).FullName, digits.Faults[1].ExceptionType, StringComparer.Ordinal);
    }

    [TestMethod]
    public void FloatingSignReturnsI4AndKeepsItsNaNArithmeticFaultDistinctFromConversionOverflow()
    {
        WarpPortableWordMathBinding binding = WarpPortableWordMathCatalog.Resolve(Standard(typeof(MathF), "Sign", [typeof(float)]))!;
        Assert.AreEqual(uint.MaxValue, Service(binding, binding.ValueEntrypoints[0], [0xBF800000]));
        Assert.AreEqual(1u, Service(binding, binding.FaultEntrypoint!, [0x7FC12345]));
        Assert.HasCount(1, binding.ValueEntrypoints); Assert.AreEqual(typeof(ArithmeticException).FullName, binding.Faults[0].ExceptionType, StringComparer.Ordinal);
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => Lower(Wrapper(Standard(typeof(Math), "Abs", [typeof(long)]))));
        Assert.AreEqual("WRPCLR2300", error.Code, StringComparer.Ordinal);
        Assert.IsTrue(error.Message.Contains("raise/EH", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SourceTranscendentalsAndDefaultRoundExecuteTheirFrozenWordAlgorithms()
    {
        CollectionAssert.AreEqual(new uint[] { 0 }, Execute(Lower(Wrapper(Standard(typeof(MathF), "Sin", [typeof(float)]))), [0]));
        WarpPortableWordLoweredProgram exponential = Lower(Wrapper(Standard(typeof(Math), "Exp", [typeof(double)])));
        CollectionAssert.AreEqual(new uint[] { 0, 0x3FF00000 }, Execute(exponential, [0, 0]));
        Assert.IsTrue(exponential.RequiredServices.Any(identity => identity.Contains(WarpPortableBinary64Transcendentals.Semantics, StringComparison.Ordinal)));
        CollectionAssert.AreEqual(new uint[] { 0x40000000 }, Execute(Lower(Wrapper(Standard(typeof(MathF), "Round", [typeof(float)]))), [0x40200000]));
    }

    [TestMethod]
    public void RootProjectionsKeepSourceCallerReferenceWordsLiveAcrossHelperContinuations()
    {
        WarpPortableWordLoweredProgram program = Lower(typeof(Kernels).GetMethod(nameof(Kernels.Root))!);
        WarpPortableWordBody body = program.Bodies[0];
        WarpPortableWordSourceBlock multiply = body.SourceBlocks.First(block => block.Instruction.OpCode == OpCodes.Mul.Value);
        Assert.IsTrue(multiply.Roots.Any(root => root.Source.Storage is "argument" && root.PrivateWordOffset == body.Arguments[0].WordOffset));
        Assert.IsTrue(multiply.Roots.Any(root => root.Source.Storage is "stack" && root.PrivateWordOffset == body.EvaluationWordOffset));
        var layout = new WarpLogicalMachineLayout(program.Kernel);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout); uint[] state = layout.CreateInitialState(32, 100000);
        uint[][] input = new uint[] { 17, 4, 29, 0, 0x3FF00000, 0, 0x40000000 }.Select(word => new[] { word }).ToArray();
        bool helperObserved = false;
        for (int attempt = 0; attempt < 10000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; attempt++)
        {
            int frame = WarpLogicalMachineLayout.HeaderWords + layout.FrameWords;
            if (state[WarpLogicalMachineLayout.DepthOffset] > 2 && state[frame + WarpLogicalMachineLayout.FrameFunctionOffset] == body.Function &&
                layout.Nodes[(int)state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]].Block == multiply.Block)
            {
                helperObserved = true;
                foreach (WarpPortableWordRoot root in multiply.Roots)
                {
                    CollectionAssert.AreEqual(new uint[] { 17, 4, 29 }, state.Skip(frame + layout.PrivateOffset + root.PrivateWordOffset).Take(3).ToArray());
                }
            }
            compiled.ExecuteQuantum(input, [], 0, state, 32, layout.MaximumBlockCost);
        }
        Assert.IsTrue(helperObserved); Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
    }

    [TestMethod]
    public void ReturnedReferenceAndWrapperRootsDescribeTheActualSsaAndPublishedTupleWords()
    {
        WarpPortableWordLoweredProgram program = Lower(typeof(Kernels).GetMethod(nameof(Kernels.CallReference))!);
        WarpPortableWordBody body = program.Bodies[0];
        WarpPortableWordSourceBlock call = body.SourceBlocks.First(block => block.Instruction.OpCode == OpCodes.Call.Value);
        Assert.HasCount(1, call.ReturnedRoots); Assert.HasCount(1, program.EntryProjection.WrapperInputRoots);
        Assert.HasCount(1, program.EntryProjection.WrapperReturnedRoots); Assert.HasCount(1, program.EntryProjection.ResultRoots);
        Assert.AreEqual(0, program.EntryProjection.WrapperInputRoots[0].SsaWordOffset);
        Assert.AreEqual(3, program.EntryProjection.WrapperReturnedRoots[0].SsaWordOffset);
        Assert.AreEqual(0, program.EntryProjection.ResultRoots[0].ResultWordOffset);
        Assert.AreEqual(program.MapsHash, WarpPortableWordMapHash.Compute(program.Bodies, program.EntryProjection), StringComparer.Ordinal);
        var layout = new WarpLogicalMachineLayout(program.Kernel); CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(32, 100000); uint[][] input = [[17], [4], [29]];
        int continuation = layout.Nodes.First(node => node.Function == body.Function && node.Block == call.Block && node.Call is not null).Continuation;
        bool suspendedObserved = false;
        for (int attempt = 0; attempt < 10000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; attempt++)
        {
            int frame = WarpLogicalMachineLayout.HeaderWords + layout.FrameWords;
            if (state[WarpLogicalMachineLayout.DepthOffset] > 2 && state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset] == continuation)
            {
                suspendedObserved = true;
                foreach (WarpPortableWordRoot root in call.Roots)
                {
                    CollectionAssert.AreEqual(new uint[] { 17, 4, 29 }, state.Skip(frame + layout.PrivateOffset + root.PrivateWordOffset).Take(3).ToArray());
                }
            }
            compiled.ExecuteQuantum(input, [], 0, state, 32, layout.MaximumBlockCost);
        }
        Assert.IsTrue(suspendedObserved); Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        int returned = WarpLogicalMachineLayout.HeaderWords + layout.FrameWords + WarpLogicalMachineLayout.FrameHeaderWords + call.ReturnedRoots[0].SsaWordOffset;
        CollectionAssert.AreEqual(new uint[] { 17, 4, 29 }, state.Skip(returned).Take(3).ToArray());
    }

    [TestMethod]
    public void DeclaredUninitializedReferenceSlotsAreNullRootedWithoutPermittingSourceReads()
    {
        MethodInfo source = Dynamic("UninitializedReferenceRoot", typeof(object), [typeof(object)], il =>
        {
            il.DeclareLocal(typeof(object)); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        }, initializeLocals: false);
        WarpPortableWordLoweredProgram program = Lower(source);
        WarpPortableWordBody body = program.Bodies[0];
        foreach (WarpPortableWordSourceBlock block in body.SourceBlocks)
        {
            Assert.IsFalse(block.Instruction.EntryLocals[0].InitializedBytes.Any(initialized => initialized));
            Assert.IsTrue(block.Roots.Any(root => root.Source.Storage is "local" && root.PrivateWordOffset == body.Locals[0].WordOffset));
            Assert.IsFalse(block.Instruction.Roots.Any(root => root.Storage is "local"));
        }
        CollectionAssert.AreEqual(new uint[] { 17, 4, 29 }, Execute(program, [17, 4, 29]));
        var layout = new WarpLogicalMachineLayout(program.Kernel); CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(32, 100000); compiled.ExecuteQuantum([[17], [4], [29]], [], 0, state, 32, layout.MaximumBlockCost);
        int local = WarpLogicalMachineLayout.HeaderWords + layout.FrameWords + layout.PrivateOffset + body.Locals[0].WordOffset;
        CollectionAssert.AreEqual(new uint[] { 0, 0, 0 }, state.Skip(local).Take(3).ToArray());
        MethodInfo invalid = Dynamic("UninitializedReferenceRead", typeof(object), Array.Empty<Type>(), il =>
        {
            il.DeclareLocal(typeof(object)); il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Ret);
        }, initializeLocals: false);
        Assert.ThrowsExactly<WarpVerificationException>(() => Lower(invalid));
    }

    [TestMethod]
    public void ObjectConstructionCannotReuseTheNoOpBaseConstructorWithoutAllocation()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Kernels).GetMethod(nameof(Kernels.NewObject))!);
        int allocation = graph.Methods.First(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal))
            .Instructions.First(instruction => instruction.OpCode == OpCodes.Newobj).Offset;
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(graph, WarpPortableTypedProgram.Verify(graph)));
        Assert.AreEqual("WRPCLR2300", error.Code, StringComparer.Ordinal); Assert.AreEqual(allocation, error.IlOffset);
    }

    [TestMethod]
    public void AuxiliarySwitchBlocksKeepOriginalSourceIdentityAndReferenceRootsWithoutChargingAgain()
    {
        MethodInfo source = Dynamic("ReferenceSwitch", typeof(object), [typeof(object), typeof(int)], il =>
        {
            Label match = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Switch, Enumerable.Repeat(match, 16).ToArray());
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
            il.MarkLabel(match); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        });
        WarpPortableWordLoweredProgram program = Lower(source);
        WarpPortableWordBody body = program.Bodies[0];
        WarpPortableWordSourceBlock dispatch = body.SourceBlocks.First(block => block.Instruction.OpCode == OpCodes.Switch.Value);
        Assert.HasCount(16, dispatch.GeneratedBlocks);
        int[] aliases = body.SourceBlocks.SelectMany(block => block.GeneratedBlocks).ToArray();
        Assert.AreEqual(aliases.Length, aliases.Distinct().Count());
        CollectionAssert.AreEquivalent(program.Kernel.Functions[0].Blocks.Where(block => block.Id != 0).Select(block => block.Id).ToArray(), aliases);
        Assert.AreEqual(1, program.Kernel.Execution!.Bodies[body.Function].SourceBlockCosts[dispatch.Block]);
        foreach (int block in dispatch.GeneratedBlocks.Where(block => block != dispatch.Block))
        {
            Assert.AreEqual(0, program.Kernel.Execution.Bodies[body.Function].SourceBlockCosts[block]);
        }
        var layout = new WarpLogicalMachineLayout(program.Kernel);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(32, 100000);
        bool auxiliaryObserved = false;
        for (int attempt = 0; attempt < 1000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; attempt++)
        {
            if (state[WarpLogicalMachineLayout.DepthOffset] == 2)
            {
                int frame = WarpLogicalMachineLayout.HeaderWords + layout.FrameWords;
                int block = layout.Nodes[(int)state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]].Block;
                if (dispatch.GeneratedBlocks.Contains(block) && block != dispatch.Block)
                {
                    auxiliaryObserved = true;
                    Assert.AreSame(dispatch, body.SourceBlocks.First(item => item.GeneratedBlocks.Contains(block)));
                    foreach (WarpPortableWordRoot root in dispatch.Roots)
                    {
                        CollectionAssert.AreEqual(new uint[] { 17, 4, 29 }, state.Skip(frame + layout.PrivateOffset + root.PrivateWordOffset).Take(3).ToArray());
                    }
                }
            }
            compiled.ExecuteQuantum([[17], [4], [29], [15]], [], 0, state, 32, layout.MaximumBlockCost);
        }
        Assert.IsTrue(auxiliaryObserved); Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        CollectionAssert.AreEqual(new uint[] { 17, 4, 29 }, Enumerable.Range(0, 3).Select(word => state[layout.GetResultWordOffset(word, 32)]).ToArray());
    }

    private static MethodInfo Standard(Type owner, string name, Type[] parameters) => owner.GetMethod(name, parameters)!;
    private static WarpPortableWordLoweredProgram Lower(MethodInfo source) { WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source); return WarpPortableWordLowerer.Lower(graph, WarpPortableTypedProgram.Verify(graph)); }
    private static MethodInfo Wrapper(MethodInfo target, Type? result = null, bool keepWide = false) => Dynamic("Call_" + target.Name, keepWide ? target.ReturnType : result ?? target.ReturnType,
        target.GetParameters().Select(parameter => parameter.ParameterType).ToArray(), il =>
        {
            for (int index = 0; index < target.GetParameters().Length; index++) { il.Emit(OpCodes.Ldarg, (short)index); }
            il.Emit(OpCodes.Call, target); il.Emit(OpCodes.Ret);
        });
    private static uint Service(WarpPortableWordMathBinding binding, string entry, uint[] arguments)
    {
        MethodInfo source = binding.Implementation.GetMethod(entry, BindingFlags.Public | BindingFlags.Static)!;
        WarpControlFlowKernel kernel = new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(source, source.GetParameters().Length)).ControlFlow;
        return Run(new WarpLogicalMachineLayout(kernel), arguments)[0];
    }
    private static uint[] Execute(WarpPortableWordLoweredProgram program, uint[] arguments) => Run(new WarpLogicalMachineLayout(program.Kernel), arguments);
    private static uint[] Run(WarpLogicalMachineLayout layout, uint[] arguments)
    {
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout); uint[] state = layout.CreateInitialState(64, 10000000);
        uint[][] inputs = arguments.Length == 0 ? [[0]] : arguments.Select(word => new[] { word }).ToArray();
        for (int attempt = 0; attempt < 100000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; attempt++) { compiled.ExecuteQuantum(inputs, [], 0, state, 64, layout.MaximumBlockCost); }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return Enumerable.Range(0, layout.ResultWordCount).Select(word => state[layout.GetResultWordOffset(word, 64)]).ToArray();
    }
    private static MethodInfo Dynamic(string name, Type result, Type[] parameters, Action<ILGenerator> emit, bool initializeLocals = true)
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("MathBinding_" + name + "_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder type = assembly.DefineDynamicModule(name).DefineType(name, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, result, parameters);
        method.InitLocals = initializeLocals;
        emit(method.GetILGenerator()); return type.CreateType()!.GetMethod(name)!;
    }
    private static class Kernels
    {
        public static (object, double) Root(object reference, double left, double right) => (reference, left * right);
        public static object CallReference(object reference) => Identity(reference);
        private static object Identity(object reference) => reference;
        public static object NewObject() => new();
    }
}
