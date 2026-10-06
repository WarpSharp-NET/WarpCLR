using System.Security.Cryptography;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualObservedCallUsesKeepEveryOrdinaryResultWordAtBothQuanta(bool largeQuantum)
    {
        var layout = ObservationLayout();
        CoreCLRResumableKernel kernel = CoreCLRResumableKernel.Compile(layout);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        uint[][] inputs = [[31]]; uint[] scalars = []; uint[] arena = [];
        uint[] state = layout.CreateInitialState(32, 100000);
        uint[] expected = (uint[])state.Clone();
        int quantum = largeQuantum ? Math.Max(4096, layout.MaximumBlockCost) : layout.MaximumBlockCost;
        int observed = 0; ulong sequence = 9;
        while (state[0] == WarpLogicalMachineLayout.Runnable)
        {
            byte[] request = WarpCoreCLRWorkerWords.Request(inputs, scalars, 0, state, 32, quantum, arena);
            WarpCoreCLRWorkerProtocol.Frame frame = await ObservationFrameAsync(key, sequence, request).ConfigureAwait(false);
            byte[] response;
            using (CoreCLRCallUseObservation.Scope scope = CoreCLRCallUseObservation.Begin(frame, key, sequence, kernel))
            {
                WarpCoreCLRWorkerWords.Invocation call = scope.Invocation;
                kernel.ExecuteManagedQuantum(call.Inputs, call.Scalars, call.Worker, call.State, call.Depth, call.Quantum, call.Arena);
                response = scope.Complete();
            }
            WarpCoreCLRWorkerCallObservations.Result result = WarpCoreCLRWorkerCallObservations.ReadResponse(response, request,
                state.Length, arena.Length, kernel.CompiledEntryPoint.Module.ModuleVersionId);
            kernel.ExecuteManagedQuantum(inputs, scalars, 0, expected, 32, quantum, arena);
            CollectionAssert.AreEqual(expected, result.State);
            CollectionAssert.AreEqual(arena, result.Arena);
            foreach (WarpCoreCLRWorkerCallObservations.CallUse use in result.Calls)
            {
                WarpLogicalMachineNode node = layout.Nodes[use.ProgramCounter];
                Assert.AreEqual(node.Function, use.Function); Assert.AreEqual(node.Call!.Value.Callee + 1, use.Callee);
                Assert.AreEqual(node.Continuation, use.Continuation); Assert.AreNotEqual(0u, use.Activation);
                Assert.AreEqual(checked(WarpLogicalMachineLayout.HeaderWords + ((int)use.Depth - 1) * layout.FrameWords), use.Frame);
            }
            observed += result.Calls.Length; state = result.State; sequence++;
        }
        Assert.IsGreaterThan(0, observed);
        Assert.AreEqual(TestKernels.Call(31), state[WarpLogicalMachineLayout.ResultOffset]);
    }

    [TestMethod]
    [DataRow("constructed")]
    [DataRow("clone")]
    [DataRow("key-copy")]
    [DataRow("payload-change")]
    [DataRow("sequence")]
    [DataRow("ordinary-kind")]
    public async Task ObservationRequiresTheActualReadFrameSessionKindSequenceAndWholePayload(string alteration)
    {
        var layout = ObservationLayout(); CoreCLRResumableKernel kernel = CoreCLRResumableKernel.Compile(layout);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] request = WarpCoreCLRWorkerWords.Request([[7]], [], 0, layout.CreateInitialState(32, 100), 32,
            layout.MaximumBlockCost, []);
        ushort kind = string.Equals(alteration, "ordinary-kind", StringComparison.Ordinal) ? WarpCoreCLRWorkerProtocol.Execute : WarpCoreCLRWorkerCallObservations.ExecuteObserved;
        WarpCoreCLRWorkerProtocol.Frame frame = await ObservationFrameAsync(key, 9, request, kind).ConfigureAwait(false);
        if (string.Equals(alteration, "constructed", StringComparison.Ordinal)) { frame = new(frame.Kind, (byte[])frame.Payload.Clone()); }
        if (string.Equals(alteration, "clone", StringComparison.Ordinal)) { frame = frame with { }; }
        if (string.Equals(alteration, "key-copy", StringComparison.Ordinal)) { key = (byte[])key.Clone(); }
        if (string.Equals(alteration, "payload-change", StringComparison.Ordinal)) { frame.Payload[^1] ^= 1; }
        byte[] exactKey = key; WarpCoreCLRWorkerProtocol.Frame exactFrame = frame;
        Assert.Throws<InvalidDataException>(() => CoreCLRCallUseObservation.Begin(exactFrame, exactKey,
            string.Equals(alteration, "sequence", StringComparison.Ordinal) ? 10u : 9u, kernel));
    }

    [TestMethod]
    public async Task EqualIrLayoutCannotSubstituteForTheActualEmittedCompilation()
    {
        var layout = ObservationLayout(); CoreCLRResumableKernel kernel = CoreCLRResumableKernel.Compile(layout);
        var replacement = new WarpLogicalMachineLayout(layout.Kernel);
        Assert.AreEqual(WarpIrHash.Compute(layout.Kernel), WarpIrHash.Compute(replacement.Kernel), StringComparer.Ordinal);
        Assert.Throws<InvalidOperationException>(() => CoreCLRResumableKernel.PrepareCompilation(replacement, kernel.CompiledEntryPoint));
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] request = WarpCoreCLRWorkerWords.Request([[7]], [], 0, layout.CreateInitialState(32, 100), 32, layout.MaximumBlockCost, []);
        WarpCoreCLRWorkerProtocol.Frame frame = await ObservationFrameAsync(key, 9, request).ConfigureAwait(false);
        using CoreCLRCallUseObservation.Scope scope = CoreCLRCallUseObservation.Begin(frame, key, 9, kernel);
        Assert.Throws<InvalidOperationException>(() => new CoreCLRCallUseObservation.Scope(new object(), scope.Emission,
            frame, key, 9, null!));
        Assert.Throws<InvalidOperationException>(() => new CoreCLRCallUseObservation.Site(new object(), scope.Emission, null!));
    }

    [TestMethod]
    public async Task ForeignCallTokenIsRejectedBeforeAnyCandidateBankAccess()
    {
        var layout = ObservationLayout(); CoreCLRResumableKernel kernel = CoreCLRResumableKernel.Compile(layout);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] request = WarpCoreCLRWorkerWords.Request([[7]], [], 0, layout.CreateInitialState(32, 100), 32, layout.MaximumBlockCost, []);
        WarpCoreCLRWorkerProtocol.Frame frame = await ObservationFrameAsync(key, 9, request).ConfigureAwait(false);
        using CoreCLRCallUseObservation.Scope scope = CoreCLRCallUseObservation.Begin(frame, key, 9, kernel);
        Assert.Throws<InvalidOperationException>(() => CoreCLRCallUseObservation.Record(new object(), null!, null!, 0, null!, null!, 0));
    }

    [TestMethod]
    public async Task StaticProgramCounterAndCompilationWithoutExecutionProduceNoCallUse()
    {
        var layout = ObservationLayout(); CoreCLRResumableKernel kernel = CoreCLRResumableKernel.Compile(layout);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        uint[] state = layout.CreateInitialState(32, 100);
        state[WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameProgramCounterOffset] =
            checked((uint)layout.Nodes.First(node => node.Call is not null).ProgramCounter);
        byte[] request = WarpCoreCLRWorkerWords.Request([[7]], [], 0, state, 32, layout.MaximumBlockCost, []);
        WarpCoreCLRWorkerProtocol.Frame frame = await ObservationFrameAsync(key, 9, request).ConfigureAwait(false);
        byte[] response;
        using (CoreCLRCallUseObservation.Scope scope = CoreCLRCallUseObservation.Begin(frame, key, 9, kernel)) { response = scope.Complete(); }
        WarpCoreCLRWorkerCallObservations.Result result = WarpCoreCLRWorkerCallObservations.ReadResponse(response, request,
            state.Length, 0, kernel.CompiledEntryPoint.Module.ModuleVersionId);
        Assert.IsEmpty(result.Calls); CollectionAssert.AreEqual(state, result.State);
    }

    [TestMethod]
    [DataRow("module")]
    [DataRow("request")]
    [DataRow("truncated")]
    [DataRow("extended")]
    public async Task ObservedResponseRejectsDifferentModuleRequestAndIncompleteInventory(string alteration)
    {
        var layout = ObservationLayout(); CoreCLRResumableKernel kernel = CoreCLRResumableKernel.Compile(layout);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        uint[] state = layout.CreateInitialState(32, 100);
        byte[] request = WarpCoreCLRWorkerWords.Request([[7]], [], 0, state, 32, Math.Max(4096, layout.MaximumBlockCost), []);
        WarpCoreCLRWorkerProtocol.Frame frame = await ObservationFrameAsync(key, 9, request).ConfigureAwait(false);
        byte[] response;
        using (CoreCLRCallUseObservation.Scope scope = CoreCLRCallUseObservation.Begin(frame, key, 9, kernel))
        {
            WarpCoreCLRWorkerWords.Invocation call = scope.Invocation;
            kernel.ExecuteManagedQuantum(call.Inputs, call.Scalars, call.Worker, call.State, call.Depth, call.Quantum, call.Arena);
            response = scope.Complete();
        }
        Guid module = string.Equals(alteration, "module", StringComparison.Ordinal) ? Guid.NewGuid() : kernel.CompiledEntryPoint.Module.ModuleVersionId;
        if (string.Equals(alteration, "request", StringComparison.Ordinal)) { request = (byte[])request.Clone(); request[^1] ^= 1; }
        if (string.Equals(alteration, "truncated", StringComparison.Ordinal)) { response = response[..^1]; }
        if (string.Equals(alteration, "extended", StringComparison.Ordinal)) { response = [.. response, 0]; }
        byte[] exactResponse = response, exactRequest = request;
        Assert.Throws<InvalidDataException>(() => WarpCoreCLRWorkerCallObservations.ReadResponse(exactResponse, exactRequest, state.Length, 0, module));
    }

    [TestMethod]
    public async Task ObservationsCannotNestCompleteTwiceOrSurviveDisposal()
    {
        var layout = ObservationLayout(); CoreCLRResumableKernel kernel = CoreCLRResumableKernel.Compile(layout);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] request = WarpCoreCLRWorkerWords.Request([[7]], [], 0, layout.CreateInitialState(32, 100), 32, layout.MaximumBlockCost, []);
        WarpCoreCLRWorkerProtocol.Frame frame = await ObservationFrameAsync(key, 9, request).ConfigureAwait(false);
        using CoreCLRCallUseObservation.Scope scope = CoreCLRCallUseObservation.Begin(frame, key, 9, kernel);
        Assert.Throws<InvalidOperationException>(() => CoreCLRCallUseObservation.Begin(frame, key, 9, kernel));
        _ = scope.Complete(); Assert.Throws<InvalidOperationException>(() => scope.Complete());
        scope.Dispose(); Assert.Throws<InvalidOperationException>(() => scope.Complete()); scope.Dispose();
        // Outside an authenticated scope the public IL hook reads no banks.
        CoreCLRCallUseObservation.Record(new object(), null!, null!, 0, null!, null!, 0);
    }

    private static WarpLogicalMachineLayout ObservationLayout() => new(new WarpIntegerMapVerifier().Verify(
        new WarpIntegerMapRequest(typeof(TestKernels).GetMethod(nameof(TestKernels.Call))!, 1)).ControlFlow);

    private static async Task<WarpCoreCLRWorkerProtocol.Frame> ObservationFrameAsync(byte[] key, ulong sequence, byte[] request,
        ushort kind = WarpCoreCLRWorkerCallObservations.ExecuteObserved)
    {
        using var stream = new MemoryStream();
        await WarpCoreCLRWorkerProtocol.WriteAsync(stream, key, kind, sequence, request, CancellationToken.None).ConfigureAwait(false);
        stream.Position = 0;
        return await WarpCoreCLRWorkerProtocol.ReadAsync(stream, key, sequence, CancellationToken.None).ConfigureAwait(false);
    }
}
