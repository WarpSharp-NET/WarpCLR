using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableSourceInitializerCases
{
    internal static void OriginalCilTriggersBindExactTypesInitializersEffectsAndEntryInvocation()
    {
        WarpPortableMethodGraph graph = Graph(nameof(Sources.Triggers));
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableSourceInitializerPlan plan = WarpPortableSourceInitializerPlan.Capture(graph, typed, schema);
        Assert.AreEqual(0, Probe.Calls); Assert.IsNull(typed.EntryInitializerTrigger);
        foreach (WarpPortableSourceInitializerTrigger row in plan.Triggers.Where(row => !row.EntryInvocation))
        {
            WarpPortableTypedInstruction instruction = typed.Methods.First(method => string.Equals(method.Identity, row.MethodIdentity, StringComparison.Ordinal))
                .Instructions.First(instruction => instruction.Offset == row.SourceOffset);
            Assert.AreEqual(unchecked((ushort)instruction.OpCode), row.SourceOpCode);
            Assert.AreEqual(WarpPortableTypedEffect.TypeInitialize, instruction.Effects[row.EffectIndex!.Value]);
            Assert.AreEqual(schema.TypeId(instruction.InitializerTrigger!.DeclaringType), row.Type);
            Assert.AreEqual(instruction.InitializerTrigger.Initializer, row.Initializer, StringComparer.Ordinal);
            Assert.IsTrue(graph.Methods.Any(method => string.Equals(method.Identity, row.Initializer, StringComparison.Ordinal)));
        }
        Assert.IsTrue(plan.Triggers.Any(row => row.Kind == WarpPortableInitializerTriggerKind.ValueInstanceMethod));
        Assert.IsTrue(plan.Triggers.Any(row => row.Kind == WarpPortableInitializerTriggerKind.InstanceConstructor));
        Assert.IsTrue(plan.Triggers.Any(row => row.Kind == WarpPortableInitializerTriggerKind.StaticField && row.BeforeFieldInit));
        Assert.IsTrue(plan.Triggers.Any(row => row.Kind == WarpPortableInitializerTriggerKind.StaticMethod));
        Assert.IsTrue(plan.Triggers.Any(row => row.ReentrantOwnInitializer));
        WarpPortableMethodGraph entry = WarpPortableMethodGraph.Discover(typeof(Strict).GetMethod(nameof(Strict.Method))!);
        WarpPortableTypedProgram entryTyped = WarpPortableTypedProgram.Verify(entry);
        Assert.IsNotNull(entryTyped.EntryInitializerTrigger);
        WarpPortableSourceInitializerPlan entryPlan = WarpPortableSourceInitializerPlan.Capture(entry, entryTyped, WarpPortableSourceHeapSchema.Create(entry, entryTyped));
        Assert.IsTrue(entryPlan.Triggers.Any(row => row.EntryInvocation && row.SourceOffset is null && row.SourceOpCode is null && row.EffectIndex is null &&
            string.Equals(row.MethodIdentity, entry.EntryIdentity, StringComparison.Ordinal)));
        Assert.AreEqual(0, Probe.Calls);
    }

    internal static void CallsWithoutCapturedInitializersAcquireNoFictitiousTypeInitializeEffect()
    {
        WarpPortableMethodGraph graph = Graph(nameof(Sources.MathWithoutInitializer));
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        Assert.IsNull(typed.EntryInitializerTrigger);
        Assert.IsFalse(typed.Methods.SelectMany(method => method.Instructions).Any(instruction => instruction.Effects.Contains(WarpPortableTypedEffect.TypeInitialize)));
        WarpPortableTypedInstruction call = typed.Methods.First(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal))
            .Instructions.First(instruction => instruction.OpCode == OpCodes.Call.Value);
        Assert.IsTrue(call.RequiredIntrinsic!.Contains("math.strict.Abs", StringComparison.Ordinal));
        Assert.IsNull(call.InitializerTrigger);
    }

    internal static void BeforeFieldInitMethodAndOrdinaryClassInstanceCallsDoNotInventTriggers()
    {
        WarpPortableMethodGraph graph = Graph(nameof(Sources.NonTriggers));
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableTypedMethod entry = typed.Methods.First(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal));
        Assert.IsTrue(graph.Types.First(type => type.SourceType == typeof(Relaxed)).SourceType.Attributes.HasFlag(TypeAttributes.BeforeFieldInit));
        Assert.IsFalse(entry.Instructions.Any(instruction => instruction.InitializerTrigger is not null));
        Assert.IsTrue(graph.Types.Any(type => type.SourceType == typeof(Strict) && type.Initializer is not null));
        Assert.IsFalse(typed.Methods.SelectMany(method => method.Instructions).Where(instruction => instruction.OpCode == OpCodes.Ldftn.Value || instruction.OpCode == OpCodes.Ldvirtftn.Value)
            .Any(instruction => instruction.InitializerTrigger is not null));
        Assert.AreEqual(0, Probe.Calls);
    }

    internal static void ActualCoreClrStrictValueAndInterfaceInvocationTimingMatchesCapturedTriggers()
    {
        WarpPortableMethodGraph graph = Graph(nameof(Sources.ValueAndInterface));
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceInitializerPlan plan = WarpPortableSourceInitializerPlan.Capture(graph, typed, WarpPortableSourceHeapSchema.Create(graph, typed));
        Assert.IsTrue(plan.Triggers.Any(row => row.Kind == WarpPortableInitializerTriggerKind.ValueInstanceMethod));
        Assert.IsTrue(plan.Triggers.Any(row => row.Kind == WarpPortableInitializerTriggerKind.InterfaceInstanceMethod));
        Assert.AreEqual(0, ReferenceProbe.ValueCalls); Assert.AreEqual(0, ReferenceProbe.InterfaceCalls);
        // These direct calls are independent CLR oracles. Discovery above only read metadata/CIL.
        Assert.AreEqual(11, Sources.ValueAndInterface());
        Assert.AreEqual(1, ReferenceProbe.ValueCalls); Assert.AreEqual(1, ReferenceProbe.InterfaceCalls);
        Assert.AreEqual(11, Sources.ValueAndInterface());
        Assert.AreEqual(1, ReferenceProbe.ValueCalls); Assert.AreEqual(1, ReferenceProbe.InterfaceCalls);
    }

    internal static void ActualCoreClrInitializerFailureCachesWrapperAndOriginalInnerIdentity()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Failed).GetMethod(nameof(Failed.Method))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceInitializerPlan plan = WarpPortableSourceInitializerPlan.Capture(graph, typed, WarpPortableSourceHeapSchema.Create(graph, typed));
        Assert.IsNotNull(typed.EntryInitializerTrigger); Assert.AreEqual(0, ReferenceProbe.FailureCalls);
        TypeInitializationException first = Assert.ThrowsExactly<TypeInitializationException>(Failed.Method);
        TypeInitializationException second = Assert.ThrowsExactly<TypeInitializationException>(Failed.Method);
        Assert.AreSame(first, second); Assert.AreSame(ReferenceProbe.Failure, first.InnerException);
        Assert.AreEqual(1, ReferenceProbe.FailureCalls);
        WarpPortableSourceInitializerType type = plan.Types.First(type => type.Type == plan.Triggers.First(row => row.EntryInvocation).Type);
        CollectionAssert.AreEqual(type.ExceptionTypeName.ToArray(), first.TypeName!.Select(character => (ushort)character).ToArray());
        Assert.AreEqual("Failed", first.TypeName, StringComparer.Ordinal);
        Assert.AreEqual(unchecked((uint)first.HResult), type.DefaultHResult);
        Assert.AreEqual(WarpPortableSourceExceptionKind.TypeInitialization,
            WarpPortableSourceHeapSchema.Create(graph, typed).ExceptionTypes[(int)type.WrapperType - 1].Kind);
        first.HResult = unchecked((int)0xA1234567);
        TypeInitializationException afterMutation = Assert.ThrowsExactly<TypeInitializationException>(Failed.Method);
        Assert.AreSame(first, afterMutation); Assert.AreSame(ReferenceProbe.Failure, afterMutation.InnerException);
        Assert.AreEqual(unchecked((int)0xA1234567), afterMutation.HResult);
        Assert.AreEqual(1, ReferenceProbe.FailureCalls);
    }

    internal static void UnboundSourceInitializerStillFailsClosedAtTheRealCompilerBoundary()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Strict).GetMethod(nameof(Strict.Method))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpVerificationException failure = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(graph, typed));
        Assert.AreEqual("WRPCLR2300", failure.Code, StringComparer.Ordinal);
        Assert.IsTrue(failure.Message.Contains("initializer", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(0, Probe.Calls);
    }

    internal static void GeneratedBeforeFieldInitMethodExecutesWithoutInventingACctorTrigger()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Relaxed).GetMethod(nameof(Relaxed.Method))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        Assert.IsNull(typed.EntryInitializerTrigger);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed);
        WarpLogicalMachineLayout layout = new(program.Kernel);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(16, 1000); int calls = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && calls++ < 100)
        {
            compiled.ExecuteQuantum([[0]], [], 0, state, 16, layout.MaximumBlockCost);
        }
        Assert.IsLessThan(100, calls);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(5u, state[layout.GetResultWordOffset(0, 16)]);
        Assert.AreEqual(0, Probe.Calls);
        Assert.IsFalse(program.Bodies.Any(body => string.Equals(body.MethodIdentity,
            graph.Types.First(type => type.SourceType == typeof(Relaxed)).Initializer, StringComparison.Ordinal)));
    }

    private static WarpPortableMethodGraph Graph(string name) => WarpPortableMethodGraph.Discover(typeof(Sources).GetMethod(name)!);
    private static class Probe { public static int Calls; }
    private static class ReferenceProbe
    {
        public static int ValueCalls;
        public static int InterfaceCalls;
        public static int FailureCalls;
        public static readonly Exception Failure = new InvalidOperationException("original-cctor-inner");
    }
    private sealed class Strict
    {
        public static int Data;
        public int Value;
        static Strict() { Initialize(); }
        private static void Initialize() { Probe.Calls++; Data = 7; }
        public Strict(int value) { Value = value; }
        public static int Method() => Data;
        public int Instance() => Value;
    }
    private static class Relaxed
    {
        public static int Data = Strict.Method();
        public static int Method() => 5;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TriggerValue
    {
        public int Value;
        static TriggerValue() { Probe.Calls++; }
        public readonly int Read() => Value;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ReferenceValue
    {
        public int Zero;
        static ReferenceValue() { ReferenceProbe.ValueCalls++; }
        public readonly int Read() => 4 + Zero;
    }
    private interface IReference
    {
        static IReference() { ReferenceProbe.InterfaceCalls++; }
        int Read() => 7;
    }
    private sealed class ReferenceImplementation : IReference;
    private static class Failed
    {
        static Failed() { ReferenceProbe.FailureCalls++; RaiseOriginal(); }
        private static void RaiseOriginal() => throw ReferenceProbe.Failure;
        public static void Method() { }
    }
    private static class Sources
    {
        public static int Triggers(TriggerValue value) => Strict.Method() + new Strict(2).Value + value.Read() + Relaxed.Data;
        public static int NonTriggers(Strict value) => value.Instance() + Relaxed.Method();
        public static float MathWithoutInitializer(float value) => MathF.Abs(value);
        public static int ValueAndInterface()
        {
            ReferenceValue value = default; return value.Read() + ((IReference)new ReferenceImplementation()).Read();
        }
    }
}
