using System.Reflection;
using System.Runtime.InteropServices;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableInitializerProjectionFixtures
{
    internal sealed record Fixture(WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed,
        WarpPortableSourceHeapSchema Schema, WarpPortableWordLoweredProgram Program,
        WarpPortableSourceInitializerExecutableProjection Projection);

    internal static Fixture Initializer(Type type, string name)
    {
        (WarpPortableMethodGraph graph, WarpPortableTypedProgram typed, WarpPortableSourceHeapSchema schema) = Source(type, name);
        WarpPortableClosedInitializerSourcePlan plan = WarpPortableClosedInitializerSourcePlan.Capture(graph, typed, schema);
        WarpPortableWordLoweredProgram lowered = WarpPortableWordLowerer.Lower(graph, typed, schema, new WarpPortableClosedInitializerSourceBinding(plan));
        return Completed(new(graph, typed, schema, lowered, WarpPortableSourceInitializerExecutableProjection.Capture(graph, schema, lowered)));
    }

    internal static Fixture Constructor(bool returnedValue)
    {
        (WarpPortableMethodGraph graph, WarpPortableTypedProgram typed, WarpPortableSourceHeapSchema schema) = Source(typeof(Entries),
            returnedValue ? nameof(Entries.MakePair) : nameof(Entries.Construct));
        WarpPortableClosedFrameSourcePlan plan = WarpPortableClosedFrameSourcePlan.Capture(graph, typed, schema);
        WarpPortableWordLoweredProgram lowered = WarpPortableWordLowerer.Lower(graph, typed, schema, new WarpPortableClosedFrameSourceBinding(plan));
        return Completed(new(graph, typed, schema, lowered, WarpPortableSourceInitializerExecutableProjection.Capture(graph, schema, lowered)));
    }

    internal static Fixture Filter()
    {
        (WarpPortableMethodGraph graph, WarpPortableTypedProgram typed, WarpPortableSourceHeapSchema schema) = Source(typeof(Entries), nameof(Entries.Filter));
        var binding = new WarpPortableExceptionSourceBinding(graph, typed, schema, controller: 1);
        WarpPortableWordLoweredProgram lowered = WarpPortableWordLowerer.Lower(graph, typed, schema, binding);
        return Completed(new(graph, typed, schema, lowered, WarpPortableSourceInitializerExecutableProjection.Capture(graph, schema, lowered)));
    }

    private static (WarpPortableMethodGraph, WarpPortableTypedProgram, WarpPortableSourceHeapSchema) Source(Type type, string name)
    {
        MethodInfo source = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(source, concreteTypes: [typeof(InvalidOperationException)]);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        return (graph, typed, WarpPortableSourceHeapSchema.Create(graph, typed));
    }

    private static Fixture Completed(Fixture fixture)
    {
        string? destination = Environment.GetEnvironmentVariable("WARPCLR_INITIALIZER_PROJECTION_WITNESSES");
        if (string.IsNullOrEmpty(destination)) { return fixture; }
        var projection = fixture.Projection;
        byte[] witness = WarpPortableSnapshotIdentity.Serialize(new
        {
            WarpPortableSourceInitializerExecutableProjection.Semantics, projection.ProjectionHash,
            fixture.Graph.GraphHash, fixture.Typed.VerifiedHash, fixture.Schema.SchemaHash,
            projection.ProgramIdentity.IdentityHash, projection.ProgramIdentity.KernelIrHash,
            projection.ProgramIdentity.StructuralLayoutHash, projection.ProgramIdentity.MapsHash,
            LogicalVersion = fixture.Program.Kernel.Execution!.IdentityVersion,
            SourceSteps = projection.SourceSteps.Select(step => new
            {
                step.MethodIdentity, step.CapturedMethodIndex, step.Function, step.OriginalFunction,
                step.EntryProgramCounter, step.SourceOffset, step.SourceOpCode, step.Instruction.Effects,
                step.Instruction.ExceptionMemberships, step.Source.Roots, step.Source.ReturnedRoots,
                Nodes = step.Points.Select(point => new { point.ProgramCounter, point.Block, point.StartsBlock, point.SourceCost, point.ChargedSourceSteps, point.NodeHash }),
            }),
            Sites = projection.Sites.Select(site => new
            {
                site.SiteHash, site.Origin.Kind, site.Origin.OriginHash, site.Origin.SourceOffset, site.Origin.SourceOpCode, site.Origin.EffectIndex,
                site.CapturedMethodIndex, site.Function, site.InitializerCapturedMethodIndex, site.InitializerFunction,
                site.EntryProgramCounter, PreludeNodes = site.Prelude is null ? Array.Empty<int>() : site.Points.Select(point => point.ProgramCounter).ToArray(),
                Candidate = projection.Describe(site).CandidateHash,
            }),
            RequiresOpaqueRuntimeReceipts = true, NativeExecution = false,
        });
        Directory.CreateDirectory(destination);
        string path = Path.Combine(destination, projection.ProjectionHash + ".json");
        if (File.Exists(path)) { CollectionAssert.AreEqual(File.ReadAllBytes(path), witness); }
        else { File.WriteAllBytes(path, witness); }
        return fixture;
    }

    internal static class Strict
    {
        private static readonly uint Value = unchecked((uint)BitConverter.SingleToInt32Bits(BitConverter.Int32BitsToSingle(37)));
        static Strict() { OriginalCall(); }
        private static void OriginalCall() { }
        public static uint Read() => Value + AddFive();
        private static uint AddFive() => 5;
    }

    internal static class Relaxed
    {
        private static readonly uint Value = Seed();
        private static uint Seed() => 17;
        public static uint Read() => Value;
    }

    internal static class ProtectedInitializer
    {
        private static readonly uint Value = Seed();
        private static uint Seed() => 31;
        static ProtectedInitializer() { OriginalCall(); }
        private static void OriginalCall() { }
        public static uint Read()
        {
            try { return Value; }
            catch (TypeInitializationException) { return 0; }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Pair
    {
        public uint First;
        public uint Second;
        public Pair(uint value) { First = value; Second = 5; }
    }

    internal static class Entries
    {
        public static uint RelaxedRead() => Relaxed.Read();
        public static uint Construct(uint value) { Pair pair = new(value); return pair.First + pair.Second; }
        public static Pair MakePair(uint value) => new(value);
        public static uint Filter(Exception error, uint decision)
        {
            try { throw error; }
            catch (InvalidOperationException) when (Accept(decision)) { return 31; }
            catch (InvalidOperationException) { return 32; }
        }
        private static bool Accept(uint decision) => decision != 0;
    }
}
