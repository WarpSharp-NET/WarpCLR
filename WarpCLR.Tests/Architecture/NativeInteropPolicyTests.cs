using System.Runtime.InteropServices;
using System.Text;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;
using WarpCLR.Sdk;

namespace WarpCLR.Tests.Architecture;

[TestClass]
public sealed class NativeInteropPolicyTests
{
    [TestMethod]
    public void Native_target_cannot_select_CoreCLR()
    {
        Assert.Throws<ArgumentException>(() => Target(WarpBackendKind.CoreCLR, "x64"));
    }

    [TestMethod]
    public void Native_target_rejects_arbitrary_architecture_and_argument_injection()
    {
        Assert.Throws<ArgumentException>(() => Target(WarpBackendKind.NVPTX, "sm_80; echo unsafe"));
        Assert.Throws<ArgumentException>(() => Target(WarpBackendKind.AMDGPU, "generic"));
        Assert.Throws<ArgumentException>(() => Target(WarpBackendKind.SPIRV, "vulkan"));
        Assert.Throws<ArgumentException>(() => Target(WarpBackendKind.AMDGPU, "gfx942 -filetype=asm"));
        Assert.Throws<ArgumentException>(() => Target(WarpBackendKind.AMDGPU, "gfx90a:xnack+:xnack-"));
        Assert.AreEqual("gfx90a:sramecc+:xnack-", Target(WarpBackendKind.AMDGPU, "gfx90a:sramecc+:xnack-").Architecture);
    }

    [TestMethod]
    public void Native_image_rejects_LLVM_text_in_place_of_binary()
    {
        byte[] text = Encoding.UTF8.GetBytes("define void @warp_integer_map() { ret void }");
        Assert.Throws<ArgumentException>(() => Image(Target(WarpBackendKind.AMDGPU, "gfx942"), WarpNativeImageFormat.Hsaco, text));
        Assert.Throws<ArgumentException>(() => Image(Target(WarpBackendKind.SPIRV, "opencl2.2-spirv1.2"), WarpNativeImageFormat.SpirV, text));
    }

    [TestMethod]
    public void Native_image_rejects_wrong_backend_binary_and_unknown_entrypoint()
    {
        Assert.Throws<ArgumentException>(() => Image(Target(WarpBackendKind.NVPTX, "sm_80"), WarpNativeImageFormat.SpirV, SpirVHeader()));
        Assert.Throws<ArgumentException>(() => new WarpNativeImage(Target(WarpBackendKind.NVPTX, "sm_80"),
            WarpNativeImageFormat.Ptx, "unverified_entry", [1], "source", "toolchain"));
    }

    [TestMethod]
    public void Binary_header_is_not_production_conformance_evidence()
    {
        WarpNativeImage image = Image(Target(WarpBackendKind.SPIRV, "opencl2.2-spirv1.2"), WarpNativeImageFormat.SpirV, SpirVHeader());
        Assert.AreEqual(WarpConformanceStatus.DevelopmentNonconforming, image.ConformanceStatus);
        Assert.IsFalse(image.SupportsPortableSafepoints);
        Assert.IsFalse(image.SupportsScalableReduction);
        WarpHostException error = Assert.Throws<WarpHostException>(image.RequireProductionAdmission);
        Assert.AreEqual("WRPNATIVE1008", error.Code);
    }

    [TestMethod]
    public void Native_image_owns_content_and_rejects_argument_schema_mismatch()
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
    public void Runtime_and_physical_target_identity_change_native_cache_identity()
    {
        WarpNativeTarget target = Target(WarpBackendKind.NVPTX, "sm_80");
        WarpNativeTarget otherRuntime = new(target.Backend, target.Architecture, target.DeviceIdentity, "driver-new",
            target.MaxWorkgroupSize, target.MaxGridX, target.GlobalMemoryBytes);
        WarpNativeTarget otherDevice = new(target.Backend, target.Architecture, "pci-other", target.RuntimeIdentity,
            target.MaxWorkgroupSize, target.MaxGridX, target.GlobalMemoryBytes);
        Assert.AreNotEqual(target.CacheIdentity, otherRuntime.CacheIdentity);
        Assert.AreNotEqual(target.CacheIdentity, otherDevice.CacheIdentity);
    }

