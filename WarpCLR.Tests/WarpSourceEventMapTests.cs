using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpSourceEventMapTests
{
    private static readonly int[] PackedByteOffsets = [0, 1, 3, 11];
    private static readonly int[] PackedByteSizes = [1, 2, 8, 4];
    [TestMethod]
    [DataRow(nameof(WarpSourceEventKernels.Invoke))]
    [DataRow(nameof(WarpSourceEventKernels.Indirect))]
    [DataRow(nameof(WarpSourceEventKernels.Packed))]
    public void MapCoversEverySealedOriginalInstructionExactlyOnce(string method)
    {
        WarpSourceEventFixture fixture = WarpSourceEventFixture.Capture(method);
        Assert.HasCount(fixture.Program.Bodies.Sum(body => body.SourceBlocks.Length), fixture.Map.Segments);
        foreach (WarpPortableSourceSegment segment in fixture.Map.Segments)
        {
            WarpLogicalMachineNode[] local = segment.LocalProgramCounters.Select(pc => fixture.Map.Layout.Nodes[pc]).ToArray();
            Assert.HasCount(1, local.Where(node => node.StartsBlock && node.SourceCost != 0));
            Assert.IsFalse(fixture.Map.Layout.IsRuntimeHelper(segment.Function));
            Assert.IsTrue(segment.HelperFunctions.All(fixture.Map.Layout.IsRuntimeHelper));
            Assert.AreSame(segment, fixture.Map.Find(segment.Function, segment.Block));
            Assert.AreEqual(segment.CilOffset, segment.Source.Instruction.Offset);
            Assert.AreEqual(segment.SourceOpcode, segment.Source.Instruction.OpCode);
            Assert.AreEqual(fixture.Map.Layout.GetPrivateWordCount(segment.Function), segment.Storage.PrivateWordCount);
            int offset = segment.Storage.EvaluationWordOffset;
            foreach (WarpPortableSourceOperand operand in segment.Storage.EntryOperands)
            { Assert.AreEqual(offset, operand.PrivateWordOffset); offset += operand.Value.WordCount; }
            Assert.IsLessThanOrEqualTo(segment.Storage.PrivateWordCount, offset);
        }
        WarpPortableSourceSegmentMap repeated = WarpPortableSourceSegmentMap.Capture(fixture.Graph, fixture.Schema, fixture.Program);
        Assert.AreEqual(fixture.Map.MapHash, repeated.MapHash, StringComparer.Ordinal);
    }

    [TestMethod]
    public void GuestCallCandidatesParkAfterSetupBeforeFirstOriginalInstruction()
    {
        WarpSourceEventFixture fixture = WarpSourceEventFixture.Capture();
        WarpPortableSourceSegment[] calls = fixture.Map.Segments.Where(segment => segment.SourceOpcode == OpCodes.Call.Value).Take(2).ToArray();
        Assert.HasCount(1, calls); WarpPortableSourceSegment call = calls[0];
        WarpPortableSourceSegmentFrontier[] entries = call.Frontiers.Where(frontier => frontier.Kind == WarpPortableSourceSegmentEndKind.BeforeGuestCall).ToArray();
        Assert.HasCount(1, entries);
        WarpLogicalMachineNode target = fixture.Map.Layout.Nodes[entries[0].ProgramCounter];
        Assert.AreNotEqual(call.Function, target.Function); Assert.IsTrue(target.StartsBlock);
        Assert.AreEqual(1, target.SourceCost); Assert.AreEqual(entries[0].Function, target.Function);
        Assert.IsFalse(fixture.Map.Layout.IsRuntimeHelper(target.Function));
    }

    [TestMethod]
    public void HelperInventoryIncludesPrivateOwnerValidationWithoutGuestAdmission()
    {
        WarpSourceEventFixture fixture = WarpSourceEventFixture.Capture(nameof(WarpSourceEventKernels.Indirect));
        Assert.IsTrue(fixture.Map.Segments.Any(segment => !segment.HelperFunctions.IsEmpty));
        Assert.IsTrue(fixture.Map.Segments.Any(segment => segment.RequiresArena));
    }

    [TestMethod]
    public void ForgedMapsAndCopiedSegmentRecordsFailTheCompilerSeal()
    {
        WarpSourceEventFixture fixture = WarpSourceEventFixture.Capture();
        WarpPortableWordLoweredProgram forged = fixture.Program with { MapsHash = new string('0', 64) };
        Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableSourceSegmentMap.Capture(fixture.Graph, fixture.Schema, forged));
        WarpPortableSourceSegment copied = fixture.Map.Segments[0] with { };
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Map.RequireExactSegment(copied));
    }

    [TestMethod]
    public void PackedDeclaredBytesAndOperandWidthsNeverRoundToTransportWordAlignment()
    {
        WarpSourceEventFixture fixture = WarpSourceEventFixture.Capture(nameof(WarpSourceEventKernels.Packed));
        WarpPortableTypedType packed = fixture.Program.VerifiedProgram.Types.First(type => type.Category == WarpPortableStackCategory.Value && type.ByteSize == 15);
        WarpPortableTypedField[] fields = packed.Fields.Where(field => !field.IsStatic).OrderBy(field => field.ByteOffset).ToArray();
        CollectionAssert.AreEqual(PackedByteOffsets, fields.Select(field => field.ByteOffset).ToArray());
        CollectionAssert.AreEqual(PackedByteSizes, fields.Select(field => field.ByteSize).ToArray());
        Assert.IsTrue(fixture.Map.Segments.SelectMany(segment => segment.Storage.EntryOperands).Any(operand => operand.Value.WordCount == 2));
        Assert.IsTrue(fixture.Map.Segments.Any(segment => segment.Storage.Locals.Any(slot => string.Equals(slot.Type.Identity, packed.Identity, StringComparison.Ordinal))));
    }
}
