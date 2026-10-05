using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableSourceFaultFactoryCases
{
    internal static void CapturedFaultResourcesBindExactCorelibCultureAndUtf16Data()
    {
        string[] keys = ["Arg_NullReferenceException", "Arg_OverflowException", "Arg_NullReferenceException"];
        WarpPortableSourceExceptionResources first = WarpPortableSourceExceptionResources.Capture(keys);
        WarpPortableSourceExceptionResources second = WarpPortableSourceExceptionResources.Capture(keys.Reverse());
        Assert.HasCount(2, first.Resources); Assert.AreEqual(first.ContractHash, second.ContractHash, StringComparer.Ordinal);
        Assert.AreEqual(64, first.CorelibHash.Length);
        // Standard BCL exceptions are reference oracles; capture invokes no source exception constructor.
        Assert.AreEqual(Activator.CreateInstance<NullReferenceException>().Message, first.Text(keys[0]), StringComparer.Ordinal);
        Assert.AreEqual(new OverflowException().Message, first.Text(keys[1]), StringComparer.Ordinal);
        Assert.AreEqual(System.Globalization.CultureInfo.CurrentUICulture.Name, first.UiCulture, StringComparer.Ordinal);
    }

    internal static void MissingResourcesAndOtherTypedClosuresCannotGrantAFaultContract()
    {
        WarpVerificationException missing = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableSourceExceptionResources.Capture(["WarpCLR_Does_Not_Exist"]));
        Assert.AreEqual("WRPCLR2420", missing.Code, StringComparer.Ordinal);
        Fixture divide = Capture(nameof(Source.Divide)), round = Capture(nameof(Source.Round));
        WarpVerificationException mismatch = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableSourceFaultFactoryContract.CaptureFinite(divide.Graph, round.Typed, divide.Schema));
        Assert.AreEqual("WRPCLR2420", mismatch.Code, StringComparer.Ordinal);
    }

    internal static void FiniteIntegerFaultRowsPreserveOriginalOpcodeEffectAndExceptionType()
    {
        Fixture fixture = Capture(nameof(Source.Divide));
        WarpPortableSourceFaultFactoryContract contract = WarpPortableSourceFaultFactoryContract.CaptureFinite(fixture.Graph, fixture.Typed, fixture.Schema);
        WarpPortableSourceFaultFactoryRow row = contract.Rows.First(item => item.Kind == WarpPortableTypedFaultKind.DivideByZero);
        WarpPortableTypedInstruction divide = fixture.Typed.Methods.First(method => string.Equals(method.Identity, fixture.Graph.EntryIdentity, StringComparison.Ordinal))
            .Instructions.First(instruction => instruction.OpCode == OpCodes.Div.Value);
        Assert.AreEqual(fixture.Graph.EntryIdentity, row.MethodIdentity, StringComparer.Ordinal);
        Assert.AreEqual(divide.Offset, row.SourceOffset); Assert.AreEqual(divide.OpCode, row.OpCode);
        Assert.AreEqual(divide.Faults.First(fault => fault.Kind == WarpPortableTypedFaultKind.DivideByZero).EffectIndex, row.EffectIndex);
        Assert.AreEqual(fixture.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(DivideByZeroException))), row.ExceptionType);
        Assert.AreEqual("Arg_DivideByZero", row.ResourceKey, StringComparer.Ordinal);
        Assert.AreEqual(new DivideByZeroException().Message, contract.Resources.Text(row.ResourceKey), StringComparer.Ordinal);
        Assert.AreEqual(contract.ContractHash, WarpPortableSourceFaultFactoryContract.CaptureFinite(fixture.Graph, fixture.Typed, fixture.Schema).ContractHash, StringComparer.Ordinal);
    }

    internal static void MathFactoryRowsKeepOperationSpecificDescriptorsAndDeclaredParameters()
    {
        Fixture round = Capture(nameof(Source.Round));
        WarpPortableSourceFaultFactoryContract roundContract = WarpPortableSourceFaultFactoryContract.CaptureFinite(round.Graph, round.Typed, round.Schema);
        WarpPortableSourceFaultFactoryRow digits = roundContract.Rows.First(row => row.FaultDescriptor == 2);
        Assert.AreEqual("digits", digits.ParamName, StringComparer.Ordinal);
        Assert.AreEqual("ArgumentOutOfRange_RoundingDigits_MathF", digits.ResourceKey, StringComparer.Ordinal);
        Assert.AreEqual(round.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(ArgumentOutOfRangeException))), digits.ExceptionType);
        Assert.IsTrue(digits.OperationIdentity.Contains("Round", StringComparison.OrdinalIgnoreCase));
        // The mode fault requires its separately bound formatting factory, so no default-data row is admitted.
        Assert.IsFalse(roundContract.Rows.Any(row => row.FaultDescriptor == 3));
        Fixture abs = Capture(nameof(Source.Abs));
        WarpPortableSourceFaultFactoryRow overflow = WarpPortableSourceFaultFactoryContract.CaptureFinite(abs.Graph, abs.Typed, abs.Schema)
            .Rows.First(row => row.FaultDescriptor == 2);
        Assert.AreEqual("Overflow_NegateTwosCompNum", overflow.ResourceKey, StringComparer.Ordinal);
        Assert.IsNull(overflow.ParamName);
    }

    internal static void ConstructorPlanningRetainsExactPrivateOwnerFieldsWithoutSourceExecution()
    {
        Fixture reference = Capture(nameof(Source.MakeReference));
        WarpPortableWordBody body = WarpPortableWordLowerer.PlanPrivateStorage(reference.Graph, reference.Typed, reference.Graph.EntryIdentity, 1);
        Assert.HasCount(1, body.PrivateTemporaries); WarpPortableWordPrivateTemporary temporary = body.PrivateTemporaries[0];
        Assert.AreEqual(body.EvaluationWordOffset, body.StoragePrefixWords);
        Assert.IsGreaterThanOrEqualTo(body.EvaluationWordOffset + body.MaximumStackWords, temporary.WordOffset);
        Assert.AreEqual(3, temporary.Type.WordCount); Assert.HasCount(1, temporary.Owners);
        WarpPortableWordTemporaryOwner owner = temporary.Owners[0];
        Assert.AreEqual(temporary.WordOffset, owner.PrivateWordOffset); Assert.AreEqual(0, owner.RelativeByteOffset);
        Assert.AreEqual(temporary.Type.Identity, owner.TypeIdentity, StringComparer.Ordinal); Assert.IsEmpty(owner.FieldPath); Assert.IsEmpty(owner.Provenance);
        foreach (WarpPortableWordSourceBlock block in body.SourceBlocks)
        {
            Assert.IsTrue(block.Roots.Any(root => root.PrivateWordOffset == owner.PrivateWordOffset && string.Equals(root.Source.Storage, "temporary", StringComparison.Ordinal)));
        }
        Assert.AreEqual(0u, Probe.ConstructorCalls); Assert.AreEqual(0u, Probe.InitializerCalls);
        WarpVerificationException unbound = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(reference.Graph, reference.Typed));
        Assert.AreEqual("WRPCLR2300", unbound.Code, StringComparer.Ordinal);
    }

    internal static void EmbeddedValueConstructorOwnersBindLeafTypesPathsAndMapIdentity()
    {
        Fixture fixture = Capture(nameof(Source.MakeValue));
        WarpPortableWordBody body = WarpPortableWordLowerer.PlanPrivateStorage(fixture.Graph, fixture.Typed, fixture.Graph.EntryIdentity, 1);
        WarpPortableWordPrivateTemporary temporary = body.PrivateTemporaries.First();
        Assert.AreEqual(WarpPortableStackCategory.Value, temporary.Type.Category); Assert.HasCount(2, temporary.Owners);
        foreach (WarpPortableWordTemporaryOwner owner in temporary.Owners)
        {
            Assert.AreEqual(temporary.WordOffset + owner.RelativeByteOffset / 4, owner.PrivateWordOffset);
            Assert.Contains(owner.RelativeByteOffset, temporary.Type.ManagedRootByteOffsets);
            Assert.IsGreaterThan(0, owner.FieldPath.Length);
            Assert.AreEqual(WarpPortableStackCategory.Reference, fixture.Typed.Types.First(type => string.Equals(type.Identity, owner.TypeIdentity, StringComparison.Ordinal)).Category);
        }
        Assert.IsTrue(body.SourceBlocks.First(block => block.Instruction.Offset == temporary.SourceOffset).Operation.RequiresLease);
        var entry = new WarpPortableWordEntryProjection(fixture.Typed.Types.First(type => string.Equals(type.Identity, fixture.Typed.Methods.First(method => string.Equals(method.Identity, fixture.Graph.EntryIdentity, StringComparison.Ordinal)).ReturnType, StringComparison.Ordinal)), [], [], []);
        string original = WarpPortableWordMapHash.Compute([body], entry);
        WarpPortableWordBody changed = body with { PrivateTemporaries = [temporary with { Owners = [] }] };
        Assert.AreNotEqual(original, WarpPortableWordMapHash.Compute([changed], entry), StringComparer.Ordinal);
        Assert.AreEqual(0u, Probe.ConstructorCalls);
    }

    internal static void RegisteredFlattenedTupleConstructionDoesNotReserveUnusedObjectStorage()
    {
        Fixture fixture = Capture(nameof(Source.Tuple));
        WarpPortableWordLoweredProgram lowered = WarpPortableWordLowerer.Lower(fixture.Graph, fixture.Typed);
        Assert.IsTrue(lowered.Bodies.All(body => body.PrivateTemporaries.IsEmpty));
        WarpPortableWordProgramIdentity.Validate(fixture.Graph, fixture.Schema, lowered);
    }

    private static Fixture Capture(string method)
    {
        MethodInfo source = typeof(Source).GetMethod(method)!;
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source); WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        return new(graph, typed, WarpPortableSourceHeapSchema.Create(graph, typed));
    }

    private sealed record Fixture(WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed, WarpPortableSourceHeapSchema Schema);
    private static class Probe { public static uint ConstructorCalls; public static uint InitializerCalls; }
    private sealed class Constructed
    {
        public int Value;
        static Constructed() => Probe.InitializerCalls++;
        public Constructed(int value) { Probe.ConstructorCalls++; Value = value; }
    }
    private struct Nested { public string? Text; public int Number; }
    private struct ConstructedValue
    {
        public byte Tag;
        public Nested Inner;
        public object? Related;
        public ConstructedValue(int value) { Tag = 255; Inner.Text = null; Inner.Number = value; Related = null; Probe.ConstructorCalls++; }
    }
    private static class Source
    {
        public static int Divide(int left, int right) => left / right;
        public static float Round(float value, int digits, MidpointRounding mode) => MathF.Round(value, digits, mode);
        public static long Abs(long value) => Math.Abs(value);
        public static Constructed MakeReference(int value) => new(value);
        public static ConstructedValue MakeValue(int value) => new(value);
        public static (int, object?) Tuple(int value, object? reference) => (value, reference);
    }
}
