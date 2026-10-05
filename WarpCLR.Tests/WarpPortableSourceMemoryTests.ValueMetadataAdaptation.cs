using System.Reflection;
using System.Runtime.InteropServices;
using WarpCLR.Compiler;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSourceMemoryTests
{
    [TestMethod]
    public void RawMetadataValuePlanRetainsCanonicalExceptionAdmissionAndConstructorClosure()
    {
        MethodInfo method = typeof(ValueMetadataSources).GetMethod(nameof(ValueMetadataSources.Construct))!;
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(method);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableClosedFrameSourcePlan plan = WarpPortableClosedFrameSourcePlan.Capture(graph, typed, schema);
        Assert.IsTrue(graph.Methods.Any(candidate => candidate.SourceMethod.IsConstructor));
        Assert.AreEqual(graph.GraphHash, plan.Graph.GraphHash, StringComparer.Ordinal);
        Assert.AreEqual(typed.VerifiedHash, plan.Program.VerifiedHash, StringComparer.Ordinal);
        Assert.AreEqual(schema.SchemaHash, plan.Schema.SchemaHash, StringComparer.Ordinal);

        graph = WarpPortableMethodGraph.Discover(typeof(ValueMetadataSources).GetMethod(nameof(ValueMetadataSources.Catch))!);
        typed = WarpPortableTypedProgram.Verify(graph);
        schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        Assert.IsTrue(graph.Methods.Any(candidate => !candidate.ExceptionRegions.IsEmpty));
        WarpVerificationException denied = Assert.ThrowsExactly<WarpVerificationException>(() =>
            WarpPortableClosedFrameSourcePlan.Capture(graph, typed, schema));
        Assert.AreEqual("WRPCLR2430", denied.Code, StringComparer.Ordinal);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ValueMetadataPair
    {
        internal ValueMetadataPair(uint first, uint second) { First = first; Second = second; }
        internal uint First { get; }
        internal uint Second { get; }
    }

    private static class ValueMetadataSources
    {
        public static uint Construct(uint value)
        {
            var pair = new ValueMetadataPair(value, value + 1);
            return pair.First ^ pair.Second;
        }

        public static uint Catch(uint value)
        {
            try { return value + 1; }
            catch (InvalidOperationException) { return 2; }
        }
    }
}
