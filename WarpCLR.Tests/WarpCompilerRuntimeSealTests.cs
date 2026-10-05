using System.Diagnostics.CodeAnalysis;
using WarpCLR.Compiler;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest constructs this internal fixture through reflected discovery under DiscoverInternals.")]
internal sealed class WarpCompilerRuntimeSealTests
{
    [TestMethod]
    public void IdenticalMetadataCloneCannotClaimTheActualCompilerIssuedProgram()
    {
        WarpPortableMethodGraph graph = Capture();
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableWordLoweredProgram original = WarpPortableWordLowerer.Lower(graph, typed);
        WarpPortableWordLoweredProgram clone = original with { };
        Assert.AreSame(original.CompilerIdentity, WarpPortableWordProgramIdentity.Validate(graph, schema, clone));
        Assert.AreSame(original.CompilerIdentity, WarpPortableWordLowerer.RequireRuntimeCompilerSeal(graph, schema, original));
        WarpVerificationException failure = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableWordLowerer.RequireRuntimeCompilerSeal(graph, schema, clone));
        Assert.AreEqual("WRPCLR2350", failure.Code, StringComparer.Ordinal);
    }

    [TestMethod]
    public void SeparatelyCapturedIdenticalClosureCannotReplaceTheIssuedClosureObject()
    {
        WarpPortableMethodGraph graph = Capture();
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableWordLoweredProgram original = WarpPortableWordLowerer.Lower(graph, typed);
        WarpPortableMethodGraph duplicate = Capture();
        Assert.AreEqual(graph.GraphHash, duplicate.GraphHash, StringComparer.Ordinal);
        Assert.AreSame(original.CompilerIdentity, WarpPortableWordProgramIdentity.Validate(duplicate, schema, original));
        Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.RequireRuntimeCompilerSeal(duplicate, schema, original));
        Assert.AreSame(original.CompilerIdentity, WarpPortableWordLowerer.RequireRuntimeCompilerSeal(graph, schema, original));
    }

    [TestMethod]
    public void ActualHostPlanRejectsIdentityCloneBeforePreparingServices()
    {
        WarpPortableMethodGraph graph = Capture();
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableWordLoweredProgram original = WarpPortableWordLowerer.Lower(graph, typed);
        WarpPortableWordLoweredProgram clone = original with { };
        Assert.AreSame(original.CompilerIdentity, WarpPortableWordProgramIdentity.Validate(graph, schema, clone));
        Assert.ThrowsExactly<WarpVerificationException>(() => new WarpCompiledSourcePlan(graph, schema, clone, 1, 1, 8, 10000, 4096, null!));
    }

    private static WarpPortableMethodGraph Capture() => WarpPortableMethodGraph.Discover(typeof(Kernels).GetMethod(nameof(Kernels.Echo))!);

    private static class Kernels
    {
        public static uint Echo(uint value) => value;
    }
}
