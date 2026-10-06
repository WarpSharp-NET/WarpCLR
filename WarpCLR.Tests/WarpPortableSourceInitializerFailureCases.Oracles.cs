using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableSourceInitializerFailureCases
{
    internal static void ActualCoreClrOrdinaryCctorFailuresMatchCapturedDataAndAlwaysNestExplicitTies()
    {
        (Type Type, Action Enter) oom = ReservedOriginalCil(typeof(OutOfMemoryException), "OracleReservedOom", "ordinary-explicit-oom");
        (Type Type, Action Enter) stack = ReservedOriginalCil(typeof(StackOverflowException), "OracleReservedStack", "ordinary-explicit-stack");
        foreach ((Type type, Action enter, Type innerType) in new (Type, Action, Type)[]
        {
            (typeof(OracleOrdinary), OracleOrdinary.Enter, typeof(InvalidOperationException)),
            (typeof(OracleNested), OracleNested.Enter, typeof(TypeInitializationException)),
            (oom.Type, oom.Enter, typeof(OutOfMemoryException)),
            (stack.Type, stack.Enter, typeof(StackOverflowException)),
        })
        {
            // Reflection/CIL capture does not invoke the original static initializer.
            Fixture fixture = Capture(type, "Enter"); Acquire(fixture);
            if (type == typeof(OracleOrdinary))
            {
                Assert.IsTrue(fixture.Typed.Methods.First(method => string.Equals(method.Identity, fixture.Graph.EntryIdentity, StringComparison.Ordinal))
                    .Instructions.Any(instruction => instruction.Reachable && !instruction.ExceptionMemberships.IsEmpty));
                Assert.IsEmpty(fixture.Entry.Origin.ExceptionMemberships);
            }
            Assert.AreEqual(0u, Service(fixture, nameof(WarpPortableHeapServices.PrepareSourceInitializerFailure), [fixture.Entry.TypeRecord]));
            uint[] inner = Inner(fixture, innerType); Assert.AreEqual(0u, Cache(fixture, inner));
            uint[] wrapper = Reference(fixture.Arena, fixture.Record + WarpPortableSourceInitializerFailureLayout.WrapperReference);
            uint payload = Payload(fixture.Arena, wrapper);
            // Independent original CoreCLR source oracle after the generated data/cache protocol.
            TypeInitializationException first = Assert.ThrowsExactly<TypeInitializationException>(enter);
            TypeInitializationException second = Assert.ThrowsExactly<TypeInitializationException>(enter);
            Assert.AreSame(first, second); Assert.AreSame(first.InnerException, second.InnerException);
            Assert.AreEqual(innerType, first.InnerException!.GetType());
            CollectionAssert.AreEqual(first.TypeName.Select(unit => (ushort)unit).ToArray(),
                Text(fixture.Arena, Reference(fixture.Arena, payload + WarpPortableSourceExceptionLayout.TypeNameWord)));
            CollectionAssert.AreEqual(first.Message.Select(unit => (ushort)unit).ToArray(),
                Text(fixture.Arena, Reference(fixture.Arena, payload + WarpPortableSourceExceptionLayout.MessageWord)));
            Assert.AreEqual(unchecked((uint)first.HResult), fixture.Arena[payload + WarpPortableSourceExceptionLayout.HResultWord]);
            if (innerType == typeof(TypeInitializationException))
            {
                Assert.AreNotSame(first, first.InnerException);
                Assert.AreEqual("ordinary-inner-type", ((TypeInitializationException)first.InnerException).TypeName, StringComparer.Ordinal);
            }
            Assert.AreEqual(0u, fixture.Arena[WarpPortableHeapLayout.PendingResult]);
            RecordFailureOracle(fixture, type, first);
        }
        Assert.AreEqual(0u, CaptureProbe.Calls);
    }

    private static void RecordFailureOracle(Fixture fixture, Type source, TypeInitializationException actual)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "initializer-failure-oracles");
        Directory.CreateDirectory(directory);
        object record = new
        {
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            WarpPortableSourceOperationOrigin.Semantics, fixture.Graph.GraphHash, fixture.Typed.VerifiedHash, fixture.Schema.SchemaHash, fixture.Plan.PlanHash,
            fixture.Plan.Resources.CorelibHash, ResourceHash = fixture.Plan.Resources.ContractHash,
            SourceTypeUnits = WarpCLR.Verifier.WarpPortableSnapshotIdentity.CodeUnits(source.FullName!),
            ActualTypeNameUnits = WarpCLR.Verifier.WarpPortableSnapshotIdentity.CodeUnits(actual.TypeName),
            ActualMessageUnits = WarpCLR.Verifier.WarpPortableSnapshotIdentity.CodeUnits(actual.Message), actual.HResult,
            InnerTypeUnits = WarpCLR.Verifier.WarpPortableSnapshotIdentity.CodeUnits(actual.InnerException!.GetType().FullName!),
            CachedSameOuterAndInner = true, ExplicitTieAlwaysNested = actual.InnerException is TypeInitializationException,
            OriginalEntryHasProtectedCil = fixture.Graph.Methods.First(method => string.Equals(method.Identity, fixture.Graph.EntryIdentity, StringComparison.Ordinal)).ExceptionRegions.Length != 0,
            EntryInvocationHasNoTargetBodyMembership = fixture.Entry.Origin.ExceptionMemberships.IsEmpty,
            SourceCil = fixture.Graph.Methods.Select(method => new { IdentityUnits = WarpCLR.Verifier.WarpPortableSnapshotIdentity.CodeUnits(method.Identity), Cil = method.Cil.ToArray() }),
            OracleOnlyOriginalSourceExecution = true, GeneratedFailureCacheConsistencyOnly = true, SourceFailureRuntimeAdmission = false,
        };
        File.WriteAllText(Path.Combine(directory, fixture.Plan.PlanHash + ".json"), JsonSerializer.Serialize(record));
    }

    private static class OracleOrdinary
    {
        static OracleOrdinary() { Raise(); }
        private static void Raise() => throw new InvalidOperationException("ordinary-inner");
        public static void Enter()
        {
            try { CaptureProbe.Calls++; }
            catch (TypeInitializationException) { CaptureProbe.Calls = 99; }
        }
    }
    private static class OracleNested
    {
        static OracleNested() { Raise(); }
        private static void Raise() => throw new TypeInitializationException("ordinary-inner-type", null);
        public static void Enter() { }
    }
    private static (Type Type, Action Enter) ReservedOriginalCil(Type exception, string name, string message)
    {
        // The input is original CIL for a source-language-reserved exception
        // witness. Reflection capture reads it before this independent oracle
        // executes; no production analyzer rule is weakened or suppressed.
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("InitializerReserved" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        TypeBuilder type = assembly.DefineDynamicModule("main").DefineType(name, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        ILGenerator constructor = type.DefineTypeInitializer().GetILGenerator();
        constructor.Emit(OpCodes.Ldstr, message); constructor.Emit(OpCodes.Newobj, exception.GetConstructor([typeof(string)])!); constructor.Emit(OpCodes.Throw);
        type.DefineMethod("Enter", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes).GetILGenerator().Emit(OpCodes.Ret);
        Type actual = type.CreateType()!;
        return (actual, actual.GetMethod("Enter")!.CreateDelegate<Action>());
    }
}
