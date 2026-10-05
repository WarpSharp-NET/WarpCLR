using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableSourceInitializerExecutionCases
{
    internal static void CapturedOriginalCctorRunsInsideRootFrameAndMemoizesPerContext()
    {
        Fixture fixture = Capture(typeof(Strict), nameof(Strict.Read));
        Assert.AreEqual(0u, Probe.Calls);
        uint[] arena = fixture.Schema.CreateArena(1001, 4096, 16, 16, 1, 4096);
        Assert.AreEqual(0x12345679u, Execute(fixture, arena));
        Assert.AreEqual(0u, Probe.Calls);
        Assert.AreEqual(2u, TypeWord(fixture, arena, typeof(Strict), WarpPortableHeapLayout.InitializerState));
        Assert.AreEqual(0x12345679u, Execute(fixture, arena));
        uint[] second = fixture.Schema.CreateArena(1002, 4096, 16, 16, 1, 4096);
        Assert.AreEqual(0x12345679u, Execute(fixture, second));
        Assert.AreEqual(0u, Probe.Calls);
        WarpPortableWordBody root = fixture.Program.Bodies.First(body => string.Equals(body.MethodIdentity, fixture.Graph.EntryIdentity, StringComparison.Ordinal));
        Assert.IsNotNull(root.InvocationPrelude);
        Assert.HasCount(6, root.InvocationPrelude.GeneratedBlocks);
        Assert.IsTrue(root.InvocationPrelude.GeneratedBlocks.All(block => fixture.Program.Kernel.Execution!.Bodies[root.Function].SourceBlockCosts[block] == 0));
        Assert.IsTrue(fixture.Program.Bodies.Any(body => fixture.Graph.Methods.First(method => string.Equals(method.Identity, body.MethodIdentity, StringComparison.Ordinal)).SourceMethod is ConstructorInfo { IsStatic: true }));
        Assert.IsNotNull(WarpPortableWordProgramIdentity.Validate(fixture.Graph, fixture.Schema, fixture.Program));
    }

    internal static void ActualCompiledCctorCycleSeesReentrantZeroThenCompletesExactStatics()
    {
        Fixture fixture = Capture(typeof(CycleA), nameof(CycleA.Read));
        uint[] arena = fixture.Schema.CreateArena(1003, 4096, 16, 16, 1, 4096);
        Assert.AreEqual(3u, Execute(fixture, arena));
        Assert.AreEqual(2u, TypeWord(fixture, arena, typeof(CycleA), WarpPortableHeapLayout.InitializerState));
        Assert.AreEqual(2u, TypeWord(fixture, arena, typeof(CycleB), WarpPortableHeapLayout.InitializerState));
        // Separate explicit CoreCLR reference oracle; capture/generated execution above did not invoke source.
        Assert.AreEqual(3u, CycleA.Read());
    }

    internal static void OriginalBeforeFieldInitCilTriggersAtFieldRatherThanConstantMethod()
    {
        Fixture fixture = Capture(typeof(Entries), nameof(Entries.Relaxed));
        Assert.IsNull(fixture.Typed.EntryInitializerTrigger);
        Assert.AreEqual(0u, Probe.RelaxedCalls);
        uint[] arena = fixture.Schema.CreateArena(1004, 4096, 16, 16, 1, 4096);
        Assert.AreEqual(22u, Execute(fixture, arena));
        Assert.AreEqual(0u, Probe.RelaxedCalls);
        Assert.AreEqual(2u, TypeWord(fixture, arena, typeof(Relaxed), WarpPortableHeapLayout.InitializerState));
        Assert.IsFalse(fixture.Program.Bodies.Any(body => body.InvocationPrelude is not null));
    }

    internal static void AbandonedCctorCannotReenterAfterResetOrPublishDefaultSourceOutput()
    {
        Fixture fixture = Capture(typeof(Abandoned), nameof(Abandoned.Read));
        uint[] arena = fixture.Schema.CreateArena(1005, 4096, 16, 16, 1, 4096);
        uint[] exhausted = State(fixture, arena, budget: 1);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, exhausted[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(1u, TypeWord(fixture, arena, typeof(Abandoned), WarpPortableHeapLayout.InitializerState));
        uint original = TypeWord(fixture, arena, typeof(Abandoned), WarpPortableHeapServices.SourceInitializerContext);
        uint[] reset = State(fixture, arena);
        Assert.AreNotEqual(original, reset[WarpLogicalMachineLayout.OwnerContextOffset]);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, reset[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(3u, reset[WarpLogicalMachineLayout.FaultKindOffset]);
        Assert.AreEqual(0xDEADBEEFu, reset[fixture.Layout.GetResultWordOffset(0, Depth)]);
        Assert.AreEqual(1u, TypeWord(fixture, arena, typeof(Abandoned), WarpPortableHeapLayout.InitializerState));
    }

    internal static void OriginalCctorCannotRunWithHeldHeapControllerOrAcquireFactoryAuthority()
    {
        Fixture fixture = Capture(typeof(Guarded), nameof(Guarded.Read));
        uint[] arena = fixture.Schema.CreateArena(1006, 4096, 16, 16, 1, 4096);
        arena[WarpPortableHeapLayout.LeaseState] = 1; arena[WarpPortableHeapLayout.LeaseOwner] = 1;
        uint[] denied = State(fixture, arena);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, denied[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(0u, TypeWord(fixture, arena, typeof(Guarded), WarpPortableHeapLayout.InitializerState));
        Assert.AreEqual(0xDEADBEEFu, denied[fixture.Layout.GetResultWordOffset(0, Depth)]);
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Failed).GetMethod(nameof(Failed.Read))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpVerificationException rejected = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableClosedInitializerSourcePlan.Capture(graph, typed, WarpPortableSourceHeapSchema.Create(graph, typed)));
        Assert.AreEqual("WRPCLR2480", rejected.Code, StringComparer.Ordinal);
    }

    internal static void ActualCompiledInitializerStaticsPreservePackedSignedWideAndRawFloatingBits()
    {
        Assert.AreEqual(0u, Probe.PackedCalls);
        foreach ((string method, ulong expected, int words) in new[]
        {
            (nameof(Packed.SignedRead), 65165UL, 1),
            (nameof(Packed.WideRead), 0xFEDCBA9876543210UL, 2),
            (nameof(Packed.SingleBits), 0xFFC12345UL, 1),
            (nameof(Packed.DoubleBits), 0xFFF8123456789ABCUL, 2),
        })
        {
            Fixture fixture = Capture(typeof(Packed), method);
            uint[] arena = fixture.Schema.CreateArena(1007, 4096, 16, 16, 1, 4096);
            uint[] state = State(fixture, arena);
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(words, fixture.Layout.ResultWordCount);
            ulong value = state[fixture.Layout.GetResultWordOffset(0, Depth)];
            if (words == 2) { value |= (ulong)state[fixture.Layout.GetResultWordOffset(1, Depth)] << 32; }
            Assert.AreEqual(expected, value);
            Assert.AreEqual(0u, Probe.PackedCalls);
        }
        // Independent original CoreCLR oracles execute only after every compiled case.
        Assert.AreEqual(65165, Packed.SignedRead());
        Assert.AreEqual(0xFEDCBA9876543210UL, Packed.WideRead());
        Assert.AreEqual(unchecked((int)0xFFC12345), Packed.SingleBits());
        Assert.AreEqual(unchecked((long)0xFFF8123456789ABC), Packed.DoubleBits());
        Assert.AreEqual(1u, Probe.PackedCalls);
    }

    private const int Depth = 64;
    private sealed record Fixture(WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed,
        WarpPortableSourceHeapSchema Schema, WarpPortableWordLoweredProgram Program, WarpLogicalMachineLayout Layout);
    private static Fixture Capture(Type type, string name)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(type.GetMethod(name)!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableClosedInitializerSourcePlan plan = WarpPortableClosedInitializerSourcePlan.Capture(graph, typed, schema);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed, schema, new WarpPortableClosedInitializerSourceBinding(plan));
        return new(graph, typed, schema, program, new(program.Kernel));
    }
    private static uint Execute(Fixture fixture, uint[] arena)
    {
        uint[] state = State(fixture, arena);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(1, fixture.Layout.ResultWordCount);
        return state[fixture.Layout.GetResultWordOffset(0, Depth)];
    }
    private static uint[] State(Fixture fixture, uint[] arena, long budget = 1000000)
    {
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(fixture.Layout);
        uint[] state = fixture.Layout.CreateInitialState(Depth, budget);
        for (int word = 0; word < fixture.Layout.ResultWordCount; word++) { state[fixture.Layout.GetResultWordOffset(word, Depth)] = 0xDEADBEEF; }
        int attempts = 0; uint expectedLease = arena[WarpPortableHeapLayout.LeaseState];
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && attempts++ < 100000)
        {
            core.ExecuteManagedQuantum([[0]], [], 0, state, Depth, fixture.Layout.MaximumBlockCost, arena);
            Assert.AreEqual(expectedLease, arena[WarpPortableHeapLayout.LeaseState]);
        }
        Assert.IsLessThan(100000, attempts); return state;
    }
    private static uint TypeWord(Fixture fixture, uint[] arena, Type type, uint offset) =>
        arena[arena[WarpPortableHeapLayout.TypeStart] + (fixture.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(type)) - 1) * WarpPortableHeapLayout.TypeWords + offset];
    private static class Probe { public static uint Calls; public static uint RelaxedCalls; public static uint CycleCalls; public static uint AbandonedCalls; public static uint GuardedCalls; public static uint PackedCalls; }
    private static class Strict
    {
        private static readonly uint Value = InitialValue();
        private static uint InitialValue() => 0x12345678;
        static Strict() { Initialize(); }
        private static void Initialize() { Probe.Calls++; }
        public static uint Read() => Value + Probe.Calls;
    }
    private static class CycleA
    {
        private static readonly uint Value = CycleB.Read() + 1;
        static CycleA() { Initialize(); }
        private static void Initialize() { Probe.CycleCalls++; }
        public static uint Read() => Value;
        public static uint Peek() => Value;
    }
    private static class CycleB
    {
        private static readonly uint Value = CycleA.Peek() + 2;
        static CycleB() { Initialize(); }
        private static void Initialize() { Probe.CycleCalls++; }
        public static uint Read() => Value;
    }
    private static class Relaxed
    {
        private static readonly uint Value = Initialize();
        private static uint Initialize() { Probe.RelaxedCalls++; return 17; }
        public static uint Constant() => 5;
        public static uint Read() => Value;
    }
    private static class Entries { public static uint Relaxed() => WarpPortableSourceInitializerExecutionCases.Relaxed.Constant() + WarpPortableSourceInitializerExecutionCases.Relaxed.Read(); }
    private static class Abandoned
    {
        private static readonly uint Value = InitialValue();
        private static uint InitialValue() => 37;
        static Abandoned() { Initialize(); }
        private static void Initialize() { Probe.AbandonedCalls++; }
        public static uint Read() => Value;
    }
    private static class Guarded
    {
        private static readonly uint Value = InitialValue();
        private static uint InitialValue() => 41;
        static Guarded() { Initialize(); }
        private static void Initialize() { Probe.GuardedCalls++; }
        public static uint Read() => Value;
    }
    private static class Failed
    {
        static Failed() { RaiseOriginal(); }
        private static void RaiseOriginal() => throw new InvalidOperationException("Unbound failure wrapper/factory oracle.");
        public static uint Read() => 0;
    }
    private static class Packed
    {
        private static readonly sbyte Signed = SignedValue();
        private static readonly ushort Unsigned = UnsignedValue();
        private static readonly ulong Wide = WideValue();
        private static readonly float Single = SingleValue();
        private static readonly double Double = DoubleValue();
        static Packed() { Initialize(); }
        private static void Initialize() { Probe.PackedCalls++; }
        private static sbyte SignedValue() => -79;
        private static ushort UnsignedValue() => 0xFEDC;
        private static ulong WideValue() => 0xFEDCBA9876543210;
        private static float SingleValue() => BitConverter.Int32BitsToSingle(unchecked((int)0xFFC12345));
        private static double DoubleValue() => BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8123456789ABC));
        public static int SignedRead() => Signed + Unsigned;
        public static ulong WideRead() => Wide;
        public static int SingleBits() => BitConverter.SingleToInt32Bits(Single);
        public static long DoubleBits() => BitConverter.DoubleToInt64Bits(Double);
    }
}
