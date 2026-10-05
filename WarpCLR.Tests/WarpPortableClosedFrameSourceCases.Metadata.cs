using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableClosedFrameSourceCases
{
    internal static void ActualReferenceContainingValueConstructorCopiesAndClearsExactOwnerFields()
    {
        Fixture fixture = Capture(nameof(Kernels.MakeReference));
        uint[] arena = fixture.Schema.CreateArena(791, 512, 16, 16, 2, 512);
        uint objectType = fixture.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(object)));
        Assert.AreEqual(0u, WarpPortableSourceFaultPoolCases.Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, WarpPortableSourceFaultPoolCases.Service(nameof(WarpPortableHeapServices.AllocateObject), arena, [objectType]));
        uint[] owner = arena.Skip((int)WarpPortableHeapLayout.Result).Take(3).ToArray();
        WarpPortableWordBody body = fixture.Program.Bodies.First(item => string.Equals(item.MethodIdentity, fixture.Graph.EntryIdentity, StringComparison.Ordinal));
        WarpPortableWordPrivateTemporary temporary = Only(body.PrivateTemporaries);
        WarpPortableWordTemporaryOwner root = Only(temporary.Owners);
        Assert.AreEqual(4, root.RelativeByteOffset);
        uint[] result = Execute(fixture, [.. owner, 0xABCDEF01, 0xFEDCBA98], completed: state =>
        {
            int sourceFrame = WarpLogicalMachineLayout.HeaderWords + fixture.Layout.FrameWords;
            int offset = sourceFrame + fixture.Layout.PrivateOffset + root.PrivateWordOffset;
            Assert.AreEqual(0u, state[offset] | state[offset + 1] | state[offset + 2]);
        });
        Assert.AreEqual(0x5Au, result[0]); CollectionAssert.AreEqual(owner, result.Skip(1).Take(3).ToArray());
        Assert.AreEqual(0xABCDEF01u, result[4]); Assert.AreEqual(0xFEDCBA98u, result[5]);
        Assert.Contains(root.RelativeByteOffset, temporary.Type.ManagedRootByteOffsets);
        Assert.IsTrue(fixture.Program.EntryProjection.ResultRoots.Any(item => item.ResultWordOffset == 1));
        Assert.AreEqual(0u, WarpPortableSourceFaultPoolCases.Service(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, WarpPortableSourceFaultPoolCases.Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    }

    internal static void ConstructorAndMemoryHelpersHaveExactOriginalSourceChargesAndRootMaps()
    {
        Fixture fixture = Capture(nameof(Kernels.MakeReference));
        int instructions = fixture.Program.Bodies.Sum(body => body.SourceBlocks.Length);
        Execute(fixture, [0, 0, 0, 17, 0], instructions, completed: state =>
        {
            Assert.AreEqual(0u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
            Assert.AreEqual(0u, state[WarpLogicalMachineLayout.RemainingStepsHighOffset]);
        });
        foreach (WarpPortableWordBody body in fixture.Program.Bodies)
        {
            WarpLogicalBodyMetadata metadata = fixture.Program.Kernel.Execution!.Bodies[body.Function];
            foreach (WarpPortableWordSourceBlock source in body.SourceBlocks)
            {
                Assert.AreEqual(1, metadata.SourceBlockCosts[source.Block]);
                foreach (int continuation in source.GeneratedBlocks.Where(block => block != source.Block)) { Assert.AreEqual(0, metadata.SourceBlockCosts[continuation]); }
                foreach (WarpPortableWordTemporaryOwner owner in body.PrivateTemporaries.SelectMany(temporary => temporary.Owners))
                {
                    Assert.IsTrue(source.Roots.Any(root => root.PrivateWordOffset == owner.PrivateWordOffset));
                }
            }
        }
        Assert.IsTrue(fixture.Program.Kernel.Execution!.Bodies.Where(body => body.RuntimeHelper).All(body => body.SourceBlockCosts.All(cost => cost == 0)));
        WarpPortableWordSourceBlock construction = Only(fixture.Program.Bodies.SelectMany(body => body.SourceBlocks).Where(block => block.Instruction.OpCode == OpCodes.Newobj.Value));
        Assert.IsTrue(construction.Operation!.RequiresLease); Assert.IsTrue(construction.Operation.WritesOwnerReferences);
        WarpPortableWordProgramIdentity identity = WarpPortableWordProgramIdentity.Validate(fixture.Graph, fixture.Schema, fixture.Program);
        Assert.AreEqual(fixture.Program.ExecutionPlanHash, identity.ExecutionPlanHash, StringComparer.Ordinal);
    }

    internal static void FrameSourceBindingCannotGrantHeapInitializersExceptionsOrExternalByrefs()
    {
        foreach (string name in new[] { nameof(Kernels.ExternalByref), nameof(Kernels.HeapConstruction), nameof(Kernels.Checked), nameof(Kernels.Divide), nameof(Kernels.WithInitializer) })
        {
            WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Kernels).GetMethod(name)!);
            WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
            WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
            WarpVerificationException rejection = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableClosedFrameSourcePlan.Capture(graph, typed, schema));
            Assert.AreEqual("WRPCLR2430", rejection.Code, StringComparer.Ordinal);
            Assert.IsGreaterThanOrEqualTo(0, rejection.IlOffset.GetValueOrDefault(-1));
        }
        Fixture supported = Capture(nameof(Kernels.MakePacked));
        WarpVerificationException unbound = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordLowerer.Lower(supported.Graph, supported.Typed));
        Assert.AreEqual("WRPCLR2300", unbound.Code, StringComparer.Ordinal);
        WarpVerificationException noSourceMemory = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableClosedFrameSourcePlan.Capture(supported.Graph,
            supported.Typed, Capture(nameof(Kernels.RecursiveOwners)).Schema));
        Assert.AreEqual("WRPCLR2430", noSourceMemory.Code, StringComparer.Ordinal);
    }

    internal static void ChangedFramePlansTemporaryOwnersAndClosuresCannotReuseCompilerIdentity()
    {
        Fixture fixture = Capture(nameof(Kernels.MakeReference));
        WarpPortableWordBody body = fixture.Program.Bodies.First(item => string.Equals(item.MethodIdentity, fixture.Graph.EntryIdentity, StringComparison.Ordinal));
        WarpPortableWordPrivateTemporary temporary = Only(body.PrivateTemporaries);
        WarpPortableWordPrivateTemporary wrong = temporary with { Owners = temporary.Owners.SetItem(0, temporary.Owners[0] with { PrivateWordOffset = temporary.Owners[0].PrivateWordOffset + 1 }) };
        WarpPortableWordBody changed = body with { PrivateTemporaries = [wrong] };
        int index = fixture.Program.Bodies.IndexOf(body);
        WarpVerificationException rejection = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableWordProgramIdentity.Validate(fixture.Graph,
            fixture.Schema, fixture.Program with { Bodies = fixture.Program.Bodies.SetItem(index, changed) }));
        Assert.AreEqual("WRPCLR2350", rejection.Code, StringComparer.Ordinal);
        WarpPortableSourceFrameSchema frames = WarpPortableSourceFrameSchema.Create(fixture.Schema, fixture.Program);
        WarpPortableSourceFrameBody planned = WarpPortableSourceFrameSchema.DescribePlannedBody(fixture.Schema, fixture.Typed, body);
        CollectionAssert.AreEqual(planned.Views.ToArray(), frames.Bodies.First(item => item.Function == body.Function).Views.ToArray());
        Assert.IsTrue(planned.Views.Any(view => string.Equals(view.Storage, "temporary", StringComparison.Ordinal) && view.ElementType == fixture.Schema.TypeId(temporary.Type.Identity)));
    }
}
