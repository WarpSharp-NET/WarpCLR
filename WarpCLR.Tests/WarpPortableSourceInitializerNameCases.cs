using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using WarpCLR.Compiler;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableSourceInitializerNameCases
{
    internal static void ActualCoreClrTopLevelAndNestedInitializerNamesUseOwnMetadataNamespace()
    {
        Type top = SavedType("Initializer.Namespace.Qualified", nested: false, generic: false);
        Check(top, "Initializer.Namespace.Qualified");
        Type nested = SavedType("Initializer.Namespace.Outer", nested: true, generic: false);
        Assert.AreEqual("Initializer.Namespace.Outer+Inner", nested.FullName, StringComparer.Ordinal);
        Check(nested, "Inner");
    }

    internal static void ActualCoreClrClosedGenericInitializerNamesExcludeDisplayArguments()
    {
        Type definition = SavedType("Initializer.Namespace.Generic`1", nested: false, generic: true);
        Type first = definition.MakeGenericType(typeof(int));
        Type second = definition.MakeGenericType(typeof(uint));
        WarpPortableSourceInitializerPlan firstPlan = Check(first, "Initializer.Namespace.Generic`1");
        WarpPortableSourceInitializerPlan secondPlan = Check(second, "Initializer.Namespace.Generic`1", priorCalls: 1);
        Assert.AreNotEqual(firstPlan.GraphHash, secondPlan.GraphHash, StringComparer.Ordinal);
        Assert.AreNotEqual(firstPlan.PlanHash, secondPlan.PlanHash, StringComparer.Ordinal);
        Assert.AreNotEqual(firstPlan.Types.First(type => type.Type == firstPlan.Triggers.First(row => row.EntryInvocation).Type).Initializer,
            secondPlan.Types.First(type => type.Type == secondPlan.Triggers.First(row => row.EntryInvocation).Type).Initializer, StringComparer.Ordinal);
    }

    internal static void ActualCoreClrSavedRawMetadataInitializerNamesMatchCapturedCodeUnits()
    {
        foreach (string unit in new[] { "\uD800", "\uD801", "\uDC00", "\uDC01", "\uFFFD", "\uD800\uDC00" })
        {
            string requested = "Raw" + unit + ".Source" + unit;
            Type source = SavedType(requested, nested: false, generic: false);
            WarpPortableSourceInitializerPlan plan = Check(source, source.FullName!);
            Record(source, plan, requested);
        }
        // The reflection emitter's metadata producer may replace invalid UTF-16.
        // Capture binds the actual loaded TypeDef and records that producer boundary.
    }

    internal static void ActualCoreClrInMemoryRawInitializerNamesMatchCapturedCodeUnits()
    {
        foreach (string unit in new[] { "\uD800", "\uD801", "\uDC00", "\uDC01", "\uFFFD", "\uD800\uDC00" })
        {
            string requested = "Dynamic" + unit + ".Source" + unit;
            var name = new AssemblyName("InitializerDynamic" + Guid.NewGuid().ToString("N"));
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
            ModuleBuilder module = assembly.DefineDynamicModule("source");
            FieldInfo counter = Counter(module);
            TypeBuilder source = module.DefineType(requested, TypeAttributes.Public | TypeAttributes.Class);
            DefineFailure(source, counter);
            Type actual = source.CreateType()!;
            WarpPortableSourceInitializerPlan plan = Check(actual, actual.FullName!);
            Record(actual, plan, requested);
        }
    }

    internal static void InMemoryNestedInitializerNameRequiresProvedOwnMetadataNamespace()
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("InitializerNested" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        TypeBuilder outer = assembly.DefineDynamicModule("source").DefineType("Dynamic.Namespace.Outer", TypeAttributes.Public);
        TypeBuilder source = outer.DefineNestedType("Inner", TypeAttributes.NestedPublic | TypeAttributes.Class);
        DefineFailure(source); source.CreateType(); Type actual = outer.CreateType()!.GetNestedType("Inner")!;
        WarpVerificationException failure = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableTypeInitializerName.Capture(actual));
        Assert.AreEqual("WRPCLR2460", failure.Code, StringComparer.Ordinal);
    }

    internal static void ActualCoreClrCachedInitializerWrapperResetsItsPropagationTrace()
    {
        TypeInitializationException first = FirstTrigger();
        string firstTrace = first.StackTrace!;
        TypeInitializationException second = SecondTrigger();
        Assert.AreSame(first, second); Assert.AreSame(first.InnerException, second.InnerException);
        Assert.IsTrue(firstTrace.Contains(nameof(FirstTrigger), StringComparison.Ordinal));
        Assert.IsFalse(second.StackTrace!.Contains(nameof(FirstTrigger), StringComparison.Ordinal));
        Assert.IsTrue(second.StackTrace.Contains(nameof(SecondTrigger), StringComparison.Ordinal));
    }

    private static WarpPortableSourceInitializerPlan Check(Type source, string expected, int priorCalls = 0)
    {
        MethodInfo trigger = source.GetMethod("Trigger")!;
        FieldInfo counter = source.Assembly.GetType("OracleCounter")!.GetField("Calls")!;
        Assert.AreEqual(priorCalls, counter.GetValue(null));
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(trigger);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableSourceInitializerPlan plan = WarpPortableSourceInitializerPlan.Capture(graph, typed, schema);
        Assert.AreEqual(priorCalls, counter.GetValue(null));
        Assert.IsNotNull(typed.EntryInitializerTrigger);
        WarpPortableSourceInitializerType type = plan.Types.First(type => type.Type == schema.TypeId(WarpPortableMethodGraphIdentity.Type(source)));
        // This invocation is a separately identified actual CLR reference oracle.
        // Neither graph/type capture nor the generated runtime executes source CIL.
        TargetInvocationException wrapper = Assert.ThrowsExactly<TargetInvocationException>(() => trigger.Invoke(null, null));
        Assert.IsInstanceOfType<TypeInitializationException>(wrapper.InnerException);
        var failure = (TypeInitializationException)wrapper.InnerException!;
        Assert.AreEqual(expected, failure.TypeName, StringComparer.Ordinal);
        CollectionAssert.AreEqual(type.ExceptionTypeName.ToArray(), failure.TypeName!.Select(character => (ushort)character).ToArray());
        Assert.AreEqual(type.DefaultHResult, unchecked((uint)failure.HResult));
        Assert.AreEqual(priorCalls + 1, counter.GetValue(null));
        Record(source, plan, expected);
        return plan;
    }

    private static Type SavedType(string name, bool nested, bool generic)
    {
        string identity = "InitializerSaved" + Guid.NewGuid().ToString("N");
        var assembly = new PersistedAssemblyBuilder(new AssemblyName(identity), typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule("source");
        FieldInfo counter = Counter(module);
        TypeBuilder outer = module.DefineType(name, TypeAttributes.Public | TypeAttributes.Class);
        TypeBuilder source = nested ? outer.DefineNestedType("Inner", TypeAttributes.NestedPublic | TypeAttributes.Class) : outer;
        if (generic) { source.DefineGenericParameters("T"); }
        DefineFailure(source, counter); source.CreateType();
        if (nested) { outer.CreateType(); }
        string directory = Path.Combine(AppContext.BaseDirectory, "initializer-name-fixtures");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, identity + ".dll");
        assembly.Save(path);
        Assembly loaded = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        Type[] types = loaded.GetTypes().Where(type => type.GetMethod("Trigger") is not null).ToArray();
        Assert.HasCount(1, types); return types[0];
    }

    private static FieldBuilder Counter(ModuleBuilder module)
    {
        TypeBuilder counter = module.DefineType("OracleCounter", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        FieldBuilder field = counter.DefineField("Calls", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        counter.CreateType(); return field;
    }

    private static void DefineFailure(TypeBuilder source, FieldInfo? counter = null)
    {
        ILGenerator initializer = source.DefineTypeInitializer().GetILGenerator();
        if (counter is not null)
        {
            initializer.Emit(OpCodes.Ldsfld, counter); initializer.Emit(OpCodes.Ldc_I4_1);
            initializer.Emit(OpCodes.Add); initializer.Emit(OpCodes.Stsfld, counter);
        }
        initializer.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes)!);
        initializer.Emit(OpCodes.Throw);
        ILGenerator method = source.DefineMethod("Trigger", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes).GetILGenerator();
        method.Emit(OpCodes.Ret);
    }

    private static void Record(Type source, WarpPortableSourceInitializerPlan plan, string requested)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "initializer-name-fixtures");
        Directory.CreateDirectory(directory);
        var type = plan.Types.First(type => type.Type == plan.Triggers.First(row => row.EntryInvocation).Type);
        object record = new
        {
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Semantics = WarpPortableTypeInitializerName.Semantics, plan.GraphHash, plan.VerifiedHash, plan.TypeSchemaHash, plan.PlanHash,
            type.Type, InitializerUnits = WarpPortableSnapshotIdentity.CodeUnits(type.Initializer), RequestedUnits = WarpPortableSnapshotIdentity.CodeUnits(requested),
            ActualReflectionUnits = WarpPortableSnapshotIdentity.CodeUnits(source.FullName!), CapturedExceptionNameUnits = type.ExceptionTypeName,
            Module = source.Module.FullyQualifiedName, source.MetadataToken, source.Assembly.IsDynamic,
            OracleOnlyOriginalSourceExecution = true, SourceInitializerRuntimeAdmission = false,
        };
        File.WriteAllText(Path.Combine(directory, plan.PlanHash + ".json"), JsonSerializer.Serialize(record));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TypeInitializationException FirstTrigger()
    {
        try { TraceFailure.Trigger(); }
        catch (TypeInitializationException failure) { return failure; }
        throw new InvalidOperationException("The CLR fixture initializer unexpectedly completed.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TypeInitializationException SecondTrigger()
    {
        try { TraceFailure.Trigger(); }
        catch (TypeInitializationException failure) { return failure; }
        throw new InvalidOperationException("The CLR fixture initializer unexpectedly completed.");
    }

    private static class TraceFailure
    {
        static TraceFailure() { RaiseOriginal(); }
        public static void Trigger() { }
        private static void RaiseOriginal() => throw new InvalidOperationException("The separately executed CLR initializer trace oracle.");
    }
}
