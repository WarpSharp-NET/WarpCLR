using System.Reflection.Emit;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableClosedFrameSourceCases
{
    internal sealed record CorruptOwnerSeed(uint[] State, uint[] Arguments, int PayloadByteOffset,
        byte[] PayloadBefore, int Function, int Block);

    internal static CorruptOwnerSeed SeedCorruptOwner(Fixture fixture, int corruption)
    {
        WarpPortableMethodGraphMethod source = fixture.Graph.Methods.First(method => string.Equals(method.SourceMethod.Name, "SetWide", StringComparison.Ordinal));
        WarpPortableWordBody body = fixture.Program.Bodies.First(item => string.Equals(item.MethodIdentity, source.Identity, StringComparison.Ordinal));
        WarpPortableWordSourceBlock target = Only(body.SourceBlocks.Where(block => block.Instruction.OpCode == OpCodes.Stind_I8.Value));
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(fixture.Layout);
        uint[] state = fixture.Layout.CreateInitialState(64, 100000); uint[] arguments = [0x81234567];
        fixture.Layout.SetSourceBoundaryMode(state, true);
        for (int attempt = 0; attempt < 100000 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; attempt++)
        {
            core.ExecuteQuantum(arguments.Select(word => new[] { word }).ToArray(), [], 0, state, 64, fixture.Layout.MaximumBlockCost);
            if (state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.BeforeSourceBoundary) { continue; }
            int frame = WarpLogicalMachineLayout.HeaderWords + ((int)state[WarpLogicalMachineLayout.DepthOffset] - 1) * fixture.Layout.FrameWords;
            if (state[frame + WarpLogicalMachineLayout.FrameFunctionOffset] == body.Function &&
                state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset] == fixture.Layout.GetBlockEntry(body.Function, target.Block))
            {
                return CorruptAtBoundary(fixture, state, arguments, frame, body, target, corruption);
            }
            WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state);
        }
        Assert.Fail("The actual source byref store boundary was not reached."); throw new InvalidOperationException();
    }

    private static CorruptOwnerSeed CorruptAtBoundary(Fixture fixture, uint[] state, uint[] arguments, int frame,
        WarpPortableWordBody body, WarpPortableWordSourceBlock target, int corruption)
    {
        int address = frame + fixture.Layout.PrivateOffset + body.EvaluationWordOffset;
        int payload = (WarpLogicalMachineLayout.HeaderWords + ((int)state[address + 1] - 1) * fixture.Layout.FrameWords + fixture.Layout.PrivateOffset) * 4 + (int)state[address + 3];
        byte[] before = ReadBytes(state, payload, 8);
        switch (corruption)
        {
            case 0: state[address] ^= 0x10000; break;
            case 1: state[address + 2]++; break;
            case 2: state[address + 3]++; break;
            case 3: state[address + 4] = 7; break;
            case 4: state[address + 5] = fixture.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(uint))); break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state);
        fixture.Layout.SetSourceBoundaryMode(state, false);
        return new(state, arguments, payload, before, body.Function, target.Block);
    }

    internal static void CorruptPrivateSourceOwnersFailBeforeWritingAnyPackedPayloadByte()
    {
        Fixture fixture = Capture(nameof(Kernels.MutatePacked)); CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(fixture.Layout);
        for (int corruption = 0; corruption < 5; corruption++)
        {
            CorruptOwnerSeed seed = SeedCorruptOwner(fixture, corruption);
            for (int attempt = 0; attempt < 100000 && seed.State[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; attempt++)
            {
                core.ExecuteQuantum(seed.Arguments.Select(word => new[] { word }).ToArray(), [], 0, seed.State, 64, fixture.Layout.MaximumBlockCost);
            }
            Assert.AreEqual(WarpLogicalMachineLayout.Faulted, seed.State[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(3u, seed.State[WarpLogicalMachineLayout.FaultKindOffset]);
            Assert.AreEqual((uint)seed.Function, seed.State[WarpLogicalMachineLayout.FaultFunctionOffset]);
            Assert.AreEqual((uint)seed.Block, seed.State[WarpLogicalMachineLayout.FaultBlockOffset]);
            CollectionAssert.AreEqual(seed.PayloadBefore, ReadBytes(seed.State, seed.PayloadByteOffset, 8));
        }
    }

    internal static void ActualBooleanConstructorStoresTheDeclaredByteWithoutNormalizingTruth()
    {
        Fixture fixture = Capture(nameof(Kernels.MakeBoolean));
        foreach (uint value in new uint[] { 0, 1, 2, 255, 256, 258, uint.MaxValue })
        {
            uint[] result = Execute(fixture, [value, 0xA5]); Assert.AreEqual(0xA500u | (value & 255), result[0]);
        }
    }

    internal static void OrdinaryInstanceAndClosedGenericByrefCallsExecuteCapturedSource()
    {
        Fixture fixture = Capture(nameof(Kernels.InstanceAndGeneric));
        foreach (uint low in new uint[] { 0, 0x81234567, uint.MaxValue })
        {
            ulong expected = (0x0123456700000000ul | low) ^ 0x5500000000000000ul;
            CollectionAssert.AreEqual(new uint[] { (uint)expected, (uint)(expected >> 32) }, Execute(fixture, [low]));
        }
        Assert.IsTrue(fixture.Graph.Methods.Any(method => method.SourceMethod.IsGenericMethod && !method.SourceMethod.ContainsGenericParameters));
        Assert.IsTrue(fixture.Graph.Methods.Any(method => !method.SourceMethod.IsStatic && !method.SourceMethod.IsConstructor && method.Intrinsic is null));
    }

    internal static void CalleeCanInstallAReferenceInAnInitiallyNullCallerFieldUnderFixedRootMaps()
    {
        Fixture fixture = Capture(nameof(Kernels.InstallReference));
        uint[] arena = fixture.Schema.CreateArena(797, 512, 16, 16, 2, 512);
        Assert.AreEqual(0u, WarpPortableSourceFaultPoolCases.Service(nameof(WarpPortableHeapServices.AcquireServiceLease), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, WarpPortableSourceFaultPoolCases.Service(nameof(WarpPortableHeapServices.AllocateObject), arena,
            [fixture.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(object)))]));
        uint[] owner = arena.Skip((int)WarpPortableHeapLayout.Result).Take(3).ToArray();
        uint[] result = Execute(fixture, owner); Assert.AreEqual(0u, result[0]);
        CollectionAssert.AreEqual(owner, result.Skip(1).Take(3).ToArray()); Assert.AreEqual(0u, result[4] | result[5]);
        WarpPortableWordBody root = fixture.Program.Bodies.First(body => string.Equals(body.MethodIdentity, fixture.Graph.EntryIdentity, StringComparison.Ordinal));
        WarpPortableWordStorageSlot local = root.Locals.First(slot => slot.Type.ManagedRootByteOffsets.Length == 1);
        int offset = local.WordOffset + local.Type.ManagedRootByteOffsets[0] / 4;
        Assert.IsTrue(root.SourceBlocks.All(block => block.Roots.Any(item => item.PrivateWordOffset == offset)));
        Assert.IsTrue(fixture.Program.Bodies.SelectMany(body => body.SourceBlocks).Any(block => block.Operation.WritesOwnerReferences && block.Operation.MayTargetCallerFrame));
        Assert.AreEqual(0u, WarpPortableSourceFaultPoolCases.Service(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), arena, [uint.MaxValue]));
        Assert.AreEqual(0u, WarpPortableSourceFaultPoolCases.Service(nameof(WarpPortableHeapServices.ReleaseServiceLease), arena, [uint.MaxValue]));
    }

    private static byte[] ReadBytes(uint[] words, int offset, int count) => Enumerable.Range(offset, count)
        .Select(index => unchecked((byte)(words[index / 4] >> (index % 4 * 8)))).ToArray();
}
