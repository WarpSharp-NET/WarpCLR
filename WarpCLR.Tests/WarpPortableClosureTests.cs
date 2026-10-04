using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpPortableClosureTests
{
    [TestMethod]
    public void DirectAndMutualRecursionDiscoverEachClosedMethodOnce()
    {
        WarpPortableMethodGraph direct = WarpPortableMethodGraph.Discover(Source(nameof(Kernels.Direct)));
        WarpPortableMethodGraph mutual = WarpPortableMethodGraph.Discover(Source(nameof(Kernels.Even)));
        WarpPortableMethodGraphMethod recursive = Only(direct.Methods.Where(method => string.Equals(method.SourceMethod.Name, nameof(Kernels.Direct), StringComparison.Ordinal)));
        CollectionAssert.Contains(recursive.Dependencies.ToArray(), recursive.Identity);
        Assert.AreEqual(2, mutual.Methods.Count(method => method.SourceMethod.DeclaringType == typeof(Kernels)));
        Assert.IsTrue(mutual.Methods.All(method => method.Dependencies.Length == 1));
    }

    [TestMethod]
    public void ClosedMethodAndTypeSpecializationsResolveFieldsAndConstructorInContext()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(Source(nameof(Kernels.Specialized)));
        WarpPortableMethodGraphMethod helper = Only(graph.Methods.Where(method => string.Equals(method.SourceMethod.Name, nameof(Kernels.Identity), StringComparison.Ordinal)));
        Assert.AreEqual(typeof(uint), ((MethodInfo)helper.SourceMethod).GetGenericArguments()[0]);
        WarpPortableMethodGraphField field = Only(graph.Fields.Where(field => string.Equals(field.SourceField.Name, nameof(Box<uint>.Value), StringComparison.Ordinal)));
        Assert.AreEqual(typeof(uint), field.SourceField.FieldType);
        Assert.IsTrue(graph.Methods.Any(method => method.SourceMethod is ConstructorInfo && method.SourceMethod.DeclaringType == typeof(Box<uint>)));
    }

    [TestMethod]
    public void VirtualAndInterfaceDispatchIncludesConcreteOverrides()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(Source(nameof(Kernels.Dispatch)));
        Assert.IsTrue(graph.Dispatches.Any(dispatch => graph.Methods.Any(method => string.Equals(method.Identity, dispatch.Target, StringComparison.Ordinal) &&
            method.SourceMethod.DeclaringType == typeof(Calculation) && string.Equals(method.SourceMethod.Name, nameof(Calculation.Apply), StringComparison.Ordinal))));
        Assert.IsTrue(graph.Dispatches.Any(dispatch => graph.Methods.Any(method => string.Equals(method.Identity, dispatch.Slot, StringComparison.Ordinal) &&
            method.SourceMethod.DeclaringType == typeof(ICalculation))));
        Assert.IsTrue(graph.Fields.Any(field => field.SourceField.DeclaringType == typeof(Calculation) && !field.IsStatic));
    }

    [TestMethod]
    public void ConcreteInputClosureChangesDispatchIdentityWithoutInvokingUserMethods()
    {
        MethodInfo source = Source(nameof(Kernels.InputDispatch));
        WarpPortableMethodGraph first = WarpPortableMethodGraph.Discover(source, concreteTypes: [typeof(Calculation)]);
        WarpPortableMethodGraph second = WarpPortableMethodGraph.Discover(source, concreteTypes: [typeof(OtherCalculation)]);
        Assert.AreNotEqual(first.GraphHash, second.GraphHash, StringComparer.Ordinal);
        Assert.AreEqual(first.GraphHash, WarpPortableMethodGraph.Discover(source, concreteTypes: [typeof(Calculation)]).GraphHash, StringComparer.Ordinal);
    }

    [TestMethod]
    public void ClosedGenericInterfaceAndVirtualDelegateTargetsAreResolved()
    {
        WarpPortableMethodGraph generic = WarpPortableMethodGraph.Discover(Source(nameof(Kernels.GenericDispatch)));
        Assert.IsTrue(generic.Dispatches.Any(dispatch => generic.Methods.Any(method =>
            string.Equals(method.Identity, dispatch.Target, StringComparison.Ordinal) && method.SourceMethod.DeclaringType == typeof(GenericCalculation<uint>))));
        WarpPortableMethodGraph bound = WarpPortableMethodGraph.Discover(Source(nameof(Kernels.VirtualDelegate)));
        Assert.IsTrue(bound.Methods.Any(method => method.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldvirtftn)));
        Assert.IsTrue(bound.Dispatches.Any(dispatch => bound.Methods.Any(method =>
            string.Equals(method.Identity, dispatch.Target, StringComparison.Ordinal) && method.SourceMethod.DeclaringType == typeof(Calculation))));
    }

    [TestMethod]
    public void AnUnresolvedInterfaceInputCannotProduceAnIncompleteClosedGraph()
    {
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableMethodGraph.Discover(Source(nameof(Kernels.InputDispatch))));
        Assert.AreEqual("WRPCLR2100", error.Code, StringComparer.Ordinal);
        StringAssert.Contains(error.Message, "no concrete target", StringComparison.Ordinal);
    }

    [TestMethod]
    public void ClosedGenericDefaultConstructionIncludesItsActualUserConstructor()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(Source(nameof(Kernels.DefaultConstruction)));
        Assert.IsTrue(graph.Intrinsics.Any(intrinsic => intrinsic.Contains("object.default-construct", StringComparison.Ordinal)));
        Assert.IsTrue(graph.Methods.Any(method => method.SourceMethod is ConstructorInfo && method.SourceMethod.DeclaringType == typeof(DefaultValue)));
        Assert.IsTrue(graph.Types.Any(type => type.SourceType == typeof(DefaultValue) && type.Instantiated));
    }

    [TestMethod]
    public void StringOperationsUseExplicitPortableServicesRatherThanHostCil()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(Source(nameof(Kernels.StringData)));
        Assert.IsTrue(graph.Intrinsics.Any(intrinsic => intrinsic.Contains("string.concat", StringComparison.Ordinal)));
        Assert.IsTrue(graph.Intrinsics.Any(intrinsic => intrinsic.Contains("string.get_Length", StringComparison.Ordinal)));
        Assert.IsTrue(graph.Intrinsics.Any(intrinsic => intrinsic.Contains("string.character", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AReferencedAssemblyRequiresExplicitPermissionAndJoinsTheClosureHash()
    {
        (MethodInfo caller, MethodInfo helper) = DynamicAssemblyCall();
        Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableMethodGraph.Discover(caller));
        Assembly[] allowed = [caller.Module.Assembly, helper.Module.Assembly];
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(caller, allowed);
        Assert.HasCount(2, graph.Methods);
        Assert.IsTrue(graph.Methods.Any(method => method.SourceMethod == helper));
        Assert.AreEqual(graph.GraphHash, WarpPortableMethodGraph.Discover(caller, allowed.Reverse()).GraphHash, StringComparer.Ordinal);
    }

    [TestMethod]
    public void TypeInitializerAndStaticStorageAreCapturedWithoutExecutingInitializer()
    {
        MethodInfo method = typeof(FaultingInitializer).GetMethod(nameof(FaultingInitializer.Read))!;
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(method);
        WarpPortableMethodGraphType type = Only(graph.Types.Where(type => type.SourceType == typeof(FaultingInitializer)));
        Assert.IsNotNull(type.Initializer);
        Assert.IsTrue(graph.Methods.Any(node => string.Equals(node.Identity, type.Initializer, StringComparison.Ordinal) && node.SourceMethod is ConstructorInfo));
        Assert.IsTrue(graph.Fields.Any(field => field.SourceField.DeclaringType == typeof(FaultingInitializer) && field.IsStatic));
        Assert.IsTrue(graph.Intrinsics.Any(intrinsic => intrinsic.Contains("exception.construct", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void DelegateTargetsAndPortableBitCastAreExplicitDependencies()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(Source(nameof(Kernels.DelegateCall)));
        Assert.IsTrue(graph.Methods.Any(method => string.Equals(method.SourceMethod.Name, nameof(Kernels.Square), StringComparison.Ordinal)));
        Assert.IsTrue(graph.Intrinsics.Any(intrinsic => intrinsic.Contains("delegate.bind", StringComparison.Ordinal)));
        Assert.IsTrue(graph.Intrinsics.Any(intrinsic => intrinsic.Contains("delegate.invoke", StringComparison.Ordinal)));
        WarpPortableMethodGraph cast = WarpPortableMethodGraph.Discover(Source(nameof(Kernels.BitCast)));
        Assert.IsTrue(cast.Intrinsics.Any(intrinsic => intrinsic.Contains("numeric.bit-cast", StringComparison.Ordinal)));
        Assert.IsTrue(cast.Methods.Any(method => method.Intrinsic is not null && method.Cil.IsEmpty));
    }

    [TestMethod]
    public void ExceptionRegionsSourceOffsetsAndStaticArrayDataRemainImmutable()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(Source(nameof(Kernels.Cleanup)));
        WarpPortableMethodGraphMethod entry = Only(graph.Methods.Where(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal)));
        Assert.IsFalse(entry.ExceptionRegions.IsEmpty);
        Assert.IsTrue(entry.Instructions.All(instruction => instruction.Offset < instruction.NextOffset && instruction.NextOffset <= entry.Cil.Length));
        WarpPortableMethodGraph data = WarpPortableMethodGraph.Discover(Source(nameof(Kernels.ArrayData)));
        Assert.IsTrue(data.Fields.Any(field => !field.InitializedData.IsEmpty));
        Assert.IsTrue(data.Intrinsics.Any(intrinsic => intrinsic.Contains("array.initialize-data", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AssemblyPermissionMismatchAndUncataloguedHostCallsAreRejected()
    {
        WarpVerificationException mismatch = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableMethodGraph.Discover(Source(nameof(Kernels.Direct)), [typeof(object).Assembly]));
        Assert.AreEqual("WRPCLR2100", mismatch.Code, StringComparer.Ordinal);
        WarpVerificationException host = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableMethodGraph.Discover(Source(nameof(Kernels.HostSleep))));
        Assert.AreEqual("WRPCLR2100", host.Code, StringComparer.Ordinal);
        StringAssert.Contains(host.Message, "portable intrinsic", StringComparison.Ordinal);
    }

    [TestMethod]
    public void OpenGenericsAndExpandingGenericRecursionTerminateWithAdmissionErrors()
    {
        WarpVerificationException open = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableMethodGraph.Discover(Source(nameof(Kernels.Identity))));
        Assert.AreEqual("WRPCLR2100", open.Code, StringComparer.Ordinal);
        Assert.Throws<WarpCompilationResourceException>(() => WarpPortableMethodGraph.Discover(Source(nameof(Kernels.Expanding))));
    }

    [TestMethod]
    [DataRow(new byte[] { 0xFE })]
    [DataRow(new byte[] { 0x20, 0 })]
    [DataRow(new byte[] { 0x45, 0xFF, 0xFF, 0xFF, 0xFF })]
    [DataRow(new byte[] { 0xFF })]
    [DataRow(new byte[] { 0x2B, 1, 0x20, 0, 0, 0, 0, 0x2A })]
    public void TruncatedUnknownAndInteriorBranchEncodingsFailAtExactSourceOffset(byte[] cil)
    {
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableMethodGraphDecoder.Decode(cil, "malformed.test"));
        Assert.AreEqual("WRPCLR2101", error.Code, StringComparer.Ordinal);
        Assert.AreEqual(0, error.IlOffset);
    }

    [TestMethod]
    public void UnsafeNativeAllocationIsRejectedBeforeAnyExecution()
    {
        MethodInfo method = DynamicUnsafe();
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableMethodGraph.Discover(method));
        Assert.AreEqual("WRPCLR2100", error.Code, StringComparer.Ordinal);
        StringAssert.Contains(error.Message, "unsafe memory", StringComparison.Ordinal);
        Assert.IsNotNull(error.IlOffset);
    }

    private static MethodInfo Source(string name) => typeof(Kernels).GetMethod(name)!;

    private static T Only<T>(IEnumerable<T> items)
    {
        T[] values = items.ToArray();
        Assert.HasCount(1, values);
        return values[0];
    }

    private static MethodInfo DynamicUnsafe()
    {
        var name = new AssemblyName("WarpCLR.PortableClosure.Unsafe." + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder type = assembly.DefineDynamicModule(name.Name!).DefineType("UnsafeProbe", TypeAttributes.Public);
        MethodBuilder method = type.DefineMethod("Allocate", MethodAttributes.Public | MethodAttributes.Static, typeof(uint), Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Localloc);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return type.CreateType()!.GetMethod("Allocate")!;
    }

    private static (MethodInfo Caller, MethodInfo Helper) DynamicAssemblyCall()
    {
        var helperName = new AssemblyName("WarpCLR.PortableClosure.Helper." + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));
        AssemblyBuilder helperAssembly = AssemblyBuilder.DefineDynamicAssembly(helperName, AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder helperType = helperAssembly.DefineDynamicModule(helperName.Name!).DefineType("Helper", TypeAttributes.Public);
        MethodBuilder helper = helperType.DefineMethod("AddOne", MethodAttributes.Public | MethodAttributes.Static, typeof(uint), [typeof(uint)]);
        ILGenerator helperIl = helper.GetILGenerator();
        helperIl.Emit(OpCodes.Ldarg_0);
        helperIl.Emit(OpCodes.Ldc_I4_1);
        helperIl.Emit(OpCodes.Add);
        helperIl.Emit(OpCodes.Ret);
        MethodInfo target = helperType.CreateType()!.GetMethod("AddOne")!;
        var callerName = new AssemblyName("WarpCLR.PortableClosure.Caller." + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));
        AssemblyBuilder callerAssembly = AssemblyBuilder.DefineDynamicAssembly(callerName, AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder callerType = callerAssembly.DefineDynamicModule(callerName.Name!).DefineType("Caller", TypeAttributes.Public);
        MethodBuilder caller = callerType.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(uint), [typeof(uint)]);
        ILGenerator callerIl = caller.GetILGenerator();
        callerIl.Emit(OpCodes.Ldarg_0);
        callerIl.Emit(OpCodes.Call, target);
        callerIl.Emit(OpCodes.Ret);
        return (callerType.CreateType()!.GetMethod("Entry")!, target);
    }

    private static class Kernels
    {
        public static uint Direct(uint value) => value == 0 ? 0 : Direct(value - 1) + 1;

        public static uint Even(uint value) => value == 0 ? 1 : Odd(value - 1);

        private static uint Odd(uint value) => value == 0 ? 0 : Even(value - 1);

        public static T Identity<T>(T value) => value;

        public static uint Specialized(uint value) => Identity(new Box<uint>(value).Value);

        public static uint Dispatch(uint value)
        {
            CalculationBase calculation = new Calculation(value);
            ICalculation contract = (ICalculation)calculation;
            return calculation.Apply(value) + contract.Apply(value);
        }

        public static uint InputDispatch(ICalculation value, uint argument) => value.Apply(argument);

        public static uint GenericDispatch(uint value)
        {
            IGenericCalculation<uint> calculation = new GenericCalculation<uint>();
            return calculation.Echo(value);
        }

        public static uint VirtualDelegate(uint value)
        {
            CalculationBase calculation = new Calculation(value);
            Func<uint, uint> function = calculation.Apply;
            return function(value);
        }

        public static uint DefaultConstruction() => Construct<DefaultValue>().Value;

        private static T Construct<T>() where T : new() => new();

        public static uint StringData(string left, string right)
        {
            string combined = string.Concat(left, right);
            return (uint)combined.Length + combined[0];
        }

        public static uint Square(uint value) => value * value;

        public static uint DelegateCall(uint value)
        {
            Func<uint, uint> function = Square;
            return function(value);
        }

        public static uint BitCast(float value) => BitConverter.SingleToUInt32Bits(value);

        public static uint Cleanup(uint value)
        {
            try
            {
                if (value == 0)
                {
                    throw new InvalidOperationException("zero");
                }

                return value;
            }
            finally
            {
                value++;
            }
        }

        public static uint ArrayData(uint index)
        {
            uint[] data = [3, 7, 11, 17];
            return data[index];
        }

        public static uint HostSleep(uint value)
        {
            Thread.Sleep(1);
            return value;
        }

        public static uint Expanding(uint value) => Grow<uint>(value);

        private static uint Grow<T>(uint value) => Grow<Box<T>>(value);
    }

    private sealed class Box<T>(T value)
    {
        public readonly T Value = value;
    }

    private interface ICalculation
    {
        uint Apply(uint value);
    }

    private abstract class CalculationBase
    {
        public abstract uint Apply(uint value);
    }

    private interface IGenericCalculation<T>
    {
        T Echo(T value);
    }

    private sealed class GenericCalculation<T> : IGenericCalculation<T>
    {
        public T Echo(T value) => value;
    }

    private sealed class DefaultValue
    {
        public readonly uint Value = 19;
    }

    private sealed class Calculation(uint bias) : CalculationBase, ICalculation
    {
        private readonly uint bias = bias;

        public override uint Apply(uint value) => value + bias;
    }

    private sealed class OtherCalculation : ICalculation
    {
        public uint Apply(uint value) => value * value;
    }

    private static class FaultingInitializer
    {
        private static readonly uint value = Fail();

        private static uint Fail() => throw new InvalidOperationException("The source initializer must never execute during discovery.");

        public static uint Read() => value;
    }
}
