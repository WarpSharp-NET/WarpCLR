using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class CompilationAdmissionTests
{
    [TestMethod]
    [DataRow(WarpCompilationResourceKind.Functions, WarpCompilationAdmission.MaximumFunctionsPerEntry)]
    [DataRow(WarpCompilationResourceKind.Blocks, WarpCompilationAdmission.MaximumBlocksPerEntry)]
    [DataRow(WarpCompilationResourceKind.Instructions, WarpCompilationAdmission.MaximumInstructionsPerEntry)]
    [DataRow(WarpCompilationResourceKind.ValueSlots, WarpCompilationAdmission.MaximumValueSlotsPerEntry)]
    [DataRow(WarpCompilationResourceKind.OperandReferences, WarpCompilationAdmission.MaximumOperandReferencesPerEntry)]
    [DataRow(WarpCompilationResourceKind.Parameters, WarpCompilationAdmission.MaximumParametersPerBody)]
    [DataRow(WarpCompilationResourceKind.Locals, WarpCompilationAdmission.MaximumLocalsPerBody)]
    [DataRow(WarpCompilationResourceKind.EvaluationStack, WarpCompilationAdmission.MaximumEvaluationStackPerBody)]
    [DataRow(WarpCompilationResourceKind.CilBytes, WarpCompilationAdmission.MaximumCilBytesPerEntry)]
    [DataRow(WarpCompilationResourceKind.VerifierWorkspaceSlots, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry)]
    [DataRow(WarpCompilationResourceKind.IdentityCharacters, WarpCompilationAdmission.MaximumIdentityCharacters)]
    public void SharedBoundaryAcceptsExactLimitAndRejectsOneMore(WarpCompilationResourceKind resource, int limit)
    {
        WarpCompilationAdmission.Require("boundary", resource, limit, limit);
        WarpCompilationResourceException error = Assert.ThrowsExactly<WarpCompilationResourceException>(
            () => WarpCompilationAdmission.Require("boundary", resource, limit + 1L, limit));
        Assert.AreEqual(resource, error.Resource);
        Assert.AreEqual(limit + 1L, error.Requested);
        Assert.AreEqual((long)limit, error.Limit);
        Assert.AreEqual("boundary", error.EntryIdentity, StringComparer.Ordinal);
    }

    [TestMethod]
    public void BoundedEnumerationStopsBeforeCollectingAnUnboundedSequence()
    {
        int yielded = 0;
        IEnumerable<int> Items()
        {
            while (true)
            {
                yielded++;
                yield return yielded;
            }
        }

        WarpCompilationResourceException error = Assert.ThrowsExactly<WarpCompilationResourceException>(
            () => WarpCompilationAdmission.Materialize(Items(), "enumeration", WarpCompilationResourceKind.Blocks, 16));
        Assert.AreEqual(17, yielded);
        Assert.AreEqual(17L, error.Requested);
    }

    [TestMethod]
    public void SignatureLimitIsCheckedBeforeKernelDefinitionTables()
    {
        WarpCompilationResourceException error = Assert.ThrowsExactly<WarpCompilationResourceException>(
            () => new WarpControlFlowKernel("parameters", 1, WarpCompilationAdmission.MaximumParametersPerBody, SimpleBlocks()));
        Assert.AreEqual(WarpCompilationResourceKind.Parameters, error.Resource);
        Assert.AreEqual(WarpCompilationAdmission.MaximumParametersPerBody + 1L, error.Requested);
        var accepted = new WarpControlFlowKernel("parameters", 1, WarpCompilationAdmission.MaximumParametersPerBody - 1, SimpleBlocks());
        WarpCompilationAdmission.Validate(accepted);
    }

    [TestMethod]
    public void HelperDiscoveryRejectsDepthBeforeRecursiveVisit()
    {
        var admission = new WarpCilCompilationAdmission("deep-helper-chain");
        admission.AdmitMethod("entry", 0, 1, 0, 1, isEntry: true);
        for (int helper = 0; helper < WarpCompilationAdmission.MaximumFunctionsPerEntry; helper++)
        {
            admission.AdmitMethod("helper", 0, 1, 0, 1, isEntry: false);
        }

        WarpCompilationResourceException error = Assert.ThrowsExactly<WarpCompilationResourceException>(
            () => admission.AdmitMethod("helper", 0, 1, 0, 1, isEntry: false));
        Assert.AreEqual(WarpCompilationResourceKind.Functions, error.Resource);
    }

    [TestMethod]
    public void CilDecoderRejectsInstructionGrowthBeforeLowering()
    {
        byte[] il = new byte[WarpCompilationAdmission.MaximumInstructionsPerEntry + 1];
        _ = WarpIntegerMapCilVerifier.ReadCallTokens(il.AsSpan(0, il.Length - 1), "instructions", out int count);
        Assert.AreEqual(WarpCompilationAdmission.MaximumInstructionsPerEntry, count);
        WarpCompilationResourceException error = Assert.ThrowsExactly<WarpCompilationResourceException>(
            () => WarpIntegerMapCilVerifier.ReadCallTokens(il, "instructions", out _));
        Assert.AreEqual(WarpCompilationResourceKind.Instructions, error.Resource);
    }

    [TestMethod]
    public void CilBodyLimitRejectsBeforeOwnedByteCopy()
    {
        byte[] il = new byte[WarpCompilationAdmission.MaximumCilBytesPerBody + 1];
        WarpCompilationResourceException error = Assert.ThrowsExactly<WarpCompilationResourceException>(
            () => new WarpIntegerMapMethodBody("CIL-bytes", 0, 0, 1, [], il));
        Assert.AreEqual(WarpCompilationResourceKind.CilBytes, error.Resource);
    }

    [TestMethod]
    public void FlowWorkspaceProductIsCheckedBeforeFlowAndPhiArrays()
    {
        var method = new WarpIntegerMapMethodBody("workspace", 0, 0,
            WarpCompilationAdmission.MaximumEvaluationStackPerBody,
            Enumerable.Repeat(WarpMetadataType.UInt32, WarpCompilationAdmission.MaximumLocalsPerBody).ToImmutableArray(), []);
        new WarpCilCompilationAdmission(method.Identity).AdmitBlocks(method, 128);
        WarpCompilationResourceException error = Assert.ThrowsExactly<WarpCompilationResourceException>(
            () => new WarpCilCompilationAdmission(method.Identity).AdmitBlocks(method, 129));
        Assert.AreEqual(WarpCompilationResourceKind.VerifierWorkspaceSlots, error.Resource);
    }

    [TestMethod]
    public void RepeatedModuleExpansionIsBoundedAcrossEntries()
    {
        var module = new WarpModuleCompilationAdmission();
        for (int entry = 0; entry < WarpCompilationAdmission.MaximumModuleExpansionFactor; entry++)
        {
            module.AdmitDecodedInstructions(WarpCompilationAdmission.MaximumInstructionsPerEntry);
        }

        WarpCompilationResourceException error = Assert.ThrowsExactly<WarpCompilationResourceException>(
            () => module.AdmitDecodedInstructions(1));
        Assert.AreEqual(WarpCompilationResourceKind.Instructions, error.Resource);
        Assert.AreEqual(WarpCompilationAdmission.MaximumModuleExpansionFactor * (long)WarpCompilationAdmission.MaximumInstructionsPerEntry, error.Limit);
    }

    [TestMethod]
    [DataRow(WarpBackendKind.NVPTX)]
    [DataRow(WarpBackendKind.AMDGPU)]
    [DataRow(WarpBackendKind.SPIRV)]
    public void NativeSourceUsesExactUtf8ByteAdmissionBeforeAppend(WarpBackendKind backend)
    {
        var layout = new WarpLogicalMachineLayout(new WarpControlFlowKernel("source", 1, 0, SimpleBlocks()));
        string expected = WarpPortableMachineEmitter.Emit(layout, backend);
        int bytes = Encoding.UTF8.GetByteCount(expected);
        Assert.AreEqual(expected, WarpPortableMachineEmitter.Emit(layout, backend, bytes), StringComparer.Ordinal);
        WarpCompilationResourceException error = Assert.ThrowsExactly<WarpCompilationResourceException>(
            () => WarpPortableMachineEmitter.Emit(layout, backend, bytes - 1));
        Assert.AreEqual(WarpCompilationResourceKind.SourceBytes, error.Resource);
        Assert.AreEqual(bytes - 1L, error.Limit);
        Assert.IsGreaterThan(error.Limit, error.Requested);
    }

    [TestMethod]
    public void UnicodeSourceBytesAreCheckedWithoutPartialAppend()
    {
        int limit = Encoding.UTF8.GetByteCount("é" + Environment.NewLine);
        var source = new WarpBoundedSourceBuilder("utf8", limit);
        source.AppendLine("é");
        Assert.AreEqual("é" + Environment.NewLine, source.ToString(), StringComparer.Ordinal);
        _ = Assert.ThrowsExactly<WarpCompilationResourceException>(() => source.AppendLine("x"));
        Assert.AreEqual("é" + Environment.NewLine, source.ToString(), StringComparer.Ordinal);
    }

    [TestMethod]
    public void PrecancelledSourceEmissionDoesNotStartTheWriter()
    {
        var layout = new WarpLogicalMachineLayout(new WarpControlFlowKernel("cancelled", 1, 0, SimpleBlocks()));
        var token = new CancellationToken(canceled: true);
        OperationCanceledException error = Assert.ThrowsExactly<OperationCanceledException>(
            () => WarpPortableMachineEmitter.Emit(layout, WarpBackendKind.NVPTX, 1, token));
        Assert.AreEqual(token, error.CancellationToken);
    }

    [TestMethod]
    public async Task CancelledSourceWriterRetainsOnlyPreviouslyAdmittedLines()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new WarpBoundedSourceBuilder("cancelled", 4096, cancellation.Token);
        source.AppendLine("first");
        await cancellation.CancelAsync().ConfigureAwait(false);
        _ = Assert.ThrowsExactly<OperationCanceledException>(() => source.AppendLine("é"));
        Assert.AreEqual("first" + Environment.NewLine, source.ToString(), StringComparer.Ordinal);
    }

    [TestMethod]
    public void DeclaredMetadataParameterCountIsAdmittedBeforeSignatureArrays()
    {
        byte[] signature = [0x00, 0xDF, 0xFF, 0xFF, 0xFF];
        using MetadataReaderProvider provider = CreateMetadata(signature, isLocal: false);
        MetadataReader metadata = provider.GetMetadataReader();
        WarpCompilationResourceException error = Assert.ThrowsExactly<WarpCompilationResourceException>(
            () => WarpMetadataCompilationAdmission.ReadMethodSignature(metadata,
                metadata.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(1)), "metadata-parameters"));
        Assert.AreEqual(WarpCompilationResourceKind.Parameters, error.Resource);
    }

    [TestMethod]
    public void DeclaredMetadataLocalCountIsAdmittedBeforeSignatureArrays()
    {
        byte[] signature = [0x07, 0xDF, 0xFF, 0xFF, 0xFF];
        using MetadataReaderProvider provider = CreateMetadata(signature, isLocal: true);
        MetadataReader metadata = provider.GetMetadataReader();
        WarpCompilationResourceException error = Assert.ThrowsExactly<WarpCompilationResourceException>(
            () => WarpMetadataCompilationAdmission.ReadLocalSignature(metadata,
                metadata.GetStandaloneSignature(MetadataTokens.StandaloneSignatureHandle(1)), "metadata-locals"));
        Assert.AreEqual(WarpCompilationResourceKind.Locals, error.Resource);
    }

    [TestMethod]
    public void UnsupportedNestedSignaturesNeverEnterRecursiveDecoder()
    {
        byte[] signature = new byte[8192];
        signature.AsSpan(2).Fill(0x1D);
        using MetadataReaderProvider provider = CreateMetadata(signature, isLocal: false);
        MetadataReader metadata = provider.GetMetadataReader();
        MethodSignature<WarpMetadataType> decoded = WarpMetadataCompilationAdmission.ReadMethodSignature(metadata,
            metadata.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(1)), "deep-signature");
        Assert.AreEqual(WarpMetadataType.Unsupported, decoded.ReturnType);
    }

    [TestMethod]
    public void AcceptedSharedKernelStillCompilesToNativeCoreClrCode()
    {
        var kernel = new WarpControlFlowKernel("accepted", 1, 0, SimpleBlocks());
        CoreCLRJitKernel direct = CoreCLRJitKernel.Compile(kernel);
        Assert.AreEqual(19u, direct.Invoke([[19]], [], 0, new CoreCLRExecutionBudget(2, 1)));
        var layout = new WarpLogicalMachineLayout(kernel);
        CoreCLRResumableKernel resumable = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(1, 2);
        resumable.ExecuteQuantum([[19]], [], 0, state, 1, 2);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(19u, state[WarpLogicalMachineLayout.ResultOffset]);
    }

    private static WarpBasicBlock[] SimpleBlocks() =>
        [new WarpBasicBlock(0, [], [new WarpIrInstruction(0, WarpIrOpCode.LoadInput)], new WarpReturnTerminator(0))];

    private static MetadataReaderProvider CreateMetadata(byte[] signature, bool isLocal)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("admission"), metadata.GetOrAddGuid(Guid.Empty), default, default);
        BlobHandle blob = metadata.GetOrAddBlob(signature);
        if (isLocal)
        {
            metadata.AddStandaloneSignature(blob);
        }
        else
        {
            metadata.AddMethodDefinition(MethodAttributes.Static, MethodImplAttributes.IL,
                metadata.GetOrAddString("entry"), blob, 0, MetadataTokens.ParameterHandle(1));
        }

        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
        return MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create<byte>(image.ToArray()));
    }
}
