using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    public void ComposableEhInstructionOwnershipBindsExactSitesEffectsAndThrowProvenanceWithoutGrantingNewobj()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(WarpPortableExceptionConstructorSources)
            .GetMethod(nameof(WarpPortableExceptionConstructorSources.FailedValue))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        var binding = new WarpPortableExceptionSourceBinding(graph, typed, schema, WarpPortableExceptionTestBuilder.Controller);
        Assert.IsGreaterThan(0, binding.InstructionOwnership.Length);
        foreach (WarpPortableExceptionInstructionOwnership row in binding.InstructionOwnership)
        {
            WarpPortableTypedInstruction instruction = typed.Methods.First(method => string.Equals(method.Identity, row.MethodIdentity, StringComparison.Ordinal))
                .Instructions.First(instruction => instruction.Offset == row.SourceOffset);
            Assert.IsTrue(binding.OwnsInstruction(row.MethodIdentity, instruction));
            CollectionAssert.AreEqual(instruction.Effects.ToArray(), row.Effects.ToArray());
            Assert.IsFalse(binding.OwnsInstruction(row.MethodIdentity, instruction with { Offset = instruction.Offset + 1 }));
            Assert.IsFalse(binding.OwnsInstruction(row.MethodIdentity, instruction with { Effects = instruction.Effects.Add(WarpPortableTypedEffect.NullCheck) }));
            if (row.SourceOpCode == unchecked((ushort)OpCodes.Throw.Value))
            {
                Assert.IsTrue(row.RequiresNonNullPreparedException);
                Assert.AreEqual(instruction.EntryStack[^1], row.ThrowOperand);
            }
        }
        WarpPortableTypedMethod caller = typed.Methods.First(method => string.Equals(method.Identity, graph.EntryIdentity, StringComparison.Ordinal));
        Assert.IsFalse(binding.OwnsInstruction(caller.Identity, caller.Instructions.First(instruction => instruction.OpCode == OpCodes.Newobj.Value)));
        Assert.AreEqual(64, binding.InstructionOwnershipHash.Length);
        Assert.AreEqual(binding.InstructionOwnershipHash, new WarpPortableExceptionSourceBinding(graph, typed, schema, WarpPortableExceptionTestBuilder.Controller)
            .InstructionOwnershipHash, StringComparer.Ordinal);
    }
}
