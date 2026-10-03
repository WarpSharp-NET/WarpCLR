using System.Runtime.InteropServices;
using System.Text;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpOpenClNativeDriver : IWarpNativeDriver
{
    private const ulong GpuDeviceType = 4;
    private const int DeviceNotFound = -1;
    private readonly Lock gate = new();
    private readonly WarpNativeLibrary library;
    private readonly OpenClApi api;
    private readonly IntPtr device;
    private readonly HashSet<Module> modules = [];
    private IntPtr context;
    private IntPtr queue;
    private bool disposed;
    private bool faulted;

    private WarpOpenClNativeDriver(int deviceOrdinal, string? libraryPath)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deviceOrdinal);
        if (IntPtr.Size != 8)
        {
            throw new PlatformNotSupportedException("The portable SPIR64 ABI requires a 64-bit host process.");
        }

        library = WarpNativeLibrary.Open(libraryPath, OperatingSystem.IsWindows() ? "OpenCL.dll" : "libOpenCL.so.1");
        try
        {
            api = new OpenClApi(library);
            device = FindGpuDevice(api, deviceOrdinal);
            if ((Query64(0x1000) & GpuDeviceType) == 0 || Query32(0x100d) != 64 || Query32(0x1026) != 1)
            {
                throw new WarpHostException("WRPNATIVE1005", "The OpenCL target must be a little-endian 64-bit physical GPU.");
            }

            string ilVersion = QueryString(0x105b);
            if (!ilVersion.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(version => version is "SPIR-V_1.2" or "SPIR-V_1.3" or "SPIR-V_1.4" or "SPIR-V_1.5" or "SPIR-V_1.6"))
            {
                throw new WarpHostException("WRPNATIVE1005", "The GPU does not admit the selected SPIR-V 1.2 execution environment.");
            }

            string name = QueryString(0x102b);
            string vendor = QueryString(0x102c);
            string runtime = QueryString(0x102d) + "/" + QueryString(0x102f) + "/" + ilVersion;
            ulong maxWorkgroup = Query64(0x1004);
            Target = new WarpNativeTarget(WarpBackendKind.SPIRV, "opencl2.2-spirv1.2",
                vendor + "/" + name + "/ordinal=" + deviceOrdinal, runtime,
                checked((uint)Math.Min(maxWorkgroup, uint.MaxValue)), uint.MaxValue, Query64(0x101f));
            using var deviceList = new WarpNativeBlock(IntPtr.Size);
            Marshal.WriteIntPtr(deviceList.Pointer, device);
            context = api.CreateContext(IntPtr.Zero, 1, deviceList.Pointer, IntPtr.Zero, IntPtr.Zero, out int error);
            Check(error, "clCreateContext");
            if (context == IntPtr.Zero) { throw new WarpHostException("WRPNATIVE1007", "OpenCL returned a null context."); }
            queue = api.CreateQueue(context, device, IntPtr.Zero, out error);
            Check(error, "clCreateCommandQueueWithProperties");
            if (queue == IntPtr.Zero) { throw new WarpHostException("WRPNATIVE1007", "OpenCL returned a null queue."); }
        }
        catch
        {
            if (queue != IntPtr.Zero) { api?.ReleaseQueue(queue); }
            if (context != IntPtr.Zero) { api?.ReleaseContext(context); }
            library.Dispose();
            throw;
        }
    }

    public WarpNativeTarget Target { get; }
    public static WarpOpenClNativeDriver Open(int deviceOrdinal = 0, string? libraryPath = null) => new(deviceOrdinal, libraryPath);

    public IWarpNativeModule Load(WarpNativeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        lock (gate)
        {
            EnsureUsable();
            if (image.Target != Target || image.Format != WarpNativeImageFormat.SpirV)
            {
                throw new WarpHostException("WRPNATIVE1003", "The SPIR-V module selects a different concrete OpenCL target.");
            }

            using var bytes = new WarpNativeBlock(image.Content.Length);
            Marshal.Copy(image.Content.ToArray(), 0, bytes.Pointer, image.Content.Length);
            IntPtr program = api.CreateProgramWithIl(context, bytes.Pointer, checked((nuint)image.Content.Length), out int error);
            Check(error, "clCreateProgramWithIL");
            if (program == IntPtr.Zero) { throw new WarpHostException("WRPNATIVE1007", "OpenCL returned a null program."); }
            IntPtr kernel = IntPtr.Zero;
            IntPtr reductionKernel = IntPtr.Zero;
            try
            {
                using var deviceList = new WarpNativeBlock(IntPtr.Size);
                Marshal.WriteIntPtr(deviceList.Pointer, device);
                error = api.BuildProgram(program, 1, deviceList.Pointer, string.Empty, IntPtr.Zero, IntPtr.Zero);
                if (error != 0)
                {
                    throw new WarpHostException("WRPNATIVE2005", $"OpenCL SPIR-V program build failed ({error}): {GetBuildLog(program)}");
                }

                kernel = api.CreateKernel(program, image.EntryPoint, out error);
                Check(error, "clCreateKernel");
                ValidateKernel(kernel, checked(image.InputBufferCount + image.ScalarArgumentCount +
                    (image.MachineLayout is null ? 2 : 5)));
                if (image.SupportsScalableReduction) { reductionKernel = CreateReductionKernel(program); }

                var loaded = new Module(this, image, program, kernel, reductionKernel);
                modules.Add(loaded);
                return loaded;
            }
            catch
            {
                if (kernel != IntPtr.Zero) { api.ReleaseKernel(kernel); }
                if (reductionKernel != IntPtr.Zero) { api.ReleaseKernel(reductionKernel); }
                api.ReleaseProgram(program);
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) { return; }
            foreach (Module module in modules.ToArray()) { module.Release(); }
            api.ReleaseQueue(queue);
            api.ReleaseContext(context);
            queue = IntPtr.Zero;
            context = IntPtr.Zero;
            disposed = true;
            library.Dispose();
        }
    }

    private static IntPtr FindGpuDevice(OpenClApi api, int deviceOrdinal)
    {
        Check(api.GetPlatformIds(0, IntPtr.Zero, out uint platformCount), "clGetPlatformIDs");
        if (platformCount > 1024)
        {
            throw new WarpHostException("WRPNATIVE1001", "The OpenCL platform list exceeds the runtime's admission limit.");
        }

        using var platforms = new WarpNativeBlock(checked((int)Math.Max(platformCount, 1) * IntPtr.Size));
        Check(api.GetPlatformIds(platformCount, platforms.Pointer, out _), "clGetPlatformIDs");
        var devices = new List<IntPtr>();
        for (int platformIndex = 0; platformIndex < platformCount; platformIndex++)
        {
            IntPtr platform = Marshal.ReadIntPtr(platforms.Pointer, platformIndex * IntPtr.Size);
            int result = api.GetDeviceIds(platform, GpuDeviceType, 0, IntPtr.Zero, out uint count);
            if (result == DeviceNotFound) { continue; }
            Check(result, "clGetDeviceIDs(GPU)");
            if (count > 1024)
            {
                throw new WarpHostException("WRPNATIVE1001", "The OpenCL GPU list exceeds the runtime's admission limit.");
            }

            using var list = new WarpNativeBlock(checked((int)Math.Max(count, 1) * IntPtr.Size));
            Check(api.GetDeviceIds(platform, GpuDeviceType, count, list.Pointer, out _), "clGetDeviceIDs(GPU)");
            for (int index = 0; index < count; index++) { devices.Add(Marshal.ReadIntPtr(list.Pointer, index * IntPtr.Size)); }
        }

        if (deviceOrdinal >= devices.Count)
        {
            throw new WarpHostException("WRPNATIVE1001", "The selected OpenCL GPU is absent; CPU OpenCL devices are never selected.");
        }

        return devices[deviceOrdinal];
    }

    private void ValidateKernel(IntPtr kernel, int expectedArguments)
    {
        using var limit = new WarpNativeBlock(sizeof(ulong));
        Check(api.GetKernelWorkgroupInfo(kernel, device, 0x11b0, sizeof(ulong), limit.Pointer, out _), "clGetKernelWorkGroupInfo");
        if (unchecked((ulong)Marshal.ReadInt64(limit.Pointer)) < WarpDeviceAbi.IntegerMapWorkgroupSize)
        {
            throw new WarpHostException("WRPNATIVE1005", "The compiled OpenCL kernel cannot admit the portable workgroup size.");
        }

        Check(api.GetKernelInfo(kernel, 0x1191, sizeof(uint), limit.Pointer, out _), "clGetKernelInfo(NUM_ARGS)");
        if (Marshal.ReadInt32(limit.Pointer) != expectedArguments)
        {
            throw new WarpHostException("WRPNATIVE1003", "The compiled OpenCL entry point does not match its native argument schema.");
        }
    }

    private IntPtr CreateReductionKernel(IntPtr program)
    {
        IntPtr kernel = api.CreateKernel(program, WarpPortableMachineEmitter.ReductionEntryPoint, out int error);
        Check(error, "clCreateKernel(reduction)");
        try { ValidateKernel(kernel, 4); return kernel; }
        catch { api.ReleaseKernel(kernel); throw; }
    }

    private uint Query32(uint property)
    {
        using var value = new WarpNativeBlock(sizeof(uint));
        Check(api.GetDeviceInfo(device, property, sizeof(uint), value.Pointer, out _), "clGetDeviceInfo");
        return unchecked((uint)Marshal.ReadInt32(value.Pointer));
    }

    private ulong Query64(uint property)
    {
        using var value = new WarpNativeBlock(sizeof(ulong));
        Check(api.GetDeviceInfo(device, property, sizeof(ulong), value.Pointer, out _), "clGetDeviceInfo");
        return unchecked((ulong)Marshal.ReadInt64(value.Pointer));
    }

    private string QueryString(uint property)
    {
        Check(api.GetDeviceInfo(device, property, 0, IntPtr.Zero, out nuint size), "clGetDeviceInfo(size)");
        if (size == 0 || size > 65536)
        {
            throw new WarpHostException("WRPNATIVE1007", "An OpenCL identity property has an invalid length.");
        }

        using var value = new WarpNativeBlock(checked((int)size));
        Check(api.GetDeviceInfo(device, property, size, value.Pointer, out _), "clGetDeviceInfo(value)");
        return Marshal.PtrToStringUTF8(value.Pointer, checked((int)size)).TrimEnd('\0');
    }

    private string GetBuildLog(IntPtr program)
    {
        Check(api.GetProgramBuildInfo(program, device, 0x1183, 0, IntPtr.Zero, out nuint size), "clGetProgramBuildInfo(size)");
        if (size == 0) { return string.Empty; }
        if (size > 1048576) { return "[build log exceeds diagnostic admission limit]"; }
        using var value = new WarpNativeBlock(checked((int)size));
        Check(api.GetProgramBuildInfo(program, device, 0x1183, size, value.Pointer, out _), "clGetProgramBuildInfo(value)");
        return Marshal.PtrToStringUTF8(value.Pointer, checked((int)size)).TrimEnd('\0');
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (faulted) { throw new WarpHostException("WRPNATIVE1006", "The OpenCL context is faulted and must be disposed."); }
    }

    private static void Check(int error, string operation)
    {
        if (error != 0)
        {
            throw new WarpHostException(error is -4 or -5 or -6 ? "WRPNATIVE1005" : "WRPNATIVE1007",
                $"OpenCL operation '{operation}' failed with driver result {error}.");
        }
    }

    private sealed class Module(WarpOpenClNativeDriver owner, WarpNativeImage image, IntPtr program, IntPtr kernel, IntPtr reductionKernel)
        : IWarpNativeModule
    {
        private bool released;
        private readonly HashSet<WarpBoundNativeMachineExecution> executions = [];

        public WarpNativeImage Image { get; } = image;
        public bool IsFaulted { get { lock (owner.gate) { return owner.faulted; } } }

        public IWarpNativeMachineExecution CreateMachineExecution(uint[] states, IReadOnlyList<uint[]> inputs,
            IReadOnlyList<uint> scalars, int itemCount, int inputBase, int maximumCallDepth)
        {
            lock (owner.gate)
            {
                owner.EnsureUsable();
                ObjectDisposedException.ThrowIf(released, this);
                var operations = CreateOperations(kernel, Image.InputBufferCount + Image.ScalarArgumentCount + 5, Image.InputBufferCount + 1);
                var execution = new WarpNativeMachineExecution(Image, states, inputs, scalars, itemCount, inputBase, maximumCallDepth, operations);
                var bound = new WarpBoundNativeMachineExecution(execution,
                    action => WithQueue(action, checkFault: true), action => WithQueue(action, checkFault: false),
                    item => executions.Remove(item));
                executions.Add(bound);
                return bound;
            }
        }

        private void WithQueue(Action action, bool checkFault)
        {
            lock (owner.gate)
            {
                if (checkFault) { owner.EnsureUsable(); ObjectDisposedException.ThrowIf(released, this); }
                action();
            }
        }

        private WarpMachineMemoryOperations CreateOperations(IntPtr entry, int argumentCount, int pointerCount) =>
            new(
                bytes =>
                {
                    IntPtr buffer = owner.api.CreateBuffer(owner.context, 1, bytes, IntPtr.Zero, out int error);
                    Check(error, "clCreateBuffer(logical)");
                    return unchecked((ulong)buffer.ToInt64());
                },
                (destination, source, bytes) => Check(owner.api.EnqueueWriteBuffer(owner.queue,
                    new IntPtr(unchecked((long)destination)), 1, 0, bytes, source, 0, IntPtr.Zero, IntPtr.Zero), "clEnqueueWriteBuffer(logical)"),
                (destination, source, bytes) => Check(owner.api.EnqueueReadBuffer(owner.queue,
                    new IntPtr(unchecked((long)source)), 1, 0, bytes, destination, 0, IntPtr.Zero, IntPtr.Zero), "clEnqueueReadBuffer(logical)"),
                (arguments, grid, block) => LaunchWithArguments(entry, argumentCount, pointerCount, arguments, grid, block),
                () => Check(owner.api.Finish(owner.queue), "clFinish(logical)"),
                pointer => { owner.api.ReleaseBuffer(new IntPtr(unchecked((long)pointer))); },
                () => owner.faulted = true);

        private void LaunchWithArguments(IntPtr entry, int argumentCount, int pointerCount, IntPtr arguments, uint grid, uint block)
        {
            for (uint index = 0; index < argumentCount; index++)
            {
                nuint size = index < pointerCount ? checked((nuint)IntPtr.Size) : sizeof(uint);
                Check(owner.api.SetKernelArgument(entry, index, size,
                    Marshal.ReadIntPtr(arguments, checked((int)index * IntPtr.Size))), "clSetKernelArg(logical)");
            }

            using var global = new WarpNativeBlock(IntPtr.Size);
            using var local = new WarpNativeBlock(IntPtr.Size);
            Marshal.WriteInt64(global.Pointer, checked((long)grid * block));
            Marshal.WriteInt64(local.Pointer, block);
            Check(owner.api.EnqueueKernel(owner.queue, entry, 1, IntPtr.Zero, global.Pointer, local.Pointer,
                0, IntPtr.Zero, IntPtr.Zero), "clEnqueueNDRangeKernel(logical)");
        }



        public uint ReduceUInt32(uint[] values, WarpReductionOperation operation, CancellationToken cancellationToken = default)
        {
            lock (owner.gate)
            {
                owner.EnsureUsable();
                ObjectDisposedException.ThrowIf(released, this);
                var operations = CreateOperations(reductionKernel, 4, 2);
                return WarpNativeReductionDispatch.Reduce(Image, values, operation, operations, cancellationToken);
            }
        }

        public uint[] ResumeUInt32(uint[] states, IReadOnlyList<uint[]> inputs, IReadOnlyList<uint> scalars,
            int itemCount, int inputBase, int maximumCallDepth, int quantum, CancellationToken cancellationToken = default)
        {
            lock (owner.gate)
            {
                owner.EnsureUsable();
                ObjectDisposedException.ThrowIf(released, this);
                var operations = CreateOperations(kernel, Image.InputBufferCount + Image.ScalarArgumentCount + 5, Image.InputBufferCount + 1);
                return WarpNativeMachineDispatch.Resume(Image, states, inputs, scalars, itemCount, inputBase,
                    maximumCallDepth, quantum, operations, cancellationToken);
            }
        }

        public uint[] DispatchUInt32(IReadOnlyList<uint[]> inputs, IReadOnlyList<uint> scalars,
            int itemCount, bool reduction, CancellationToken cancellationToken = default)
        {
            Image.ValidateArguments(inputs, scalars, reduction);
            WarpNativeLaunch launch = WarpNativeLaunch.Admit(owner.Target, inputs, itemCount, reduction);
            lock (owner.gate)
            {
                owner.EnsureUsable();
                ObjectDisposedException.ThrowIf(released, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (launch.GridX == 0) { return []; }
                var buffers = new List<IntPtr>();
                bool launched = false;
                try
                {
                    uint argument = UploadInputs(inputs, itemCount, buffers, cancellationToken);

                    nuint outputBytes = checked((nuint)Math.Max(launch.OutputCount, 1) * sizeof(uint));
                    IntPtr output = owner.api.CreateBuffer(owner.context, 2, outputBytes, IntPtr.Zero, out int outputError);
                    Check(outputError, "clCreateBuffer(output)");
                    buffers.Add(output);
                    SetPointer(argument++, output);
                    SetInteger(argument++, checked((uint)itemCount));
                    foreach (uint scalar in scalars) { SetInteger(argument++, scalar); }
                    using var global = new WarpNativeBlock(IntPtr.Size);
                    using var local = new WarpNativeBlock(IntPtr.Size);
                    Marshal.WriteInt64(global.Pointer, checked((long)launch.GridX * launch.WorkgroupSize));
                    Marshal.WriteInt64(local.Pointer, launch.WorkgroupSize);
                    cancellationToken.ThrowIfCancellationRequested();
                    Check(owner.api.EnqueueKernel(owner.queue, kernel, 1, IntPtr.Zero, global.Pointer, local.Pointer,
                        0, IntPtr.Zero, IntPtr.Zero), "clEnqueueNDRangeKernel");
                    launched = true;
                    Check(owner.api.Finish(owner.queue), "clFinish");
                    var result = new uint[launch.OutputCount];
                    using var resultPin = new WarpPinnedUInt32(result);
                    Check(owner.api.EnqueueReadBuffer(owner.queue, output, 1, 0, outputBytes, resultPin.Pointer,
                        0, IntPtr.Zero, IntPtr.Zero), "clEnqueueReadBuffer");
                    cancellationToken.ThrowIfCancellationRequested();
                    return result;
                }
                catch (WarpHostException)
                {
                    if (launched) { owner.faulted = true; }
                    throw;
                }
                finally
                {
                    foreach (ref readonly IntPtr buffer in CollectionsMarshal.AsSpan(buffers)) { owner.api.ReleaseBuffer(buffer); }
                }
            }
        }

        private void SetPointer(uint index, IntPtr pointer)
        {
            using var value = new WarpNativeBlock(IntPtr.Size);
            Marshal.WriteIntPtr(value.Pointer, pointer);
            Check(owner.api.SetKernelArgument(kernel, index, checked((nuint)IntPtr.Size), value.Pointer), "clSetKernelArg(pointer)");
        }

        private void SetInteger(uint index, uint integer)
        {
            using var value = new WarpNativeBlock(sizeof(uint));
            Marshal.WriteInt32(value.Pointer, unchecked((int)integer));
            Check(owner.api.SetKernelArgument(kernel, index, sizeof(uint), value.Pointer), "clSetKernelArg(integer)");
        }

        private uint UploadInputs(IReadOnlyList<uint[]> inputs, int itemCount, List<IntPtr> buffers, CancellationToken cancellationToken)
        {
        uint argument = 0;
        foreach (uint[] input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            nuint bytes = checked((nuint)Math.Max(itemCount, 1) * sizeof(uint));
            IntPtr buffer = owner.api.CreateBuffer(owner.context, 4, bytes, IntPtr.Zero, out int error);
            Check(error, "clCreateBuffer(input)");
            buffers.Add(buffer);
            using var pin = new WarpPinnedUInt32((uint[])input.Clone());
            if (itemCount > 0)
            {
                Check(owner.api.EnqueueWriteBuffer(owner.queue, buffer, 1, 0, bytes, pin.Pointer,
                    0, IntPtr.Zero, IntPtr.Zero), "clEnqueueWriteBuffer");
            }

            SetPointer(argument++, buffer);
        }

            return argument;
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                if (released) { return; }
                owner.EnsureUsable();
                Release();
            }
        }

        internal void Release()
        {
            if (released) { return; }
            foreach (WarpBoundNativeMachineExecution execution in executions.ToArray()) { execution.Dispose(); }
            owner.api.ReleaseKernel(kernel);
            if (reductionKernel != IntPtr.Zero) { owner.api.ReleaseKernel(reductionKernel); }
            owner.api.ReleaseProgram(program);
            released = true;
            owner.modules.Remove(this);
        }
    }

    private sealed class OpenClApi
    {
        public OpenClApi(WarpNativeLibrary library)
        {
            GetPlatformIds = library.Export<GetPlatformIdsDelegate>("clGetPlatformIDs");
            GetDeviceIds = library.Export<GetDeviceIdsDelegate>("clGetDeviceIDs");
            GetDeviceInfo = library.Export<GetInfoDelegate>("clGetDeviceInfo");
            CreateContext = library.Export<CreateContextDelegate>("clCreateContext");
            ReleaseContext = library.Export<HandleDelegate>("clReleaseContext");
            CreateQueue = library.Export<CreateQueueDelegate>("clCreateCommandQueueWithProperties");
            ReleaseQueue = library.Export<HandleDelegate>("clReleaseCommandQueue");
            CreateProgramWithIl = library.Export<CreateProgramDelegate>("clCreateProgramWithIL");
            BuildProgram = library.Export<BuildProgramDelegate>("clBuildProgram");
            GetProgramBuildInfo = library.Export<GetProgramBuildInfoDelegate>("clGetProgramBuildInfo");
            ReleaseProgram = library.Export<HandleDelegate>("clReleaseProgram");
            CreateKernel = library.Export<CreateKernelDelegate>("clCreateKernel");
            GetKernelInfo = library.Export<GetInfoDelegate>("clGetKernelInfo");
            GetKernelWorkgroupInfo = library.Export<GetProgramBuildInfoDelegate>("clGetKernelWorkGroupInfo");
            ReleaseKernel = library.Export<HandleDelegate>("clReleaseKernel");
            SetKernelArgument = library.Export<SetKernelArgumentDelegate>("clSetKernelArg");
            CreateBuffer = library.Export<CreateBufferDelegate>("clCreateBuffer");
            ReleaseBuffer = library.Export<HandleDelegate>("clReleaseMemObject");
            EnqueueReadBuffer = library.Export<TransferBufferDelegate>("clEnqueueReadBuffer");
            EnqueueWriteBuffer = library.Export<TransferBufferDelegate>("clEnqueueWriteBuffer");
            EnqueueKernel = library.Export<EnqueueKernelDelegate>("clEnqueueNDRangeKernel");
            Finish = library.Export<HandleDelegate>("clFinish");
        }

        public GetPlatformIdsDelegate GetPlatformIds { get; }
        public GetDeviceIdsDelegate GetDeviceIds { get; }
        public GetInfoDelegate GetDeviceInfo { get; }
        public CreateContextDelegate CreateContext { get; }
        public HandleDelegate ReleaseContext { get; }
        public CreateQueueDelegate CreateQueue { get; }
        public HandleDelegate ReleaseQueue { get; }
        public CreateProgramDelegate CreateProgramWithIl { get; }
        public BuildProgramDelegate BuildProgram { get; }
        public GetProgramBuildInfoDelegate GetProgramBuildInfo { get; }
        public HandleDelegate ReleaseProgram { get; }
        public CreateKernelDelegate CreateKernel { get; }
        public GetInfoDelegate GetKernelInfo { get; }
        public GetProgramBuildInfoDelegate GetKernelWorkgroupInfo { get; }
        public HandleDelegate ReleaseKernel { get; }
        public SetKernelArgumentDelegate SetKernelArgument { get; }
        public CreateBufferDelegate CreateBuffer { get; }
        public HandleDelegate ReleaseBuffer { get; }
        public TransferBufferDelegate EnqueueReadBuffer { get; }
        public TransferBufferDelegate EnqueueWriteBuffer { get; }
        public EnqueueKernelDelegate EnqueueKernel { get; }
        public HandleDelegate Finish { get; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetPlatformIdsDelegate(uint count, IntPtr platforms, out uint actualCount);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDeviceIdsDelegate(IntPtr platform, ulong type, uint count, IntPtr devices, out uint actualCount);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetInfoDelegate(IntPtr device, uint name, nuint size, IntPtr data, out nuint actualSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr CreateContextDelegate(IntPtr properties, uint count, IntPtr devices, IntPtr callback, IntPtr userData, out int error);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int HandleDelegate(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr CreateQueueDelegate(IntPtr context, IntPtr device, IntPtr properties, out int error);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr CreateProgramDelegate(IntPtr context, IntPtr il, nuint length, out int error);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BuildProgramDelegate(IntPtr program, uint count, IntPtr devices,
        [MarshalAs(UnmanagedType.LPStr)] string options, IntPtr callback, IntPtr userData);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetProgramBuildInfoDelegate(IntPtr program, IntPtr device, uint name, nuint size, IntPtr data, out nuint actualSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr CreateKernelDelegate(IntPtr program, [MarshalAs(UnmanagedType.LPStr)] string name, out int error);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetKernelArgumentDelegate(IntPtr kernel, uint index, nuint size, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr CreateBufferDelegate(IntPtr context, ulong flags, nuint size, IntPtr hostPointer, out int error);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TransferBufferDelegate(IntPtr queue, IntPtr buffer, uint blocking,
        nuint offset, nuint size, IntPtr data, uint count, IntPtr events, IntPtr resultEvent);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnqueueKernelDelegate(IntPtr queue, IntPtr kernel, uint dimensions,
        IntPtr offset, IntPtr global, IntPtr local, uint count, IntPtr events, IntPtr resultEvent);
}
