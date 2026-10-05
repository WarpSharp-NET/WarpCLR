using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableSourceExceptionDataCases
{
    internal static void CapturedExceptionDataBindsDenseKindsHResultsAndAppendOnlyRoots()
    {
        WarpPortableSourceHeapSchema schema = Schema();
        Assert.HasCount(schema.Types.Length, schema.ExceptionTypes);
        foreach (Type type in BuiltinOracles())
        {
            // Standard CLR exception constructors are an oracle only, never a source-capture path.
            Exception reference = type == typeof(TypeInitializationException) ? new TypeInitializationException("Captured.SourceType", null) :
                (Exception)Activator.CreateInstance(type)!;
            uint id = Id(schema, reference.GetType());
            WarpPortableSourceExceptionType row = schema.ExceptionTypes[(int)id - 1];
            Assert.AreEqual(id, row.Type); Assert.AreEqual(unchecked((uint)reference.HResult), row.DefaultHResult);
            Assert.AreNotEqual(0u, row.Kind);
        }
        WarpPortableSourceHeapType source = schema.Types.First(type => type.Id == Id(schema, typeof(SourceError)));
        foreach (int offset in new[] { 0, 3, 6, 12, 15, 18 }) { Assert.IsTrue(source.References.Any(root => root.Offset == offset)); }
        foreach (WarpPortableSourceHeapField field in schema.Fields.Where(field => field.DeclaringType == source.Id && !field.IsStatic))
        {
            Assert.AreEqual(field.SourceByteOffset + 88, field.HeapByteOffset);
        }
        uint[] arena = Arena(schema); uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
        uint start = arena[descriptor + WarpPortableSourceMemoryLayout.ExceptionTypeStart];
        Assert.IsLessThan(arena[descriptor + WarpPortableSourceMemoryLayout.FrameStart], start);
        Assert.AreEqual(0u, arena[start + (Id(schema, typeof(object)) - 1) * 2]);
        Assert.AreEqual(0u, Probe.Calls);
    }

    internal static void GeneratedExceptionDataPreservesRawFieldsAndOriginalInnerIdentity()
    {
        Fixture fixture = Create(typeof(ArgumentOutOfRangeException)); Acquire(fixture.Arena);
        Scratch(fixture.Arena, [.. fixture.Message, .. fixture.Inner, .. fixture.ParamName, .. fixture.Actual, 0, 0, 0]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), fixture.Arena, [.. fixture.Owner, 0]));
        foreach ((uint field, uint[] expected) in new (uint, uint[])[]
        {
            (0, fixture.Message), (3, fixture.Inner), (12, fixture.ParamName), (15, fixture.Actual),
        })
        {
            Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReadSourceExceptionDataField), fixture.Arena, [.. fixture.Owner, field]));
            CollectionAssert.AreEqual(expected, ResultReference(fixture.Arena)); Acknowledge(fixture.Arena);
        }
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReadSourceExceptionDataField), fixture.Arena, [.. fixture.Owner, 21]));
        Assert.AreEqual(unchecked((uint)Activator.CreateInstance<ArgumentOutOfRangeException>().HResult), fixture.Arena[WarpPortableHeapLayout.Result]);
        Release(fixture.Arena);
    }

    internal static void GeneratedExceptionInitializationValidatesEveryReferenceBeforeWriting()
    {
        Fixture fixture = Create(typeof(ArgumentOutOfRangeException)); uint[] arena = fixture.Arena; Acquire(arena);
        uint payload = Payload(arena, fixture.Owner); uint[] before = arena.Skip((int)payload).Take(22).ToArray();
        uint[][] invalid = [
            [.. fixture.Actual, .. fixture.Inner, .. fixture.ParamName, .. fixture.Actual, 0, 0, 0],
            [.. fixture.Message, fixture.Inner[0], fixture.Inner[1], fixture.Inner[2] + 1, .. fixture.ParamName, .. fixture.Actual, 0, 0, 0],
            [.. fixture.Message, .. fixture.Inner, .. fixture.ParamName, .. fixture.Actual, .. fixture.TypeName]];
        foreach (uint[] input in invalid)
        {
            Scratch(arena, input);
            Assert.AreNotEqual(0u, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [.. fixture.Owner, 0]));
            CollectionAssert.AreEqual(before, arena.Skip((int)payload).Take(22).ToArray());
        }
        Scratch(arena, [.. fixture.Message, .. fixture.Inner, .. fixture.ParamName, .. fixture.Actual, 0, 0, 0]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [.. fixture.Owner, 0]));
        Release(arena);
    }

    internal static void InitializedFaultGenerationsCannotBeReusedByClearingHResult()
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema); uint[] owner = Allocate(schema, arena, typeof(Exception)); Acquire(arena);
        Scratch(arena, new uint[15]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [.. owner, 0]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.WriteSourceExceptionHResult), arena, [.. owner, 0]));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [.. owner, 0]));
        Release(arena);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.RequestCollection), arena, []));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.Collect), arena, []));
        uint[] next = Allocate(schema, arena, typeof(Exception)); Assert.AreEqual(owner[1], next[1]); Assert.AreNotEqual(owner[2], next[2]);
        Acquire(arena); Scratch(arena, new uint[15]);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [.. owner, 0]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [.. next, 0])); Release(arena);
    }

    internal static void ExceptionDataRequiresExactOwnerLeaseAndCapturedDescriptor()
    {
        Fixture fixture = Create(typeof(Exception)); uint[] arena = fixture.Arena; Scratch(arena, new uint[15]);
        Assert.AreEqual(WarpPortableHeapLayout.Busy, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [.. fixture.Owner, 0]));
        Acquire(arena);
        Assert.AreEqual(WarpPortableHeapLayout.NullReference, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [0, 0, 0, 0]));
        Assert.AreEqual(WarpPortableHeapLayout.WrongContext, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [fixture.Owner[0] + 1, fixture.Owner[1], fixture.Owner[2], 0]));
        Assert.AreEqual(WarpPortableHeapLayout.Bounds, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [.. fixture.Owner, uint.MaxValue]));
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, Service(nameof(WarpPortableHeapServices.ReadSourceExceptionDataField), arena, [.. fixture.Owner, 0]));
        uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
        arena[descriptor + WarpPortableSourceMemoryLayout.ExceptionTypeStart] = uint.MaxValue;
        Assert.AreEqual(WarpPortableHeapLayout.InvalidType, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [.. fixture.Owner, 0])); Release(arena);
    }

    internal static void ExceptionParameterActualAndInnerRootsSurvivePreciseCollection()
    {
        Fixture fixture = Create(typeof(ArgumentOutOfRangeException)); uint[] arena = fixture.Arena; Acquire(arena);
        Scratch(arena, [.. fixture.Message, .. fixture.Inner, .. fixture.ParamName, .. fixture.Actual, 0, 0, 0]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [.. fixture.Owner, 0]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireRoot), arena, [.. fixture.Owner, WarpPortableHeapLayout.StrongRoot, 0, 0, 0]));
        uint root = arena[WarpPortableHeapLayout.Result]; uint rootGeneration = arena[WarpPortableHeapLayout.Result + 1]; Release(arena);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.RequestCollection), arena, []));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.Collect), arena, []));
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.Result]); // Only the unused TypeName string is unreachable.
        foreach (uint[] reference in new[] { fixture.Owner, fixture.Message, fixture.Inner, fixture.ParamName, fixture.Actual })
        {
            Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.GetType), arena, reference));
        }
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseRoot), arena, [root, rootGeneration]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.RequestCollection), arena, []));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.Collect), arena, []));
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]);
    }

    internal static void RawExceptionMessageDataNeverPretendsToImplementVirtualFormatting()
    {
        Fixture fixture = Create(typeof(TypeInitializationException)); uint[] arena = fixture.Arena; Acquire(arena);
        Scratch(arena, [.. fixture.Message, .. fixture.Inner, 0, 0, 0, 0, 0, 0, .. fixture.TypeName]);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), arena, [.. fixture.Owner, 0]));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReadSourceExceptionDataField), arena, [.. fixture.Owner, 18]));
        CollectionAssert.AreEqual(fixture.TypeName, ResultReference(arena)); Acknowledge(arena);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, Service(nameof(WarpPortableHeapServices.ReadSourceExceptionDataField), arena, [.. fixture.Owner, 12])); Release(arena);
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Source).GetMethod(nameof(Source.Message))!);
        WarpVerificationException unbound = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(graph, WarpPortableTypedProgram.Verify(graph)));
        Assert.AreEqual("WRPCLR2300", unbound.Code, StringComparer.Ordinal);
        Assert.AreNotEqual("raw", ReferenceArgumentMessage("lengths"), StringComparer.Ordinal);
    }

    private static Fixture Create(Type exception)
    {
        WarpPortableSourceHeapSchema schema = Schema(); uint[] arena = Arena(schema);
        uint[] message = Text(schema, arena, "raw"), inner = Allocate(schema, arena, typeof(Exception));
        uint[] param = Text(schema, arena, "lengths"), actual = Allocate(schema, arena, typeof(object));
        uint[] typeName = Text(schema, arena, "Captured.SourceType"), owner = Allocate(schema, arena, exception);
        return new(schema, arena, owner, message, inner, param, actual, typeName);
    }

    private static WarpPortableSourceHeapSchema Schema()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Source).GetMethod(nameof(Source.Identity))!, concreteTypes: [typeof(SourceError)]);
        return WarpPortableSourceHeapSchema.Create(graph, WarpPortableTypedProgram.Verify(graph));
    }
    private static uint[] Arena(WarpPortableSourceHeapSchema schema) => schema.CreateArena(293, 4096, 32, 32, 2, 4096);
    private static uint Id(WarpPortableSourceHeapSchema schema, Type type) => schema.TypeId(WarpPortableMethodGraphIdentity.Type(type));
    private static uint Payload(uint[] arena, uint[] owner) => arena[arena[WarpPortableHeapLayout.SlotStart] + (owner[1] - 1) * WarpPortableHeapLayout.SlotWords + WarpPortableHeapLayout.SlotPayload];
    private static void Scratch(uint[] arena, uint[] values) => values.CopyTo(arena, (int)arena[WarpPortableHeapLayout.ScratchStart]);
    private static uint[] ResultReference(uint[] arena) => arena.Skip((int)WarpPortableHeapLayout.Result).Take(3).ToArray();
    private static uint[] Allocate(WarpPortableSourceHeapSchema schema, uint[] arena, Type type)
    {
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AllocateObject), arena, [Id(schema, type)])); return ResultReference(arena);
    }
    private static uint[] Text(WarpPortableSourceHeapSchema schema, uint[] arena, string text)
    {
        Scratch(arena, text.Select(character => (uint)character).ToArray());
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.CreateString), arena, [Id(schema, typeof(string)), 0, (uint)text.Length])); return ResultReference(arena);
    }
    private static void Acquire(uint[] arena) => Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
    private static void Acknowledge(uint[] arena) => Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), arena, [uint.MaxValue]));
    private static void Release(uint[] arena) => Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    private static uint Service(string name, uint[] arena, uint[] arguments)
    {
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableHeapServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout); uint[] state = layout.CreateInitialState(64, 10000000);
        uint[][] inputs = arguments.Length == 0 ? [[0]] : arguments.Select(value => new[] { value }).ToArray(); int quanta = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && quanta++ < 100000)
        {
            compiled.ExecuteManagedQuantum(inputs, [], 0, state, 64, layout.MaximumBlockCost, arena);
        }
        Assert.IsLessThan(100000, quanta); Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return state[layout.GetResultWordOffset(0, 64)];
    }
    private static string ReferenceArgumentMessage(string paramName) => new ArgumentException("raw", paramName).Message;
    private static Type[] BuiltinOracles() => [typeof(Exception), typeof(SystemException), typeof(NullReferenceException),
        typeof(IndexOutOfRangeException), typeof(OverflowException), typeof(DivideByZeroException), typeof(ArithmeticException),
        typeof(ArrayTypeMismatchException), typeof(InvalidCastException), typeof(InvalidOperationException), typeof(OutOfMemoryException),
        typeof(StackOverflowException), typeof(ArgumentException), typeof(ArgumentOutOfRangeException), typeof(ArgumentNullException),
        typeof(TypeInitializationException)];
    private sealed record Fixture(WarpPortableSourceHeapSchema Schema, uint[] Arena, uint[] Owner, uint[] Message,
        uint[] Inner, uint[] ParamName, uint[] Actual, uint[] TypeName);
    private static class Probe { public static uint Calls; }
    private sealed class SourceError : Exception
    {
        public object? Related;
        public SourceError() { Probe.Calls++; Related = null; }
        public SourceError(string message) : base(message) { Probe.Calls++; Related = null; }
        public SourceError(string message, Exception innerException) : base(message, innerException) { Probe.Calls++; Related = null; }
    }
    private static class Source
    {
        public static int Identity(int value) => value;
        public static string Message(Exception value) => value.Message;
        public static SourceError ConstructedOnlyWhenSourceExecutes() => new();
    }
}
