using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;
using WarpCLR.Sdk;

namespace WarpCLR.Tests.Architecture;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class NativeInteropPolicyTests
{
    private static readonly string[] AggregateSleepArguments = ["0.4"];
    private static readonly string[] AggregateVersionArguments = ["fixture-version"];
    private static readonly int[] ExpectedHipPropertiesR0600 = [1472, 288, 320, 336, 1160];
    [TestMethod]
    public void NativeTargetCannotSelectCoreCLR()
    {
        Assert.Throws<ArgumentException>(() => Target(WarpBackendKind.CoreCLR, "x64"));
    }

    [TestMethod]
    public void NativeTargetRejectsArbitraryArchitectureAndArgumentInjection()
    {
        Assert.Throws<ArgumentException>(() => Target(WarpBackendKind.NVPTX, "sm_80; echo unsafe"));
        Assert.Throws<ArgumentException>(() => Target(WarpBackendKind.AMDGPU, "generic"));
        Assert.Throws<ArgumentException>(() => Target(WarpBackendKind.SPIRV, "vulkan"));
        Assert.Throws<ArgumentException>(() => Target(WarpBackendKind.AMDGPU, "gfx942 -filetype=asm"));
        Assert.Throws<ArgumentException>(() => Target(WarpBackendKind.AMDGPU, "gfx90a:xnack+:xnack-"));
        Assert.AreEqual("gfx90a:sramecc+:xnack-", Target(WarpBackendKind.AMDGPU, "gfx90a:sramecc+:xnack-").Architecture, StringComparer.Ordinal);
    }

    [TestMethod]
    public void NativeImageRejectsLlvmTextInPlaceOfBinary()
    {
        byte[] text = Encoding.UTF8.GetBytes("define void @warp_integer_map() { ret void }");
        Assert.Throws<ArgumentException>(() => Image(Target(WarpBackendKind.AMDGPU, "gfx942"), WarpNativeImageFormat.Hsaco, text));
        Assert.Throws<ArgumentException>(() => Image(Target(WarpBackendKind.SPIRV, "opencl2.2-spirv1.2"), WarpNativeImageFormat.SpirV, text));
    }

    [TestMethod]
    public void NativeImageRejectsWrongBackendBinaryAndUnknownEntrypoint()
    {
        Assert.Throws<ArgumentException>(() => Image(Target(WarpBackendKind.NVPTX, "sm_80"), WarpNativeImageFormat.SpirV, SpirVHeader()));
        Assert.Throws<ArgumentException>(() => new WarpNativeImage(Target(WarpBackendKind.NVPTX, "sm_80"),
            WarpNativeImageFormat.Ptx, "unverified_entry", [1], "source", "toolchain"));
    }

    [TestMethod]
    public void BinaryHeaderIsNotProductionConformanceEvidence()
    {
        WarpNativeImage image = Image(Target(WarpBackendKind.SPIRV, "opencl2.2-spirv1.2"), WarpNativeImageFormat.SpirV, SpirVHeader());
        Assert.AreEqual(WarpConformanceStatus.DevelopmentNonconforming, image.ConformanceStatus);
        Assert.IsFalse(image.SupportsPortableSafepoints);
        Assert.IsFalse(image.SupportsScalableReduction);
        WarpHostException error = Assert.Throws<WarpHostException>(image.RequireProductionAdmission);
        Assert.AreEqual("WRPNATIVE1008", error.Code, StringComparer.Ordinal);
    }

    [TestMethod]
    public void NativeImageOwnsContentAndRejectsArgumentSchemaMismatch()
    {
        byte[] source = [42];
        WarpNativeImage image = new(Target(WarpBackendKind.NVPTX, "sm_80"), WarpNativeImageFormat.Ptx,
            WarpDeviceAbi.IntegerMapEntryPoint, source, "source", "toolchain", 2, 1);
        source[0] = 0;
        Assert.AreEqual((byte)42, image.Content.Span[0]);
        image.ValidateArguments([new uint[1], new uint[1]], [7], false);
        Assert.Throws<WarpHostException>(() => image.ValidateArguments([new uint[1]], [7], false));
        Assert.Throws<WarpHostException>(() => image.ValidateArguments([new uint[1], new uint[1]], [], false));
        Assert.Throws<WarpHostException>(() => image.ValidateArguments([new uint[1], new uint[1]], [7], true));
    }

    [TestMethod]
    public void RuntimeAndPhysicalTargetIdentityChangeNativeCacheIdentity()
    {
        WarpNativeTarget target = Target(WarpBackendKind.NVPTX, "sm_80");
        WarpNativeTarget otherRuntime = new(target.Backend, target.Architecture, target.DeviceIdentity, "driver-new",
            target.MaxWorkgroupSize, target.MaxGridX, target.GlobalMemoryBytes);
        WarpNativeTarget otherDevice = new(target.Backend, target.Architecture, "pci-other", target.RuntimeIdentity,
            target.MaxWorkgroupSize, target.MaxGridX, target.GlobalMemoryBytes);
        Assert.AreNotEqual(target.CacheIdentity, otherRuntime.CacheIdentity, StringComparer.Ordinal);
        Assert.AreNotEqual(target.CacheIdentity, otherDevice.CacheIdentity, StringComparer.Ordinal);
    }

    [TestMethod]
    public void NativeResourceAdmissionCatchesGridWorkgroupAndMemoryLimitsBeforeLaunch()
    {
        WarpNativeTarget target = new(WarpBackendKind.NVPTX, "sm_80", "device", "runtime", 256, 1, 1024 * 1024);
        WarpNativeLaunch accepted = WarpNativeLaunch.Admit(target, [new uint[256]], 256, false);
        Assert.AreEqual(1u, accepted.GridX);
        Assert.AreEqual(256u, accepted.WorkgroupSize);
        Assert.AreEqual(2048UL, accepted.BytesRequired);
        Assert.AreEqual("WRPNATIVE1005", Assert.Throws<WarpHostException>(() =>
            WarpNativeLaunch.Admit(target, [new uint[257]], 257, false)).Code, StringComparer.Ordinal);
        WarpNativeTarget smallMemory = new(WarpBackendKind.NVPTX, "sm_80", "device", "runtime", 256, 100, 16);
        Assert.Throws<WarpHostException>(() => WarpNativeLaunch.Admit(smallMemory, [new uint[3]], 3, false));
        WarpNativeTarget smallBlock = new(WarpBackendKind.NVPTX, "sm_80", "device", "runtime", 128, 100, 1024);
        Assert.Throws<WarpHostException>(() => WarpNativeLaunch.Admit(smallBlock, [new uint[1]], 1, false));
    }