    [TestMethod]
    public void Native_resource_admission_catches_grid_workgroup_and_memory_limits_before_launch()
    {
        WarpNativeTarget target = new(WarpBackendKind.NVPTX, "sm_80", "device", "runtime", 256, 1, 1024 * 1024);
        WarpNativeLaunch accepted = WarpNativeLaunch.Admit(target, [new uint[256]], 256, false);
        Assert.AreEqual(1u, accepted.GridX);
        Assert.AreEqual(256u, accepted.WorkgroupSize);
        Assert.AreEqual(2048UL, accepted.BytesRequired);
        Assert.AreEqual("WRPNATIVE1005", Assert.Throws<WarpHostException>(() =>
            WarpNativeLaunch.Admit(target, [new uint[257]], 257, false)).Code);
        WarpNativeTarget smallMemory = new(WarpBackendKind.NVPTX, "sm_80", "device", "runtime", 256, 100, 16);
        Assert.Throws<WarpHostException>(() => WarpNativeLaunch.Admit(smallMemory, [new uint[3]], 3, false));
        WarpNativeTarget smallBlock = new(WarpBackendKind.NVPTX, "sm_80", "device", "runtime", 128, 100, 1024);
        Assert.Throws<WarpHostException>(() => WarpNativeLaunch.Admit(smallBlock, [new uint[1]], 1, false));
    }

    [TestMethod]
    public void Native_resource_admission_validates_buffers_and_empty_dispatch_shapes()
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
    public void Native_argument_storage_preserves_full_width_pointers_and_uint_values()
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
    public async Task PTX_retains_development_status_but_is_retargeted_to_concrete_hardware()
    {
        WarpCompilation compilation = new WarpBuildPipeline().CompileIntegerMap(
            typeof(TestKernels).GetMethod(nameof(TestKernels.Combine))!, 2);
        WarpNativeTarget target = Target(WarpBackendKind.NVPTX, "sm_89");
        WarpNativeImage image = await new WarpNativeToolchain().CompileAsync(compilation.Artifacts[WarpBackendKind.NVPTX], target);
        StringAssert.Contains(Encoding.UTF8.GetString(image.Content.Span), ".target sm_89");
        Assert.AreEqual(2, image.InputBufferCount);
        Assert.AreEqual(2, image.ScalarArgumentCount);
        Assert.AreEqual(compilation.Artifacts[WarpBackendKind.NVPTX].ContentHash, image.SourceHash);
        Assert.IsFalse(image.SupportsPortableSafepoints);
    }

    [TestMethod]
    public async Task Native_toolchain_rejects_cross_backend_artifact_and_ambiguous_PTX()
    {
        WarpBackendArtifact ptx = new(WarpBackendKind.NVPTX, WarpArtifactFormat.NVPTX,
            WarpDeviceAbi.IntegerMapEntryPoint, Encoding.UTF8.GetBytes(".target sm_50\n.target sm_80\n"));
        var toolchain = new WarpNativeToolchain();
        await Assert.ThrowsAsync<WarpHostException>(() => toolchain.CompileAsync(ptx, Target(WarpBackendKind.AMDGPU, "gfx942")));
        await Assert.ThrowsAsync<WarpHostException>(() => toolchain.CompileAsync(ptx, Target(WarpBackendKind.NVPTX, "sm_89")));
    }

    [TestMethod]
    public async Task Missing_native_compiler_is_an_explicit_error_without_emulation()
    {
        WarpBackendArtifact llvm = new(WarpBackendKind.AMDGPU, WarpArtifactFormat.AMDGPULLVMIR,
            WarpDeviceAbi.IntegerMapEntryPoint, Encoding.UTF8.GetBytes("target triple = \"amdgcn-amd-amdhsa\"\n"));
        var toolchain = new WarpNativeToolchain(new WarpNativeToolchainOptions
        {
            LlvmAssembler = Path.Combine(Path.GetTempPath(), "missing-warpclr-tool-" + Guid.NewGuid().ToString("N")),
        });
        WarpHostException error = await Assert.ThrowsAsync<WarpHostException>(() => toolchain.CompileAsync(llvm,
            Target(WarpBackendKind.AMDGPU, "gfx942")));
        Assert.AreEqual("WRPNATIVE2001", error.Code);
    }

