using System.Reflection;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableSourceExceptionAccessorCases
{
    internal static void ExactAccessorCatalogPreservesReturnTypesAndOriginalVirtualTargets()
    {
        foreach ((Type type, string property, Type result) in new (Type, string, Type)[]
        {
            (typeof(Exception), nameof(Exception.HResult), typeof(int)), (typeof(Exception), nameof(Exception.InnerException), typeof(Exception)),
            (typeof(ArgumentException), nameof(ArgumentException.ParamName), typeof(string)),
            (typeof(ArgumentOutOfRangeException), nameof(ArgumentOutOfRangeException.ActualValue), typeof(object)),
            (typeof(TypeInitializationException), nameof(TypeInitializationException.TypeName), typeof(string)),
        })
        {
            MethodInfo method = type.GetProperty(property)!.GetMethod!;
            string identity = WarpPortableMethodGraphIntrinsics.Resolve(method)!;
            Assert.IsTrue(identity.Contains("exception.data.v1.", StringComparison.Ordinal));
            Assert.AreEqual(result, method.ReturnType);
        }
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Sources).GetMethod(nameof(Sources.Param))!, concreteTypes: [typeof(OverriddenArgument)]);
        MethodInfo getter = typeof(OverriddenArgument).GetProperty(nameof(ArgumentException.ParamName))!.GetMethod!;
        string target = WarpPortableMethodGraphIdentity.Method(getter);
        Assert.IsTrue(graph.Dispatches.Any(dispatch => string.Equals(dispatch.Target, target, StringComparison.Ordinal)));
        Assert.IsNull(graph.Methods.First(method => string.Equals(method.Identity, target, StringComparison.Ordinal)).Intrinsic);
        WarpVerificationException rejected = Assert.ThrowsExactly<WarpVerificationException>(() => Capture(graph));
        Assert.AreEqual("WRPCLR2470", rejected.Code, StringComparer.Ordinal);
        Assert.AreEqual("source-override", ReferenceOverride("count"), StringComparer.Ordinal);
        MethodInfo baseMethod = typeof(Exception).GetMethod(nameof(Exception.GetBaseException), Type.EmptyTypes)!;
        MethodInfo inheritedMethod = typeof(InvalidOperationException).GetMethod(nameof(Exception.GetBaseException), Type.EmptyTypes)!;
        Assert.AreEqual(typeof(Exception), inheritedMethod.DeclaringType);
        Assert.AreEqual(typeof(InvalidOperationException), inheritedMethod.ReflectedType);
        Assert.AreNotEqual(baseMethod, inheritedMethod);
        Assert.AreEqual(WarpPortableMethodGraphIntrinsics.Resolve(baseMethod), WarpPortableMethodGraphIntrinsics.Resolve(inheritedMethod), StringComparer.Ordinal);
        MethodInfo inheritedMessage = typeof(InvalidOperationException).GetProperty(nameof(Exception.Message))!.GetMethod!;
        Assert.AreEqual(WarpPortableExceptionAccessorKind.Message, WarpPortableMethodGraphIntrinsics.ExceptionAccessor(inheritedMessage));
    }

    internal static void GeneratedOriginalCilGettersReadExactDataAndRetainReferenceIdentity()
    {
        foreach ((string name, Type type, uint field) in new (string, Type, uint)[]
        {
            (nameof(Sources.HResult), typeof(ArgumentOutOfRangeException), 21),
            (nameof(Sources.Inner), typeof(ArgumentOutOfRangeException), 3),
            (nameof(Sources.Param), typeof(ArgumentOutOfRangeException), 12),
            (nameof(Sources.Actual), typeof(ArgumentOutOfRangeException), 15),
            (nameof(Sources.TypeName), typeof(TypeInitializationException), 18),
        })
        {
            Fixture fixture = Capture(name); Seed seed = Create(fixture, type);
            uint[] expected = field == 21 ? [unchecked((uint)Activator.CreateInstance<ArgumentOutOfRangeException>().HResult)] :
                field == 3 ? seed.Inner : field == 12 ? seed.Param : field == 15 ? seed.Actual : seed.TypeName;
            CollectionAssert.AreEqual(expected, Execute(fixture, seed.Arena, seed.Owner, fixture.Layout.MaximumBlockCost));
            CollectionAssert.AreEqual(expected, Execute(fixture, seed.Arena, seed.Owner, 4096));
            Assert.AreEqual(0u, seed.Arena[WarpPortableHeapLayout.PendingResult]);
            Assert.AreEqual(1u, seed.Arena[WarpPortableHeapLayout.LeaseState]);
            Assert.IsTrue(fixture.Program.RequiredServices.Any(identity => identity.Contains(WarpPortableHeapServices.ExceptionAccessorSemantics, StringComparison.Ordinal)));
            Assert.IsTrue(fixture.Program.Bodies.Any(body => body.SourceBlocks.Any(block => !block.GeneratedBlocks.IsEmpty)));
            Assert.IsNotNull(WarpPortableWordProgramIdentity.Validate(fixture.Graph, fixture.Schema, fixture.Program));
            Release(seed.Arena);
        }
    }

    internal static void GeneratedBaseExceptionFollowsInitializedInnerOwnersWithoutHostInvocation()
    {
        Fixture fixture = Capture(nameof(Sources.Base)); Seed seed = Create(fixture, typeof(Exception));
        CollectionAssert.AreEqual(seed.Inner, Execute(fixture, seed.Arena, seed.Owner, 1));
        CollectionAssert.AreEqual(seed.Inner, Execute(fixture, seed.Arena, seed.Inner, 1));
        Exception leaf = new InvalidOperationException("leaf"), middle = new InvalidOperationException("middle", leaf), reference = new InvalidOperationException("outer", middle);
        Assert.AreSame(leaf, Sources.Base(reference));
        Assert.AreEqual(0u, seed.Arena[WarpPortableHeapLayout.PendingResult]);
        Release(seed.Arena);
    }

    internal static void GeneratedGetterNullResultsRemainCanonicalNullAndNeverNumericConversions()
    {
        foreach (string name in new[] { nameof(Sources.Inner), nameof(Sources.Param), nameof(Sources.Actual) })
        {
            Fixture fixture = Capture(name); Seed seed = Create(fixture, typeof(ArgumentOutOfRangeException), empty: true);
            CollectionAssert.AreEqual(new uint[3], Execute(fixture, seed.Arena, seed.Owner, 1));
            Assert.AreEqual(3, fixture.Layout.ResultWordCount);
            Assert.AreEqual(WarpPortableStackCategory.Reference, fixture.Program.EntryProjection.ResultType.Category);
            Release(seed.Arena);
        }
    }

    internal static void GeneratedGetterRejectsUnadmittedOwnerDataWithoutPublishingOrdinaryOutput()
    {
        Fixture fixture = Capture(nameof(Sources.HResult)); Seed seed = Create(fixture, typeof(Exception));
        foreach (uint[] invalid in new uint[][] { [0, 0, 0], [seed.Owner[0] + 1, seed.Owner[1], seed.Owner[2]], [seed.Owner[0], seed.Owner[1], seed.Owner[2] + 1] })
        {
            uint[] state = ExecuteState(fixture, seed.Arena, invalid, 1, resultSeed: 0xDEADBEEF);
            Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(3u, state[WarpLogicalMachineLayout.FaultKindOffset]);
            Assert.AreEqual(0xDEADBEEFu, state[fixture.Layout.GetResultWordOffset(0, 64)]);
            Assert.AreEqual(0u, seed.Arena[WarpPortableHeapLayout.PendingResult]);
        }
        Release(seed.Arena);
    }

    internal static void CapturedFormattingAndExplicitNullAccessorsStayDeniedUntilRealFaultBindings()
    {
        foreach (string name in new[] { nameof(Sources.Message), nameof(Sources.Trace), nameof(Sources.ExplicitNull) })
        {
            WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Sources).GetMethod(name)!);
            WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
            WarpVerificationException denied = Assert.ThrowsExactly<WarpVerificationException>(() =>
                WarpPortableClosedExceptionAccessorPlan.Capture(graph, typed, WarpPortableSourceHeapSchema.Create(graph, typed)));
            Assert.AreEqual("WRPCLR2470", denied.Code, StringComparer.Ordinal);
            WarpVerificationException unbound = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(graph, typed));
            Assert.AreEqual("WRPCLR2300", unbound.Code, StringComparer.Ordinal);
        }
        Assert.AreNotEqual("raw", ReferenceMessage("count"), StringComparer.Ordinal);
    }

    internal static void GetterOwnerRootAndEmbeddedParameterIdentitySurvivePreciseCollection()
    {
        Fixture fixture = Capture(nameof(Sources.Param)); Seed seed = Create(fixture, typeof(ArgumentOutOfRangeException));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.AcquireRoot), seed.Arena,
            [.. seed.Owner, WarpPortableHeapLayout.StrongRoot, 0, 0, 0]));
        uint root = seed.Arena[WarpPortableHeapLayout.Result], generation = seed.Arena[WarpPortableHeapLayout.Result + 1];
        Release(seed.Arena);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.RequestCollection), seed.Arena, []));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.Collect), seed.Arena, []));
        Acquire(seed.Arena);
        CollectionAssert.AreEqual(seed.Param, Execute(fixture, seed.Arena, seed.Owner, 1));
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.GetType), seed.Arena, seed.Param));
        Release(seed.Arena);
        Assert.AreEqual(0u, Service(nameof(WarpPortableHeapServices.ReleaseRoot), seed.Arena, [root, generation]));
    }

    internal static void GeneratedSourceHResultSetterPreservesAllInt32BitsAndInitializedGeneration()
    {
        Fixture fixture = Capture(nameof(Sources.SetHResult)); Seed seed = Create(fixture, typeof(Exception));
        foreach (uint value in new uint[] { 0, 1, uint.MaxValue, 0x80000000, 0x81234567 })
        {
            CollectionAssert.AreEqual(new[] { value }, Execute(fixture, seed.Arena, [.. seed.Owner, value], 1));
        }
        Exception oracle = new InvalidOperationException();
        Assert.AreEqual(unchecked((int)0x81234567), Sources.SetHResult(oracle, unchecked((int)0x81234567)));
        Assert.AreEqual(0u, seed.Arena[WarpPortableHeapLayout.PendingResult]);
        Assert.IsTrue(fixture.Typed.Methods.SelectMany(method => method.Instructions).Any(instruction =>
            instruction.RequiredIntrinsic?.Contains("set_HResult", StringComparison.Ordinal) == true && instruction.Effects.Contains(WarpPortableTypedEffect.WriteMemory)));
        Scratch(seed.Arena, new uint[15]);
        Assert.AreEqual(WarpPortableHeapLayout.InvalidOperation, Service(nameof(WarpPortableHeapServices.InitializeSourceExceptionData), seed.Arena, [.. seed.Owner, 0]));
        Release(seed.Arena);
    }

    internal static void GeneratedVirtualGetterRejectsAnUninstantiatedCapturedOverrideOwner()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Sources).GetMethod(nameof(Sources.ParamWithUnusedHolder))!);
        Assert.IsTrue(graph.Types.Any(type => type.SourceType == typeof(OverriddenArgument) && !type.Instantiated));
        Fixture fixture = Capture(graph); Seed seed = Create(fixture, typeof(OverriddenArgument));
        uint[] state = ExecuteState(fixture, seed.Arena, [.. seed.Owner, 0, 0, 0], 1, resultSeed: 0xDEADBEEF);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(3u, state[WarpLogicalMachineLayout.FaultKindOffset]);
        for (int word = 0; word < 3; word++) { Assert.AreEqual(0xDEADBEEFu, state[fixture.Layout.GetResultWordOffset(word, 64)]); }
        Assert.AreEqual(0u, seed.Arena[WarpPortableHeapLayout.PendingResult]); Release(seed.Arena);
        Holder holder = ReferenceHolder("count");
        Assert.IsNotNull(holder.Value);
        Assert.AreEqual("source-override", Sources.ParamWithUnusedHolder(holder.Value, holder), StringComparer.Ordinal);
    }

    private sealed class OverriddenArgument : ArgumentException
    {
        public OverriddenArgument() { }
        public OverriddenArgument(string message) : base(message) { }
        public OverriddenArgument(string message, Exception innerException) : base(message, innerException) { }
        public OverriddenArgument(string message, string paramName) : base(message, paramName) { }
        public OverriddenArgument(string message, string paramName, Exception innerException) : base(message, paramName, innerException) { }
        public override string ParamName => "source-override";
    }
    private static string ReferenceMessage(string paramName) => new ArgumentException("raw", paramName).Message;
    private static string ReferenceOverride(string paramName) => new OverriddenArgument("raw", paramName).ParamName;
    private static Holder ReferenceHolder(string paramName) => new(new OverriddenArgument("raw", paramName));
    private sealed class Holder(OverriddenArgument value) { public readonly OverriddenArgument Value = value; }
    private static class Sources
    {
        public static int HResult(Exception value) => value.HResult;
        public static Exception? Inner(Exception value) => value.InnerException;
        public static string? Param(ArgumentException value) => value.ParamName;
        public static string? ParamWithUnusedHolder(ArgumentException value, Holder unused) => value.ParamName;
        public static object? Actual(ArgumentOutOfRangeException value) => value.ActualValue;
        public static string? TypeName(TypeInitializationException value) => value.TypeName;
        public static Exception Base(Exception value) => value.GetBaseException();
        public static string Message(Exception value) => value.Message;
        public static string? Trace(Exception value) => value.StackTrace;
        public static int ExplicitNull() => ((Exception)null!).HResult;
        public static int SetHResult(Exception value, int result) { value.HResult = result; return value.HResult; }
    }
}