    [TestMethod]
    public void NativeResourceAdmissionValidatesBuffersAndEmptyDispatchShapes()
    {
        WarpNativeTarget target = Target(WarpBackendKind.NVPTX, "sm_80");
        Assert.Throws<WarpHostException>(() => WarpNativeLaunch.Admit(target, [new uint[2]], 3, false));
        Assert.Throws<WarpHostException>(() => WarpNativeLaunch.Admit(target, [null!], 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => WarpNativeLaunch.Admit(target, [], -1, false));
        WarpNativeLaunch empty = WarpNativeLaunch.Admit(target, [Array.Empty<uint>()], 0, false);
        Assert.AreEqual(0u, empty.GridX);
        Assert.AreEqual(0, empty.OutputCount);
        WarpNativeLaunch reduction = WarpNativeLaunch.Admit(target, [Array.Empty<uint>()], 0, true);
        Assert.AreEqual(1u, reduction.GridX);
        Assert.AreEqual(1, reduction.OutputCount);
    }

    [TestMethod]
    public void NativeArgumentStoragePreservesFullWidthPointersAndUIntValues()
    {
        using var arguments = new WarpNativeKernelArguments([0xFEDCBA9876543210UL], 0xAABBCCDDEEFF0011UL, uint.MaxValue, [0x80000000]);
        IntPtr first = Marshal.ReadIntPtr(arguments.Pointer);
        IntPtr output = Marshal.ReadIntPtr(arguments.Pointer, IntPtr.Size);
        IntPtr count = Marshal.ReadIntPtr(arguments.Pointer, 2 * IntPtr.Size);
        IntPtr scalar = Marshal.ReadIntPtr(arguments.Pointer, 3 * IntPtr.Size);
        Assert.AreEqual(0xFEDCBA9876543210UL, unchecked((ulong)Marshal.ReadInt64(first)));
        Assert.AreEqual(0xAABBCCDDEEFF0011UL, unchecked((ulong)Marshal.ReadInt64(output)));
        Assert.AreEqual(uint.MaxValue, unchecked((uint)Marshal.ReadInt32(count)));
        Assert.AreEqual(0x80000000u, unchecked((uint)Marshal.ReadInt32(scalar)));
        Assert.AreEqual(0L, first.ToInt64() % sizeof(ulong));
    }

    [TestMethod]
    public async Task PtxRetainsDevelopmentStatusButIsRetargetedToConcreteHardware()
    {
        WarpCompilation compilation = new WarpBuildPipeline().CompileIntegerMap(
            typeof(TestKernels).GetMethod(nameof(TestKernels.Combine))!, 2);
        WarpNativeTarget target = Target(WarpBackendKind.NVPTX, "sm_89");
        WarpNativeImage image = await new WarpNativeToolchain().CompileAsync(compilation.Artifacts[WarpBackendKind.NVPTX], target).ConfigureAwait(false);
        StringAssert.Contains(Encoding.UTF8.GetString(image.Content.Span), ".target sm_89", StringComparison.Ordinal);
        Assert.AreEqual(2, image.InputBufferCount);
        Assert.AreEqual(2, image.ScalarArgumentCount);
        Assert.AreEqual(compilation.Artifacts[WarpBackendKind.NVPTX].ContentHash, image.SourceHash, StringComparer.Ordinal);
        Assert.IsFalse(image.SupportsPortableSafepoints);
    }

    [TestMethod]
    public async Task NativeToolchainRejectsCrossBackendArtifactAndAmbiguousPtx()
    {
        WarpBackendArtifact ptx = new(WarpBackendKind.NVPTX, WarpArtifactFormat.NVPTX,
            WarpDeviceAbi.IntegerMapEntryPoint, Encoding.UTF8.GetBytes(".target sm_50\n.target sm_80\n"));
        var toolchain = new WarpNativeToolchain();
        await Assert.ThrowsAsync<WarpHostException>(() => toolchain.CompileAsync(ptx, Target(WarpBackendKind.AMDGPU, "gfx942"))).ConfigureAwait(false);
        await Assert.ThrowsAsync<WarpHostException>(() => toolchain.CompileAsync(ptx, Target(WarpBackendKind.NVPTX, "sm_89"))).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MissingNativeCompilerIsAnExplicitErrorWithoutEmulation()
    {
        WarpBackendArtifact llvm = new(WarpBackendKind.AMDGPU, WarpArtifactFormat.AMDGPULLVMIR,
            WarpDeviceAbi.IntegerMapEntryPoint, Encoding.UTF8.GetBytes("target triple = \"amdgcn-amd-amdhsa\"\n"));
        var toolchain = new WarpNativeToolchain(new WarpNativeToolchainOptions
        {
            LlvmAssembler = Path.Combine(Path.GetTempPath(), "missing-warpclr-tool-" + Guid.NewGuid().ToString("N")),
        });
        WarpHostException error = await Assert.ThrowsAsync<WarpHostException>(() => toolchain.CompileAsync(llvm,
            Target(WarpBackendKind.AMDGPU, "gfx942"))).ConfigureAwait(false);
        Assert.AreEqual("WRPNATIVE2001", error.Code, StringComparer.Ordinal);
    }

    [TestMethod]
    public async Task NativeProcessDoesNotInterpretShellMetacharactersInArguments()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The test process fixture requires Linux printf."); }
        WarpToolProcessResult result = await WarpToolProcess.RunAsync("/usr/bin/printf",
            ["%s", "literal ; $(echo never-run) | value"], null, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.AreEqual("literal ; $(echo never-run) | value", result.StandardOutput, StringComparer.Ordinal);
    }

    [TestMethod]
    public async Task NativeProcessDrainsAndBoundsLargeDiagnostics()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The test process fixture requires Linux tools."); }
        WarpToolProcessResult result = await WarpToolProcess.RunAsync("/bin/sh",
            ["-c", "head -c 200000 /dev/zero | tr '\\000' x"], null, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.IsLessThan(66000, result.StandardOutput.Length);
        StringAssert.Contains(result.StandardOutput, "diagnostic output truncated", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task NativeProcessNonzeroExitIsACompilationDiagnostic()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The test process fixture requires Linux tools."); }
        WarpHostException error = await Assert.ThrowsAsync<WarpHostException>(() => WarpToolProcess.RunAsync("/bin/sh",
            ["-c", "printf 'compiler failed' >&2; exit 7"], null, TimeSpan.FromSeconds(10))).ConfigureAwait(false);
        Assert.AreEqual("WRPNATIVE2002", error.Code, StringComparer.Ordinal);
        StringAssert.Contains(error.Message, "compiler failed", StringComparison.Ordinal);
        StringAssert.Contains(error.Message, "exit code 7", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task NativeProcessTimeoutAndCallerCancellationAreDistinct()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The test process fixture requires Linux sleep."); }
        WarpHostException error = await Assert.ThrowsAsync<WarpHostException>(() => WarpToolProcess.RunAsync("/bin/sleep",
            ["10"], null, TimeSpan.FromMilliseconds(100))).ConfigureAwait(false);
        Assert.AreEqual("WRPNATIVE2004", error.Code, StringComparer.Ordinal);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() => WarpToolProcess.RunAsync("/bin/sleep",
            ["10"], null, TimeSpan.FromSeconds(10), cancel.Token)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task NativeCompilationDeadlineCoversSeveralIndividuallyBoundedProcessSteps()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The test process fixture requires Linux sleep."); }
        int completed = 0;
        WarpHostException error = await Assert.ThrowsAsync<WarpHostException>(() => WarpNativeCompilationDeadline.RunAsync(
            TimeSpan.FromMilliseconds(900), async token =>
            {
                for (int step = 0; step < 3; step++)
                {
                    await WarpToolProcess.RunAsync("/bin/sleep", AggregateSleepArguments, null, TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    completed++;
                }

                return completed;
            })).ConfigureAwait(false);
        Assert.AreEqual("WRPNATIVE2004", error.Code, StringComparer.Ordinal);
        Assert.IsGreaterThan(0, completed);
        Assert.IsLessThan(3, completed);
    }

    [TestMethod]
    public async Task NativeCompilationDeadlineAllowsAWholeProcessPipelineWithinBudget()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The test process fixture requires Linux printf."); }
        int completed = await WarpNativeCompilationDeadline.RunAsync(TimeSpan.FromSeconds(10), async token =>
        {
            int steps = 0;
            for (int step = 0; step < 3; step++)
            {
                WarpToolProcessResult result = await WarpToolProcess.RunAsync("/usr/bin/printf", AggregateVersionArguments,
                    null, TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                Assert.AreEqual("fixture-version", result.StandardOutput, StringComparer.Ordinal);
                steps++;
            }

            return steps;
        }).ConfigureAwait(false);
        Assert.AreEqual(3, completed);
    }

    [TestMethod]
    public async Task NativeCompilationDeadlinePreservesExplicitCallerCancellation()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The test process fixture requires Linux sleep."); }
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        OperationCanceledException error = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            WarpNativeCompilationDeadline.RunAsync(TimeSpan.FromSeconds(10), token =>
                WarpToolProcess.RunAsync("/bin/sleep", ["10"], null, TimeSpan.FromSeconds(10), token), cancel.Token)).ConfigureAwait(false);
        Assert.AreEqual(cancel.Token, error.CancellationToken);
    }

    [TestMethod]
    public async Task NativeProcessDeadlineAlsoBoundsInheritedOutputPipesAfterParentExit()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The test process fixture requires Linux tools."); }
        WarpHostException error = await Assert.ThrowsAsync<WarpHostException>(() => WarpToolProcess.RunAsync("/bin/sh",
            ["-c", "sleep 1 & exit 0"], null, TimeSpan.FromMilliseconds(100))).ConfigureAwait(false);
        Assert.AreEqual("WRPNATIVE2004", error.Code, StringComparer.Ordinal);
    }

    [TestMethod]
    public void NativeDriverConfigurationRejectsRelativeLibraryPathsBeforeLoading()
    {
        Assert.Throws<ArgumentException>(() => WarpNativeLibrary.Open("untrusted-driver.so", "ignored"));
    }

    [TestMethod]
    public void HipUsesExplicitlyVersioned64BitPropertiesLayout()
    {
        CollectionAssert.AreEqual(ExpectedHipPropertiesR0600, new int[]
        {
            HipDevicePropertiesR0600.Size, HipDevicePropertiesR0600.TotalGlobalMemory,
            HipDevicePropertiesR0600.MaxThreadsPerBlock, HipDevicePropertiesR0600.MaxGridSize,
            HipDevicePropertiesR0600.GcnArchitecture,
        });
    }

    [TestMethod]
    public void LogicalMachineAdmissionValidatesStateShapeAndContinuationBeforeNativeDispatch()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Call));
        WarpNativeImage image = MachineImage(layout);
        uint[] state = layout.CreateInitialState(8, 1000);
        WarpNativeMachineLaunch accepted = WarpNativeMachineLaunch.Admit(image, state, [new uint[5]], [],
            1, 4, 8, layout.MaximumBlockCost);
        Assert.AreEqual(1u, accepted.GridX);
        Assert.Throws<WarpHostException>(() => WarpNativeMachineLaunch.Admit(image, state, [new uint[5]], [],
            1, 5, 8, layout.MaximumBlockCost));
        Assert.Throws<WarpHostException>(() => WarpNativeMachineLaunch.Admit(image, state, [new uint[5]], [],
            1, 4, 7, layout.MaximumBlockCost));
        Assert.Throws<WarpHostException>(() => WarpNativeMachineLaunch.Admit(image, state, [new uint[5]], [],
            1, 4, 8, layout.MaximumBlockCost - 1));
        state[WarpLogicalMachineLayout.DepthOffset] = 9;
        Assert.Throws<WarpHostException>(() => WarpNativeMachineLaunch.Admit(image, state, [new uint[5]], [],
            1, 4, 8, layout.MaximumBlockCost));
        state[WarpLogicalMachineLayout.DepthOffset] = 1;
        state[WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameProgramCounterOffset] = uint.MaxValue;
        Assert.Throws<WarpHostException>(() => WarpNativeMachineLaunch.Admit(image, state, [new uint[5]], [],
            1, 4, 8, layout.MaximumBlockCost));
    }

    [TestMethod]
    public void LogicalMachineImageHasControlAbiAndCannotUseLegacyDispatchSignature()
    {
        WarpNativeImage image = MachineImage(Layout(nameof(TestKernels.Loop)));
        Assert.AreEqual(WarpLogicalMachineLayout.Version, image.DeviceAbiVersion, StringComparer.Ordinal);
        Assert.IsTrue(image.SupportsPortableSafepoints);
        Assert.IsTrue(image.SupportsScalableReduction);
        Assert.AreEqual(WarpConformanceStatus.DevelopmentNonconforming, image.ConformanceStatus);
        image.RequireProductionAdmission();
        Assert.Throws<WarpHostException>(() => image.ValidateArguments([new uint[1]], [], false));
    }

    [TestMethod]
    public void LogicalMachineArgumentsPreserveTheSharedPortableParameterOrder()
    {
        using var arguments = new WarpNativeKernelArguments(123, [456, 789], [12, 34], 56, 78, 90, 100);
        Assert.AreEqual(123L, Marshal.ReadInt64(Marshal.ReadIntPtr(arguments.Pointer)));
        Assert.AreEqual(456L, Marshal.ReadInt64(Marshal.ReadIntPtr(arguments.Pointer, IntPtr.Size)));
        Assert.AreEqual(789L, Marshal.ReadInt64(Marshal.ReadIntPtr(arguments.Pointer, 2 * IntPtr.Size)));
        uint[] expected = [12, 34, 56, 78, 90, 100];
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.AreEqual(expected[index], unchecked((uint)Marshal.ReadInt32(
                Marshal.ReadIntPtr(arguments.Pointer, (3 + index) * IntPtr.Size))));
        }
    }

    [TestMethod]
    public void LogicalMachineTransferIsTransactionalAndReleasesAllAllocations()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        WarpNativeImage image = MachineImage(layout);
        uint[] state = layout.CreateInitialState(4, 1000);
        using var fixture = new TransferFixture();
        fixture.OnLaunch = arguments =>
        {
            ulong statesPointer = TransferFixture.ReadPointer(arguments, 0);
            Marshal.WriteInt32(new IntPtr(unchecked((long)statesPointer)), WarpLogicalMachineLayout.StatusOffset * sizeof(uint),
                (int)WarpLogicalMachineLayout.Completed);
            Marshal.WriteInt32(new IntPtr(unchecked((long)statesPointer)), WarpLogicalMachineLayout.ResultOffset * sizeof(uint), 42);
            Assert.AreEqual(1u, TransferFixture.ReadInteger(arguments, 2));
            Assert.AreEqual(0u, TransferFixture.ReadInteger(arguments, 3));
            Assert.AreEqual(4u, TransferFixture.ReadInteger(arguments, 4));
            Assert.AreEqual(128u, TransferFixture.ReadInteger(arguments, 5));
        };
        uint[] result = WarpNativeMachineDispatch.Resume(image, state, [new uint[] { 5 }], [], 1, 0, 4, 128,
            fixture.Operations, CancellationToken.None);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, result[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(42u, result[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.HasCount(0, fixture.Allocations);
        Assert.AreEqual(1, fixture.Launches);
    }

    [TestMethod]
    public void LogicalMachineNativeFailureQuarantinesContextAndCancellationDoesNotPublishState()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        WarpNativeImage image = MachineImage(layout);
        uint[] state = layout.CreateInitialState(4, 1000);
        using (var failed = new TransferFixture())
        {
            failed.OnLaunch = _ => throw new WarpHostException("fixture", "Native launch failed.");
            Assert.Throws<WarpHostException>(() => WarpNativeMachineDispatch.Resume(image, state, [new uint[1]], [],
                1, 0, 4, 128, failed.Operations, CancellationToken.None));
            Assert.IsTrue(failed.Quarantined);
            Assert.HasCount(0, failed.Allocations);
        }

        using var cancel = new CancellationTokenSource();
        using var cancelled = new TransferFixture();
        cancelled.OnSynchronize = cancel.Cancel;
        Assert.Throws<OperationCanceledException>(() => WarpNativeMachineDispatch.Resume(image, state, [new uint[1]], [],
            1, 0, 4, 128, cancelled.Operations, cancel.Token));
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.IsFalse(cancelled.Quarantined);
        Assert.HasCount(0, cancelled.Allocations);
    }

    [TestMethod]
    public void ParallelReductionOrchestratesPairwiseResidentPassesWithoutHostCombination()
    {
        WarpNativeImage image = MachineImage(Layout(nameof(TestKernels.Loop)));
        using var fixture = new TransferFixture();
        var inputCounts = new List<uint>();
        fixture.OnLaunch = arguments =>
        {
            ulong output = TransferFixture.ReadPointer(arguments, 0);
            ulong input = TransferFixture.ReadPointer(arguments, 1);
            uint count = TransferFixture.ReadInteger(arguments, 2);
            Assert.AreEqual((uint)WarpReductionOperation.WrappingSum, TransferFixture.ReadInteger(arguments, 3));
            inputCounts.Add(count);
            for (uint index = 0; index < (count + 1) / 2; index++)
            {
                uint left = unchecked((uint)Marshal.ReadInt32(new IntPtr(unchecked((long)input)), checked((int)index * 2 * sizeof(uint))));
                uint right = 2 * index + 1 < count
                    ? unchecked((uint)Marshal.ReadInt32(new IntPtr(unchecked((long)input)), checked((int)(2 * index + 1) * sizeof(uint))))
                    : 0;
                Marshal.WriteInt32(new IntPtr(unchecked((long)output)), checked((int)index * sizeof(uint)), unchecked((int)(left + right)));
            }
        };
        uint actual = WarpNativeReductionDispatch.Reduce(image, [1, 2, 3, 4, 5, 6, 7, 8, 9],
            WarpReductionOperation.WrappingSum, fixture.Operations, CancellationToken.None);
        Assert.AreEqual(45u, actual);
        CollectionAssert.AreEqual(new uint[] { 9, 5, 3, 2 }, inputCounts);
        Assert.AreEqual(2, fixture.MaximumAllocations);
        Assert.HasCount(0, fixture.Allocations);
    }

    [TestMethod]
    public void ReductionEmptyIdentityAndSingletonNeedNoDevicePassAndPreserveCancellation()
    {
        WarpNativeImage image = MachineImage(Layout(nameof(TestKernels.Loop)));
        using var fixture = new TransferFixture();
        Assert.AreEqual(0u, WarpNativeReductionDispatch.Reduce(image, [], WarpReductionOperation.WrappingSum,
            fixture.Operations, CancellationToken.None));
        Assert.AreEqual(uint.MaxValue, WarpNativeReductionDispatch.Reduce(image, [], WarpReductionOperation.Minimum,
            fixture.Operations, CancellationToken.None));
        Assert.AreEqual(0u, WarpNativeReductionDispatch.Reduce(image, [], WarpReductionOperation.Maximum,
            fixture.Operations, CancellationToken.None));
        Assert.AreEqual(42u, WarpNativeReductionDispatch.Reduce(image, [42], WarpReductionOperation.Maximum,
            fixture.Operations, CancellationToken.None));
        Assert.AreEqual(0, fixture.Launches);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => WarpNativeReductionDispatch.Reduce(image, [],
            WarpReductionOperation.Maximum, fixture.Operations, cancel.Token));
    }

    [TestMethod]
    public void ResidentLogicalExecutionUploadsOnceAndKeepsAllocationsAcrossQuanta()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        uint[] initial = layout.CreateInitialState(4, 1000);
        using var fixture = new TransferFixture();
        fixture.OnLaunch = arguments =>
        {
            IntPtr state = new(unchecked((long)TransferFixture.ReadPointer(arguments, 0)));
            int resultOffset = WarpLogicalMachineLayout.ResultOffset * sizeof(uint);
            Marshal.WriteInt32(state, resultOffset, Marshal.ReadInt32(state, resultOffset) + 1);
            TransferFixture.ConsumeAdmittedBudget(arguments, initial.Length);
        };
        using (var execution = new WarpNativeMachineExecution(MachineImage(layout), initial, [new uint[] { 9 }], [],
            1, 0, 4, fixture.Operations))
        {
            Assert.AreEqual(2, fixture.Uploads);
            Assert.HasCount(2, fixture.Allocations);
            uint[] first = execution.Resume(128);
            first[WarpLogicalMachineLayout.ResultOffset] = uint.MaxValue;
            uint[] second = execution.Resume(128);
            Assert.AreEqual(2u, second[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(0u, initial[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(2, fixture.Uploads);
            Assert.HasCount(2, fixture.Allocations);
        }

        Assert.HasCount(0, fixture.Allocations);
        Assert.AreEqual(2, fixture.MaximumAllocations);
        Assert.AreEqual(2, fixture.Launches);
    }

    [TestMethod]
    public void ResidentExecutionCancellationNeverPublishesAndCleanupIsIdempotent()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        using var fixture = new TransferFixture();
        using var cancel = new CancellationTokenSource();
        fixture.OnLaunch = arguments => TransferFixture.ConsumeAdmittedBudget(arguments, layout.GetStateWords(4));
        fixture.OnSynchronize = cancel.Cancel;
        var execution = new WarpNativeMachineExecution(MachineImage(layout), layout.CreateInitialState(4, 1000),
            [new uint[1]], [], 1, 0, 4, fixture.Operations);
        Assert.Throws<OperationCanceledException>(() => execution.Resume(128, cancel.Token));
        Assert.Throws<OperationCanceledException>(() => execution.Resume(128));
        Assert.Throws<OperationCanceledException>(() => execution.ResetBatch(layout.CreateInitialState(4, 1000), 1, 0));
        execution.Dispose();
        execution.Dispose();
        Assert.Throws<ObjectDisposedException>(() => execution.Resume(128));
        Assert.Throws<ObjectDisposedException>(() => execution.ResetBatch(layout.CreateInitialState(4, 1000), 1, 0));
        Assert.HasCount(0, fixture.Allocations);
        Assert.IsFalse(fixture.Quarantined);
    }

    [TestMethod]
    public void ResidentBatchesUploadInputsOnceAndPreserveExactLastBatchReadback()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        uint[] single = layout.CreateInitialState(4, 1000);
        uint[] initial = [.. single, .. single];
        int stride = single.Length;
        using var fixture = new TransferFixture();
        var statePointers = new List<ulong>();
        var inputPointers = new List<ulong>();
        var inputBases = new List<uint>();
        fixture.OnLaunch = arguments =>
        {
            ulong statesPointer = TransferFixture.ReadPointer(arguments, 0);
            ulong inputPointer = TransferFixture.ReadPointer(arguments, 1);
            uint count = TransferFixture.ReadInteger(arguments, 2);
            uint inputBase = TransferFixture.ReadInteger(arguments, 3);
            statePointers.Add(statesPointer);
            inputPointers.Add(inputPointer);
            inputBases.Add(inputBase);
            TransferFixture.ConsumeAdmittedBudget(arguments, stride);
            for (uint worker = 0; worker < count; worker++)
            {
                int value = Marshal.ReadInt32(new IntPtr(unchecked((long)inputPointer)), checked((int)(inputBase + worker) * sizeof(uint)));
                int offset = checked((int)worker * stride + WarpLogicalMachineLayout.ResultOffset) * sizeof(uint);
                Marshal.WriteInt32(new IntPtr(unchecked((long)statesPointer)), offset, value);
            }
        };
        using (var execution = new WarpNativeMachineExecution(MachineImage(layout), initial, [new uint[] { 10, 20, 30, 40 }], [],
            2, 0, 4, fixture.Operations))
        {
            uint[] first = execution.Resume(128);
            Assert.AreEqual(10u, first[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(20u, first[stride + WarpLogicalMachineLayout.ResultOffset]);
            execution.ResetBatch(single, 1, 3);
            uint[] last = execution.Resume(128);
            Assert.HasCount(stride, last);
            Assert.AreEqual(40u, last[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(3, fixture.Uploads);
            Assert.AreEqual(1, fixture.UploadedPointers.Count(pointer => pointer == inputPointers[0]));
            Assert.AreEqual(statePointers[0], statePointers[1]);
            Assert.AreEqual(inputPointers[0], inputPointers[1]);
            CollectionAssert.AreEqual(new uint[] { 0, 3 }, inputBases);
            Assert.AreEqual(2, fixture.MaximumAllocations);
        }

        Assert.HasCount(0, fixture.Allocations);
    }

    [TestMethod]
    public void ResidentBatchAdmissionRejectsInvalidCapacityAndStateBeforeAnyUpload()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        uint[] single = layout.CreateInitialState(4, 1000);
        using var fixture = new TransferFixture();
        using var execution = new WarpNativeMachineExecution(MachineImage(layout), [.. single, .. single],
            [new uint[] { 10, 20, 30, 40 }], [], 2, 0, 4, fixture.Operations);
        WarpHostException capacity = Assert.Throws<WarpHostException>(() => execution.ResetBatch([.. single, .. single, .. single], 3, 0));
        Assert.AreEqual("WRPNATIVE1005", capacity.Code, StringComparer.Ordinal);
        Assert.Throws<WarpHostException>(() => execution.ResetBatch(single, 1, 4));
        Assert.Throws<WarpHostException>(() => execution.ResetBatch(new uint[single.Length - 1], 1, 0));
        uint[] invalid = (uint[])single.Clone();
        invalid[WarpLogicalMachineLayout.StatusOffset] = uint.MaxValue;
        Assert.Throws<WarpHostException>(() => execution.ResetBatch(invalid, 1, 0));
        Assert.AreEqual(2, fixture.Uploads);
        Assert.AreEqual(0, fixture.Launches);
        Assert.IsFalse(fixture.Quarantined);
        execution.ResetBatch([], 0, 4);
        Assert.HasCount(0, execution.Resume(128));
        Assert.AreEqual(2, fixture.Uploads);
        execution.ResetBatch(single, 1, 0);
        Assert.AreEqual(3, fixture.Uploads);
    }

    [TestMethod]
    public void ResidentBatchStagingFailureQuarantinesAndStillReleasesEveryAllocation()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        uint[] single = layout.CreateInitialState(4, 1000);
        using var fixture = new TransferFixture();
        using (var execution = new WarpNativeMachineExecution(MachineImage(layout), single, [new uint[2]], [],
            1, 0, 4, fixture.Operations))
        {
            fixture.OnUpload = () => throw new WarpHostException("fixture", "Native state staging failed.");
            Assert.Throws<WarpHostException>(() => execution.ResetBatch(single, 1, 1));
            Assert.IsTrue(fixture.Quarantined);
            Assert.AreEqual(0, fixture.Launches);
            Assert.HasCount(2, fixture.Allocations);
        }

        Assert.HasCount(0, fixture.Allocations);
    }

    [TestMethod]
    [DataRow(WarpLogicalMachineLayout.StatusOffset, uint.MaxValue)]
    [DataRow(WarpLogicalMachineLayout.DepthOffset, 0u)]
    [DataRow(WarpLogicalMachineLayout.DepthOffset, 5u)]
    [DataRow(WarpLogicalMachineLayout.FaultKindOffset, 7u)]
    [DataRow(WarpLogicalMachineLayout.RemainingStepsHighOffset, 0x80000000u)]
    [DataRow(WarpLogicalMachineLayout.RemainingStepsLowOffset, 1001u)]
    [DataRow(WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameProgramCounterOffset, uint.MaxValue)]
    [DataRow(WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameFunctionOffset, uint.MaxValue)]
    public void CorruptNativeControlReadbackIsQuarantinedBeforePublication(int field, uint value)
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        uint[] initial = layout.CreateInitialState(4, 1000);
        using var fixture = new TransferFixture();
        fixture.OnLaunch = arguments => Marshal.WriteInt32(new IntPtr(unchecked((long)TransferFixture.ReadPointer(arguments, 0))),
            field * sizeof(uint), unchecked((int)value));
        using var execution = new WarpNativeMachineExecution(MachineImage(layout), initial, [new uint[1]], [], 1, 0, 4, fixture.Operations);
        WarpHostException fault = Assert.Throws<WarpHostException>(() => execution.Resume(128));
        Assert.AreEqual("WRPNATIVE1007", fault.Code, StringComparer.Ordinal);
        Assert.IsTrue(fixture.Quarantined);
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, initial[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual("WRPNATIVE1006", Assert.Throws<WarpHostException>(() => execution.Resume(128)).Code, StringComparer.Ordinal);
    }

    [TestMethod]
    public void NativeContinuationAdmissionMatchesCoreRootReturnSlotAndSignedBudgetValidation()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Call));
        WarpNativeImage image = MachineImage(layout);
        uint[] initial = layout.CreateInitialState(4, 1000);
        uint helperPc = checked((uint)layout.Nodes.First(node => node.Function == 1).ProgramCounter);
        initial[WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameFunctionOffset] = 1;
        initial[WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameProgramCounterOffset] = helperPc;
        Assert.Throws<WarpHostException>(() => WarpNativeMachineLaunch.Admit(image, initial, [new uint[1]], [], 1, 0, 4, 128));
        initial = layout.CreateInitialState(4, 1000);
        initial[WarpLogicalMachineLayout.DepthOffset] = 2;
        int callee = WarpLogicalMachineLayout.HeaderWords + layout.FrameWords;
        initial[callee + WarpLogicalMachineLayout.FrameFunctionOffset] = 1;
        initial[callee + WarpLogicalMachineLayout.FrameProgramCounterOffset] = helperPc;
        initial[callee + WarpLogicalMachineLayout.FrameReturnValueOffset] = uint.MaxValue;
        Assert.Throws<WarpHostException>(() => WarpNativeMachineLaunch.Admit(image, initial, [new uint[1]], [], 1, 0, 4, 128));
        initial = layout.CreateInitialState(4, 1000);
        initial[WarpLogicalMachineLayout.RemainingStepsHighOffset] = uint.MaxValue;
        Assert.Throws<WarpHostException>(() => WarpNativeMachineLaunch.Admit(image, initial, [new uint[1]], [], 1, 0, 4, 128));
    }

    [TestMethod]
    public void NativeReturnedBudgetCannotIncreaseAfterAValidPriorQuantum()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        using var fixture = new TransferFixture();
        fixture.OnLaunch = arguments => Marshal.WriteInt32(new IntPtr(unchecked((long)TransferFixture.ReadPointer(arguments, 0))),
            WarpLogicalMachineLayout.RemainingStepsLowOffset * sizeof(uint), fixture.Launches == 1 ? 900 : 901);
        using var execution = new WarpNativeMachineExecution(MachineImage(layout), layout.CreateInitialState(4, 1000),
            [new uint[1]], [], 1, 0, 4, fixture.Operations);
        uint[] first = execution.Resume(128);
        Assert.AreEqual(900u, first[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        Assert.Throws<WarpHostException>(() => execution.Resume(128));
        Assert.IsTrue(fixture.Quarantined);
    }

    [TestMethod]
    public void NativeRunnableQuantumCannotAvoidItsFiniteAdmittedStepBudget()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        using var fixture = new TransferFixture();
        using var execution = new WarpNativeMachineExecution(MachineImage(layout), layout.CreateInitialState(4, 1000),
            [new uint[1]], [], 1, 0, 4, fixture.Operations);
        WarpHostException fault = Assert.Throws<WarpHostException>(() => execution.Resume(128));
        Assert.AreEqual("WRPNATIVE1007", fault.Code, StringComparer.Ordinal);
        Assert.IsTrue(fixture.Quarantined);
    }

    [TestMethod]
    public void CancellationAfterValidatedLogicalFaultReadbackStillQuarantinesTheNativeContext()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        uint[] initial = layout.CreateInitialState(4, 1000);
        WarpLogicalMachineNode node = layout.Nodes[layout.GetBlockEntry(0, 0)];
        using var fixture = new TransferFixture();
        using var cancel = new CancellationTokenSource();
        fixture.OnLaunch = arguments =>
        {
            IntPtr states = new(unchecked((long)TransferFixture.ReadPointer(arguments, 0)));
            Marshal.WriteInt32(states, WarpLogicalMachineLayout.StatusOffset * sizeof(uint), (int)WarpLogicalMachineLayout.Faulted);
            Marshal.WriteInt32(states, WarpLogicalMachineLayout.FaultKindOffset * sizeof(uint), (int)WarpLogicalMachineLayout.StepLimitFault);
            Marshal.WriteInt32(states, WarpLogicalMachineLayout.FaultFunctionOffset * sizeof(uint), node.Function);
            Marshal.WriteInt32(states, WarpLogicalMachineLayout.FaultBlockOffset * sizeof(uint), node.Block);
        };
        fixture.OnReadback = cancel.Cancel;
        using var execution = new WarpNativeMachineExecution(MachineImage(layout), initial, [new uint[1]], [], 1, 0, 4, fixture.Operations);
        Assert.Throws<OperationCanceledException>(() => execution.Resume(128, cancel.Token));
        Assert.IsTrue(fixture.Quarantined);
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, initial[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual("WRPNATIVE1006", Assert.Throws<WarpHostException>(() => execution.Resume(128)).Code, StringComparer.Ordinal);
    }

    [TestMethod]
    public void NativeReturnedFaultMustReferenceItsVerifiedCurrentBlock()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        using var fixture = new TransferFixture();
        fixture.OnLaunch = arguments =>
        {
            IntPtr states = new(unchecked((long)TransferFixture.ReadPointer(arguments, 0)));
            Marshal.WriteInt32(states, WarpLogicalMachineLayout.StatusOffset * sizeof(uint), (int)WarpLogicalMachineLayout.Faulted);
            Marshal.WriteInt32(states, WarpLogicalMachineLayout.FaultKindOffset * sizeof(uint), (int)WarpLogicalMachineLayout.StepLimitFault);
            Marshal.WriteInt32(states, WarpLogicalMachineLayout.FaultBlockOffset * sizeof(uint), -1);
        };
        using var execution = new WarpNativeMachineExecution(MachineImage(layout), layout.CreateInitialState(4, 1000),
            [new uint[1]], [], 1, 0, 4, fixture.Operations);
        Assert.Throws<WarpHostException>(() => execution.Resume(128));
        Assert.IsTrue(fixture.Quarantined);
    }

    [TestMethod]
    public void ReductionResourcesAreAdmittedBeforeAnyMapOrDevicePass()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        WarpNativeTarget tiny = new(WarpBackendKind.NVPTX, "sm_80", "tiny-device", "runtime", 256, 1, 32);
        var image = new WarpNativeImage(tiny, WarpNativeImageFormat.Ptx, WarpPortableMachineEmitter.EntryPoint,
            [1], "fixture", "fixture", layout.Kernel.InputBufferCount, layout.Kernel.ScalarArgumentCount, layout);
        WarpHostException resource = Assert.Throws<WarpHostException>(() =>
            WarpNativeReductionDispatch.ValidateAdmission(image, 9, WarpReductionOperation.WrappingSum));
        Assert.AreEqual("WRPNATIVE1005", resource.Code, StringComparer.Ordinal);
        WarpNativeReductionDispatch.ValidateAdmission(image, 0, WarpReductionOperation.Minimum);
        WarpNativeReductionDispatch.ValidateAdmission(image, 1, WarpReductionOperation.Minimum);
    }

    [TestMethod]
    public void KernelArgumentCapacityIsInTheTargetIdentityAndCheckedBeforeDispatch()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        WarpNativeTarget accepted = new(WarpBackendKind.NVPTX, "sm_80", "device", "runtime", 256, 1, 1UL << 20, 32);
        WarpNativeTarget rejected = new(WarpBackendKind.NVPTX, "sm_80", "device", "runtime", 256, 1, 1UL << 20, 31);
        Assert.AreNotEqual(accepted.CacheIdentity, rejected.CacheIdentity, StringComparer.Ordinal);
        Assert.AreEqual(32UL, WarpNativeArgumentLayout.GetPackedBytes(1, 0, machine: true));
        var image = new WarpNativeImage(accepted, WarpNativeImageFormat.Ptx, WarpPortableMachineEmitter.EntryPoint,
            [1], "fixture", "fixture", 1, 0, layout);
        _ = WarpNativeMachineLaunch.Admit(image, layout.CreateInitialState(4, 1000), [new uint[1]], [], 1, 0, 4, 128);
        var tooSmall = new WarpNativeImage(rejected, WarpNativeImageFormat.Ptx, WarpPortableMachineEmitter.EntryPoint,
            [1], "fixture", "fixture", 1, 0, layout);
        WarpHostException failure = Assert.Throws<WarpHostException>(() => WarpNativeMachineLaunch.Admit(tooSmall,
            layout.CreateInitialState(4, 1000), [new uint[1]], [], 1, 0, 4, 128));
        Assert.AreEqual("WRPNATIVE1005", failure.Code, StringComparer.Ordinal);
        _ = WarpNativeLaunch.Admit(accepted, [new uint[1]], 1, false, 3);
        Assert.Throws<WarpHostException>(() => WarpNativeLaunch.Admit(accepted, [new uint[1]], 1, false, 4));
    }

    private static WarpLogicalMachineLayout Layout(string method) => new(new WarpBuildPipeline()
        .CompileIntegerMap(typeof(TestKernels).GetMethod(method)!, 1).Kernel);

    private static WarpNativeImage MachineImage(WarpLogicalMachineLayout layout) =>
        new(Target(WarpBackendKind.NVPTX, "sm_80"), WarpNativeImageFormat.Ptx, WarpPortableMachineEmitter.EntryPoint,
            [1], "source", "toolchain", layout.Kernel.InputBufferCount, layout.Kernel.ScalarArgumentCount, layout);

    [TestMethod]
    public void NativeHelperProgressUsesSeparateMonotonicOperationalCounter()
    {
        WarpLogicalMachineLayout layout = WarpLogicalFrameKernels.CreateHelperLoop();
        using var fixture = new TransferFixture();
        fixture.OnLaunch = arguments => Marshal.WriteInt32(new IntPtr(unchecked((long)TransferFixture.ReadPointer(arguments, 0))),
            WarpLogicalMachineLayout.UsedOperationsLowOffset * sizeof(uint), fixture.Launches);
        using var execution = new WarpNativeMachineExecution(MachineImage(layout), layout.CreateInitialState(1, 1),
            [new uint[1]], [], 1, 0, 1, fixture.Operations);
        Assert.AreEqual(1u, execution.Resume(layout.MaximumBlockCost)[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        Assert.AreEqual(2u, execution.Resume(layout.MaximumBlockCost)[WarpLogicalMachineLayout.UsedOperationsLowOffset]);
        Assert.IsFalse(fixture.Quarantined);
        fixture.OnLaunch = arguments => Marshal.WriteInt32(new IntPtr(unchecked((long)TransferFixture.ReadPointer(arguments, 0))),
            WarpLogicalMachineLayout.UsedOperationsLowOffset * sizeof(uint), 1);
        WarpHostException error = Assert.ThrowsExactly<WarpHostException>(() => execution.Resume(layout.MaximumBlockCost));
        Assert.AreEqual("WRPNATIVE1007", error.Code, StringComparer.Ordinal);
        Assert.IsTrue(fixture.Quarantined);
    }

    [TestMethod]
    public void NativeHelperCannotYieldWithoutSourceOrOperationalProgress()
    {
        WarpLogicalMachineLayout layout = WarpLogicalFrameKernels.CreateHelperLoop();
        using var fixture = new TransferFixture();
        using var execution = new WarpNativeMachineExecution(MachineImage(layout), layout.CreateInitialState(1, 1),
            [new uint[1]], [], 1, 0, 1, fixture.Operations);
        WarpHostException error = Assert.ThrowsExactly<WarpHostException>(() => execution.Resume(layout.MaximumBlockCost));
        Assert.AreEqual("WRPNATIVE1007", error.Code, StringComparer.Ordinal);
        Assert.IsTrue(fixture.Quarantined);
    }

    // A host-memory transport fixture tests orchestration only. It is not hardware or native CLR conformance evidence.
    private sealed class TransferFixture : IDisposable
    {
        public Dictionary<ulong, WarpNativeBlock> Allocations { get; } = [];
        public Action<IntPtr>? OnLaunch { get; set; }
        public Action? OnUpload { get; set; }
        public Action? OnSynchronize { get; set; }
        public Action? OnReadback { get; set; }
        public int Launches { get; private set; }
        public int MaximumAllocations { get; private set; }
        public int Uploads { get; private set; }
        public List<ulong> UploadedPointers { get; } = [];
        public bool Quarantined { get; private set; }
        public WarpMachineMemoryOperations Operations => new(Allocate, Upload, Readback,
            (arguments, _, _) => { Launches++; OnLaunch?.Invoke(arguments); },
            () => OnSynchronize?.Invoke(), Free, () => Quarantined = true);

        public static ulong ReadPointer(IntPtr arguments, int index) => unchecked((ulong)Marshal.ReadInt64(
            Marshal.ReadIntPtr(arguments, index * IntPtr.Size)));

        public static uint ReadInteger(IntPtr arguments, int index) => unchecked((uint)Marshal.ReadInt32(
            Marshal.ReadIntPtr(arguments, index * IntPtr.Size)));

        public static void ConsumeAdmittedBudget(IntPtr arguments, int stride)
        {
            IntPtr state = new(unchecked((long)ReadPointer(arguments, 0)));
            uint count = ReadInteger(arguments, 2);
            for (int worker = 0; worker < count; worker++)
            {
                int offset = (worker * stride + WarpLogicalMachineLayout.RemainingStepsLowOffset) * sizeof(uint);
                Marshal.WriteInt64(state, offset, Marshal.ReadInt64(state, offset) - 1);
            }
        }

        private ulong Allocate(nuint bytes)
        {
            var block = new WarpNativeBlock(checked((int)bytes));
            ulong pointer = unchecked((ulong)block.Pointer.ToInt64());
            Allocations.Add(pointer, block);
            MaximumAllocations = Math.Max(MaximumAllocations, Allocations.Count);
            return pointer;
        }

        private void Upload(ulong destination, IntPtr source, nuint bytes)
        {
            OnUpload?.Invoke();
            Uploads++;
            UploadedPointers.Add(destination);
            Copy(new IntPtr(unchecked((long)destination)), source, bytes);
        }
        private void Readback(IntPtr destination, ulong source, nuint bytes)
        {
            Copy(destination, new IntPtr(unchecked((long)source)), bytes);
            OnReadback?.Invoke();
        }

        private static void Copy(IntPtr destination, IntPtr source, nuint bytes)
        {
            byte[] data = new byte[checked((int)bytes)];
            Marshal.Copy(source, data, 0, data.Length);
            Marshal.Copy(data, 0, destination, data.Length);
        }

        private void Free(ulong pointer)
        {
            Allocations[pointer].Dispose();
            Allocations.Remove(pointer);
        }

        public void Dispose()
        {
            foreach (WarpNativeBlock block in Allocations.Values) { block.Dispose(); }
            Allocations.Clear();
        }
    }

    private static WarpNativeTarget Target(WarpBackendKind backend, string architecture) =>
        new(backend, architecture, "test-device", "test-runtime", 1024, uint.MaxValue, 8UL * 1024 * 1024 * 1024);

    private static WarpNativeImage Image(WarpNativeTarget target, WarpNativeImageFormat format, byte[] content) =>
        new(target, format, WarpDeviceAbi.IntegerMapEntryPoint, content, "test-source", "test-toolchain");

    private static byte[] SpirVHeader() => [3, 2, 0x23, 7, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0];
}