    [TestMethod]
    public async Task Native_process_does_not_interpret_shell_metacharacters_in_arguments()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The test process fixture requires Linux printf."); }
        WarpToolProcessResult result = await WarpToolProcess.RunAsync("/usr/bin/printf",
            ["%s", "literal ; $(echo never-run) | value"], null, TimeSpan.FromSeconds(10));
        Assert.AreEqual("literal ; $(echo never-run) | value", result.StandardOutput);
    }

    [TestMethod]
    public async Task Native_process_drains_and_bounds_large_diagnostics()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The test process fixture requires Linux tools."); }
        WarpToolProcessResult result = await WarpToolProcess.RunAsync("/bin/sh",
            ["-c", "head -c 200000 /dev/zero | tr '\\000' x"], null, TimeSpan.FromSeconds(10));
        Assert.IsTrue(result.StandardOutput.Length < 66000);
        StringAssert.Contains(result.StandardOutput, "diagnostic output truncated");
    }

    [TestMethod]
    public async Task Native_process_nonzero_exit_is_a_compilation_diagnostic()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The test process fixture requires Linux tools."); }
        WarpHostException error = await Assert.ThrowsAsync<WarpHostException>(() => WarpToolProcess.RunAsync("/bin/sh",
            ["-c", "printf 'compiler failed' >&2; exit 7"], null, TimeSpan.FromSeconds(10)));
        Assert.AreEqual("WRPNATIVE2002", error.Code);
        StringAssert.Contains(error.Message, "compiler failed");
        StringAssert.Contains(error.Message, "exit code 7");
    }

    [TestMethod]
    public async Task Native_process_timeout_and_caller_cancellation_are_distinct()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The test process fixture requires Linux sleep."); }
        WarpHostException error = await Assert.ThrowsAsync<WarpHostException>(() => WarpToolProcess.RunAsync("/bin/sleep",
            ["10"], null, TimeSpan.FromMilliseconds(100)));
        Assert.AreEqual("WRPNATIVE2004", error.Code);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() => WarpToolProcess.RunAsync("/bin/sleep",
            ["10"], null, TimeSpan.FromSeconds(10), cancel.Token));
    }

    [TestMethod]
    public async Task Native_process_deadline_also_bounds_inherited_output_pipes_after_parent_exit()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The test process fixture requires Linux tools."); }
        WarpHostException error = await Assert.ThrowsAsync<WarpHostException>(() => WarpToolProcess.RunAsync("/bin/sh",
            ["-c", "sleep 1 & exit 0"], null, TimeSpan.FromMilliseconds(100)));
        Assert.AreEqual("WRPNATIVE2004", error.Code);
    }

    [TestMethod]
    public void Native_driver_configuration_rejects_relative_library_paths_before_loading()
    {
        Assert.Throws<ArgumentException>(() => WarpNativeLibrary.Open("untrusted-driver.so", "ignored"));
    }

    [TestMethod]
    public void Hip_uses_explicitly_versioned_64bit_properties_layout()
    {
        Assert.AreEqual(1472, HipDevicePropertiesR0600.Size);
        Assert.AreEqual(288, HipDevicePropertiesR0600.TotalGlobalMemory);
        Assert.AreEqual(320, HipDevicePropertiesR0600.MaxThreadsPerBlock);
        Assert.AreEqual(336, HipDevicePropertiesR0600.MaxGridSize);
        Assert.AreEqual(1160, HipDevicePropertiesR0600.GcnArchitecture);
    }

    [TestMethod]
    public void Logical_machine_admission_validates_state_shape_and_continuation_before_native_dispatch()
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
    public void Logical_machine_image_has_control_ABI_and_cannot_use_legacy_dispatch_signature()
    {
        WarpNativeImage image = MachineImage(Layout(nameof(TestKernels.Loop)));
        Assert.AreEqual(WarpLogicalMachineLayout.Version, image.DeviceAbiVersion);
        Assert.IsTrue(image.SupportsPortableSafepoints);
        Assert.IsTrue(image.SupportsScalableReduction);
        Assert.AreEqual(WarpConformanceStatus.DevelopmentNonconforming, image.ConformanceStatus);
        image.RequireProductionAdmission();
        Assert.Throws<WarpHostException>(() => image.ValidateArguments([new uint[1]], [], false));
    }

    [TestMethod]
    public void Logical_machine_arguments_preserve_the_shared_portable_parameter_order()
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
    public void Logical_machine_transfer_is_transactional_and_releases_all_allocations()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        WarpNativeImage image = MachineImage(layout);
        uint[] state = layout.CreateInitialState(4, 1000);
        using var fixture = new TransferFixture();
        fixture.OnLaunch = arguments =>
        {
            ulong statesPointer = fixture.ReadPointer(arguments, 0);
            Marshal.WriteInt32(new IntPtr(unchecked((long)statesPointer)), WarpLogicalMachineLayout.StatusOffset * sizeof(uint),
                (int)WarpLogicalMachineLayout.Completed);
            Marshal.WriteInt32(new IntPtr(unchecked((long)statesPointer)), WarpLogicalMachineLayout.ResultOffset * sizeof(uint), 42);
            Assert.AreEqual(1u, fixture.ReadInteger(arguments, 2));
            Assert.AreEqual(0u, fixture.ReadInteger(arguments, 3));
            Assert.AreEqual(4u, fixture.ReadInteger(arguments, 4));
            Assert.AreEqual(128u, fixture.ReadInteger(arguments, 5));
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
    public void Logical_machine_native_failure_quarantines_context_and_cancellation_does_not_publish_state()
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
    public void Parallel_reduction_orchestrates_pairwise_resident_passes_without_host_combination()
    {
        WarpNativeImage image = MachineImage(Layout(nameof(TestKernels.Loop)));
        using var fixture = new TransferFixture();
        var inputCounts = new List<uint>();
        fixture.OnLaunch = arguments =>
        {
            ulong output = fixture.ReadPointer(arguments, 0);
            ulong input = fixture.ReadPointer(arguments, 1);
            uint count = fixture.ReadInteger(arguments, 2);
            Assert.AreEqual((uint)WarpReductionOperation.WrappingSum, fixture.ReadInteger(arguments, 3));
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
    public void Reduction_empty_identity_and_singleton_need_no_device_pass_and_preserve_cancellation()
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
    public void Resident_logical_execution_uploads_once_and_keeps_allocations_across_quanta()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        uint[] initial = layout.CreateInitialState(4, 1000);
        using var fixture = new TransferFixture();
        fixture.OnLaunch = arguments =>
        {
            IntPtr state = new(unchecked((long)fixture.ReadPointer(arguments, 0)));
            int resultOffset = WarpLogicalMachineLayout.ResultOffset * sizeof(uint);
            Marshal.WriteInt32(state, resultOffset, Marshal.ReadInt32(state, resultOffset) + 1);
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
    public void Resident_execution_cancellation_never_publishes_and_cleanup_is_idempotent()
    {
        WarpLogicalMachineLayout layout = Layout(nameof(TestKernels.Loop));
        using var fixture = new TransferFixture();
        using var cancel = new CancellationTokenSource();
        fixture.OnSynchronize = cancel.Cancel;
        var execution = new WarpNativeMachineExecution(MachineImage(layout), layout.CreateInitialState(4, 1000),
            [new uint[1]], [], 1, 0, 4, fixture.Operations);
        Assert.Throws<OperationCanceledException>(() => execution.Resume(128, cancel.Token));
        Assert.Throws<OperationCanceledException>(() => execution.Resume(128));
        execution.Dispose();
        execution.Dispose();
        Assert.Throws<ObjectDisposedException>(() => execution.Resume(128));
        Assert.HasCount(0, fixture.Allocations);
        Assert.IsFalse(fixture.Quarantined);
    }

    private static WarpLogicalMachineLayout Layout(string method) => new(new WarpBuildPipeline()
        .CompileIntegerMap(typeof(TestKernels).GetMethod(method)!, 1).Kernel);

    private static WarpNativeImage MachineImage(WarpLogicalMachineLayout layout) =>
        new(Target(WarpBackendKind.NVPTX, "sm_80"), WarpNativeImageFormat.Ptx, WarpPortableMachineEmitter.EntryPoint,
            [1], "source", "toolchain", layout.Kernel.InputBufferCount, layout.Kernel.ScalarArgumentCount, layout);

    // A host-memory transport fixture tests orchestration only. It is not hardware or native CLR conformance evidence.
    private sealed class TransferFixture : IDisposable
    {
        public Dictionary<ulong, WarpNativeBlock> Allocations { get; } = [];
        public Action<IntPtr>? OnLaunch { get; set; }
        public Action? OnSynchronize { get; set; }
        public int Launches { get; private set; }
        public int MaximumAllocations { get; private set; }
        public int Uploads { get; private set; }
        public bool Quarantined { get; private set; }
        public WarpMachineMemoryOperations Operations => new(Allocate, Upload, Readback,
            (arguments, _, _) => { Launches++; OnLaunch?.Invoke(arguments); },
            () => OnSynchronize?.Invoke(), Free, () => Quarantined = true);

        public ulong ReadPointer(IntPtr arguments, int index) => unchecked((ulong)Marshal.ReadInt64(
            Marshal.ReadIntPtr(arguments, index * IntPtr.Size)));

        public uint ReadInteger(IntPtr arguments, int index) => unchecked((uint)Marshal.ReadInt32(
            Marshal.ReadIntPtr(arguments, index * IntPtr.Size)));

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
            Uploads++;
            Copy(new IntPtr(unchecked((long)destination)), source, bytes);
        }
        private void Readback(IntPtr destination, ulong source, nuint bytes) => Copy(destination, new IntPtr(unchecked((long)source)), bytes);

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
