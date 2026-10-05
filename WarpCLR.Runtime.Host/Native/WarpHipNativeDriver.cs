using System.Runtime.InteropServices;
using System.Text;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpHipNativeDriver : IWarpNativeDriver
{
    private readonly Lock gate = new();
    private readonly WarpNativeLibrary library;
    private readonly HipApi api;
    private readonly int deviceOrdinal;
    private readonly HashSet<Module> modules = [];
    private readonly HashSet<WarpNativeManagedArena> arenas = [];
    private bool disposed;
    private bool faulted;

    private WarpHipNativeDriver(int deviceOrdinal, string? libraryPath)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deviceOrdinal);
        if (IntPtr.Size != 8)
        {
            throw new PlatformNotSupportedException("The HIP R0600 device ABI requires a 64-bit process.");
        }

        this.deviceOrdinal = deviceOrdinal;
        library = WarpNativeLibrary.Open(libraryPath, OperatingSystem.IsWindows() ? "amdhip64.dll" : "libamdhip64.so");
        try
        {
            api = new HipApi(library);
            Check(api.Init(0), "hipInit");
            Check(api.DeviceGetCount(out int count), "hipGetDeviceCount");
            if (deviceOrdinal >= count)
            {
                throw new WarpHostException("WRPNATIVE1001", "The selected HIP device is absent.");
            }

            Check(api.RuntimeGetVersion(out int runtimeVersion), "hipRuntimeGetVersion");
            using var properties = new WarpNativeBlock(HipDevicePropertiesR0600.Size);
            Check(api.GetDeviceProperties(properties.Pointer, deviceOrdinal), "hipGetDevicePropertiesR0600");
            string architecture = ReadFixedString(properties.Pointer, HipDevicePropertiesR0600.GcnArchitecture, 256);
            string name = ReadFixedString(properties.Pointer, 0, 256);
            var bus = new StringBuilder(32);
            Check(api.DeviceGetPciBusId(bus, bus.Capacity, deviceOrdinal), "hipDeviceGetPCIBusId");
            uint maxThreads = checked((uint)Marshal.ReadInt32(properties.Pointer, HipDevicePropertiesR0600.MaxThreadsPerBlock));
            uint maxGrid = checked((uint)Marshal.ReadInt32(properties.Pointer, HipDevicePropertiesR0600.MaxGridSize));
            ulong memory = unchecked((ulong)Marshal.ReadInt64(properties.Pointer, HipDevicePropertiesR0600.TotalGlobalMemory));
            Target = new WarpNativeTarget(WarpBackendKind.AMDGPU, architecture, bus + "/" + name,
                "hip-runtime/" + runtimeVersion, maxThreads, maxGrid, memory);
        }
        catch
        {
            library.Dispose();
            throw;
        }
    }

    public WarpNativeTarget Target { get; }
    public static WarpHipNativeDriver Open(int deviceOrdinal = 0, string? libraryPath = null) => new(deviceOrdinal, libraryPath);

    private void WithArenaContext(Action action, bool checkFault)
    {
        lock (gate)
        {
            if (checkFault) { EnsureUsable(); }
            if (disposed) { action(); return; }
            using var device = EnterDevice();
            action();
        }
    }

    public IWarpNativeModule Load(WarpNativeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        lock (gate)
        {
            EnsureUsable();
            if (image.Target != Target || image.Format != WarpNativeImageFormat.Hsaco)
            {
                throw new WarpHostException("WRPNATIVE1003", "The HSACO was compiled for a different concrete HIP target.");
            }

            using var device = EnterDevice();
            using var bytes = new WarpNativeBlock(image.Content.Length);
            Marshal.Copy(image.Content.ToArray(), 0, bytes.Pointer, image.Content.Length);
            Check(api.ModuleLoadData(out IntPtr module, bytes.Pointer), "hipModuleLoadData");
            try
            {
                Check(api.ModuleGetFunction(out IntPtr function, module, image.EntryPoint), "hipModuleGetFunction");
                Check(api.FunctionGetAttribute(out int maxThreads, 0, function), "hipFuncGetAttribute");
                if (maxThreads < WarpDeviceAbi.IntegerMapWorkgroupSize)
                {
                    throw new WarpHostException("WRPNATIVE1005", "The loaded HIP kernel cannot admit the portable workgroup size.");
                }

                IntPtr reductionFunction = IntPtr.Zero;
                if (image.SupportsScalableReduction)
                {
                    Check(api.ModuleGetFunction(out reductionFunction, module, WarpPortableMachineEmitter.ReductionEntryPoint),
                        "hipModuleGetFunction(reduction)");
                    Check(api.FunctionGetAttribute(out int reductionMaxThreads, 0, reductionFunction), "hipFuncGetAttribute(reduction)");
                    if (reductionMaxThreads < WarpDeviceAbi.IntegerMapWorkgroupSize)
                    {
                        throw new WarpHostException("WRPNATIVE1005", "The HIP reduction kernel cannot admit the portable workgroup size.");
                    }
                }

                var loaded = new Module(this, image, module, function, reductionFunction);
                modules.Add(loaded);
                return loaded;
            }
            catch
            {
                api.ModuleUnload(module);
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) { return; }
            var cleanup = new WarpNativeCleanup();
            cleanup.Attempt(() =>
            {
                using var device = EnterDevice();
                foreach (Module module in modules.ToArray()) { cleanup.Attempt(module.Release); }
                foreach (WarpNativeManagedArena arena in arenas.ToArray()) { cleanup.Attempt(arena.CloseContext); }
            });
            disposed = true;
            try { cleanup.Complete(); }
            finally { library.Dispose(); }
        }
    }

    private DeviceScope EnterDevice()
    {
        Check(api.GetDevice(out int previous), "hipGetDevice");
        Check(api.SetDevice(deviceOrdinal), "hipSetDevice");
        return new DeviceScope(api, previous);
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (faulted) { throw new WarpHostException("WRPNATIVE1006", "The HIP context is faulted and must be disposed."); }
    }

    private static string ReadFixedString(IntPtr data, int offset, int length)
    {
        byte[] text = new byte[length];
        Marshal.Copy(IntPtr.Add(data, offset), text, 0, text.Length);
        int end = Array.IndexOf(text, (byte)0);
        return Encoding.UTF8.GetString(text, 0, end < 0 ? length : end);
    }

    private static void Check(int error, string operation)
    {
        if (error != 0)
        {
            throw new WarpHostException(error == 2 ? "WRPNATIVE1005" : "WRPNATIVE1007",
                $"HIP operation '{operation}' failed with runtime result {error}.");
        }
    }

    private sealed class Module(WarpHipNativeDriver owner, WarpNativeImage image, IntPtr module, IntPtr function, IntPtr reductionFunction)
        : IWarpNativeModule
    {
        private bool released;
        private readonly HashSet<WarpBoundNativeMachineExecution> executions = [];

        public WarpNativeImage Image { get; } = image;
        public bool IsFaulted { get { lock (owner.gate) { return owner.faulted; } } }

        public WarpNativeManagedArena CreateManagedArena(uint[] initial)
        {
            lock (owner.gate)
            {
                owner.EnsureUsable();
                ObjectDisposedException.ThrowIf(released, this);
                using var device = owner.EnterDevice();
                var arena = new WarpNativeManagedArena(Image.Target, CreateOperations(function, IntPtr.Zero), initial,
                    action => owner.WithArenaContext(action, checkFault: true),
                    action => owner.WithArenaContext(action, checkFault: false), item => owner.arenas.Remove(item));
                try { owner.arenas.Add(arena); return arena; }
                catch { arena.Dispose(); throw; }
            }
        }

        public IWarpNativeMachineExecution CreateMachineExecution(uint[] states, IReadOnlyList<uint[]> inputs,
            IReadOnlyList<uint> scalars, int itemCount, int inputBase, int maximumCallDepth,
            WarpNativeManagedArena? managedArena = null)
        {
            lock (owner.gate)
            {
                owner.EnsureUsable();
                ObjectDisposedException.ThrowIf(released, this);
                using var device = owner.EnterDevice();
                Check(owner.api.StreamCreate(out IntPtr stream, 1), "hipStreamCreateWithFlags(resident)");
                bool streamTransferred = false;
                try
                {
                    var bound = new WarpBoundNativeMachineExecution(
                        () => new WarpNativeMachineExecution(Image, states, inputs, scalars, itemCount, inputBase,
                            maximumCallDepth, CreateOperations(function, stream), managedArena),
                        action => WithDevice(action, checkFault: true), action => WithDevice(action, checkFault: false),
                        item => { owner.api.StreamDestroy(stream); executions.Remove(item); });
                    streamTransferred = true;
                    try { executions.Add(bound); return bound; }
                    catch { bound.Dispose(); throw; }
                }
                catch
                {
                    if (!streamTransferred) { owner.api.StreamDestroy(stream); }
                    throw;
                }
            }
        }

        private void WithDevice(Action action, bool checkFault)
        {
            lock (owner.gate)
            {
                if (checkFault) { owner.EnsureUsable(); ObjectDisposedException.ThrowIf(released, this); }
                using var device = owner.EnterDevice();
                action();
            }
        }

        private WarpMachineMemoryOperations CreateOperations(IntPtr entry, IntPtr stream)
        {
            return new WarpMachineMemoryOperations(
                bytes => { Check(owner.api.MemoryAllocate(out IntPtr pointer, bytes), "hipMalloc(state)"); return unchecked((ulong)pointer.ToInt64()); },
                (destination, source, bytes) => Check(owner.api.MemoryCopy(new IntPtr(unchecked((long)destination)), source, bytes, 1), "hipMemcpy(state HtoD)"),
                (destination, source, bytes) => Check(owner.api.MemoryCopy(destination, new IntPtr(unchecked((long)source)), bytes, 2), "hipMemcpy(state DtoH)"),
                (arguments, grid, block) => Check(owner.api.LaunchKernel(entry, grid, 1, 1, block, 1, 1, 0,
                    stream, arguments, IntPtr.Zero), "hipModuleLaunchKernel(resume)"),
                () => Check(owner.api.StreamSynchronize(stream), "hipStreamSynchronize(resume)"),
                pointer => Check(owner.api.MemoryFree(new IntPtr(unchecked((long)pointer))), "hipFree(logical)"),
                () => owner.faulted = true, owner);
        }

        public uint ReduceUInt32(uint[] values, WarpReductionOperation operation, CancellationToken cancellationToken = default)
        {
            lock (owner.gate)
            {
                owner.EnsureUsable();
                ObjectDisposedException.ThrowIf(released, this);
                using var device = owner.EnterDevice();
                Check(owner.api.StreamCreate(out IntPtr stream, 1), "hipStreamCreateWithFlags(reduction)");
                try
                {
                    var operations = CreateOperations(reductionFunction, stream);
                    return WarpNativeReductionDispatch.Reduce(Image, values, operation, operations, cancellationToken);
                }
                finally { owner.api.StreamDestroy(stream); }
            }
        }

        public uint[] ResumeUInt32(uint[] states, IReadOnlyList<uint[]> inputs, IReadOnlyList<uint> scalars,
            int itemCount, int inputBase, int maximumCallDepth, int quantum, CancellationToken cancellationToken = default)
        {
            lock (owner.gate)
            {
                owner.EnsureUsable();
                ObjectDisposedException.ThrowIf(released, this);
                using var device = owner.EnterDevice();
                Check(owner.api.StreamCreate(out IntPtr stream, 1), "hipStreamCreateWithFlags(resume)");
                try
                {
                    var operations = CreateOperations(function, stream);
                    return WarpNativeMachineDispatch.Resume(Image, states, inputs, scalars, itemCount, inputBase,
                        maximumCallDepth, quantum, operations, cancellationToken);
                }
                finally { owner.api.StreamDestroy(stream); }
            }
        }

        public uint[] DispatchUInt32(IReadOnlyList<uint[]> inputs, IReadOnlyList<uint> scalars,
            int itemCount, bool reduction, CancellationToken cancellationToken = default)
        {
            Image.ValidateArguments(inputs, scalars, reduction);
            WarpNativeLaunch launch = WarpNativeLaunch.Admit(owner.Target, inputs, itemCount, reduction, scalars.Count);
            lock (owner.gate)
            {
                owner.EnsureUsable();
                ObjectDisposedException.ThrowIf(released, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (launch.GridX == 0) { return []; }
                using var device = owner.EnterDevice();
                Check(owner.api.StreamCreate(out IntPtr stream, 1), "hipStreamCreateWithFlags");
                List<ulong> allocations = [];
                bool launched = false;
                try
                {
                    List<ulong> inputPointers = UploadInputs(inputs, itemCount, allocations, cancellationToken);

                    nuint outputBytes = checked((nuint)Math.Max(launch.OutputCount, 1) * sizeof(uint));
                    Check(owner.api.MemoryAllocate(out IntPtr output, outputBytes), "hipMalloc(output)");
                    allocations.Add(unchecked((ulong)output.ToInt64()));
                    using var arguments = new WarpNativeKernelArguments(inputPointers, unchecked((ulong)output.ToInt64()),
                        checked((uint)itemCount), scalars);
                    cancellationToken.ThrowIfCancellationRequested();
                    Check(owner.api.LaunchKernel(function, launch.GridX, 1, 1, launch.WorkgroupSize, 1, 1,
                        0, stream, arguments.Pointer, IntPtr.Zero), "hipModuleLaunchKernel");
                    launched = true;
                    Check(owner.api.StreamSynchronize(stream), "hipStreamSynchronize");
                    var result = new uint[launch.OutputCount];
                    using var resultPin = new WarpPinnedUInt32(result);
                    Check(owner.api.MemoryCopy(resultPin.Pointer, output, outputBytes, 2), "hipMemcpy(DtoH)");
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
                    foreach (ref readonly ulong allocation in CollectionsMarshal.AsSpan(allocations)) { owner.api.MemoryFree(new IntPtr(unchecked((long)allocation))); }
                    owner.api.StreamDestroy(stream);
                }
            }
        }

        private List<ulong> UploadInputs(IReadOnlyList<uint[]> inputs, int itemCount, List<ulong> allocations, CancellationToken cancellationToken)
        {
            var inputPointers = new List<ulong>(inputs.Count);
            foreach (uint[] input in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                nuint bytes = checked((nuint)Math.Max(itemCount, 1) * sizeof(uint));
                Check(owner.api.MemoryAllocate(out IntPtr pointer, bytes), "hipMalloc");
                allocations.Add(unchecked((ulong)pointer.ToInt64()));
                inputPointers.Add(unchecked((ulong)pointer.ToInt64()));
                using var pinned = new WarpPinnedUInt32((uint[])input.Clone());
                if (itemCount > 0) { Check(owner.api.MemoryCopy(pointer, pinned.Pointer, bytes, 1), "hipMemcpy(HtoD)"); }
            }

            return inputPointers;
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                if (released) { return; }
                ObjectDisposedException.ThrowIf(owner.disposed, owner);
                using var device = owner.EnterDevice();
                Release();
            }
        }

        internal void Release()
        {
            if (released) { return; }
            released = true;
            var cleanup = new WarpNativeCleanup();
            foreach (WarpBoundNativeMachineExecution execution in executions.ToArray()) { cleanup.Attempt(execution.Dispose); }
            cleanup.Attempt(() => Check(owner.api.ModuleUnload(module), "module unload"));
            owner.modules.Remove(this);
            cleanup.Complete();
        }
    }

    private sealed class DeviceScope(HipApi api, int previousDevice) : IDisposable
    {
        public void Dispose() => Check(api.SetDevice(previousDevice), "hipSetDevice(restore)");
    }

    private sealed class HipApi
    {
        public HipApi(WarpNativeLibrary library)
        {
            Init = library.Export<InitDelegate>("hipInit");
            DeviceGetCount = library.Export<GetIntegerDelegate>("hipGetDeviceCount");
            RuntimeGetVersion = library.Export<GetIntegerDelegate>("hipRuntimeGetVersion");
            GetDeviceProperties = library.Export<DevicePropertiesDelegate>("hipGetDevicePropertiesR0600");
            DeviceGetPciBusId = library.Export<DeviceStringDelegate>("hipDeviceGetPCIBusId");
            GetDevice = library.Export<GetIntegerDelegate>("hipGetDevice");
            SetDevice = library.Export<SetIntegerDelegate>("hipSetDevice");
            ModuleLoadData = library.Export<ModuleLoadDelegate>("hipModuleLoadData");
            ModuleGetFunction = library.Export<GetFunctionDelegate>("hipModuleGetFunction");
            ModuleUnload = library.Export<HandleDelegate>("hipModuleUnload");
            FunctionGetAttribute = library.Export<FunctionAttributeDelegate>("hipFuncGetAttribute");
            StreamCreate = library.Export<StreamCreateDelegate>("hipStreamCreateWithFlags");
            StreamSynchronize = library.Export<HandleDelegate>("hipStreamSynchronize");
            StreamDestroy = library.Export<HandleDelegate>("hipStreamDestroy");
            MemoryAllocate = library.Export<MemoryAllocateDelegate>("hipMalloc");
            MemoryFree = library.Export<HandleDelegate>("hipFree");
            MemoryCopy = library.Export<MemoryCopyDelegate>("hipMemcpy");
            LaunchKernel = library.Export<LaunchDelegate>("hipModuleLaunchKernel");
        }

        public InitDelegate Init { get; }
        public GetIntegerDelegate DeviceGetCount { get; }
        public GetIntegerDelegate RuntimeGetVersion { get; }
        public DevicePropertiesDelegate GetDeviceProperties { get; }
        public DeviceStringDelegate DeviceGetPciBusId { get; }
        public GetIntegerDelegate GetDevice { get; }
        public SetIntegerDelegate SetDevice { get; }
        public ModuleLoadDelegate ModuleLoadData { get; }
        public GetFunctionDelegate ModuleGetFunction { get; }
        public HandleDelegate ModuleUnload { get; }
        public FunctionAttributeDelegate FunctionGetAttribute { get; }
        public StreamCreateDelegate StreamCreate { get; }
        public HandleDelegate StreamSynchronize { get; }
        public HandleDelegate StreamDestroy { get; }
        public MemoryAllocateDelegate MemoryAllocate { get; }
        public HandleDelegate MemoryFree { get; }
        public MemoryCopyDelegate MemoryCopy { get; }
        public LaunchDelegate LaunchKernel { get; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitDelegate(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetIntegerDelegate(out int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetIntegerDelegate(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DevicePropertiesDelegate(IntPtr properties, int device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceStringDelegate([Out] StringBuilder value, int length, int device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int HandleDelegate(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ModuleLoadDelegate(out IntPtr module, IntPtr image);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetFunctionDelegate(out IntPtr function, IntPtr module, [MarshalAs(UnmanagedType.LPStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FunctionAttributeDelegate(out int value, int attribute, IntPtr function);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StreamCreateDelegate(out IntPtr stream, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MemoryAllocateDelegate(out IntPtr pointer, nuint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MemoryCopyDelegate(IntPtr destination, IntPtr source, nuint bytes, int kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LaunchDelegate(IntPtr function, uint gridX, uint gridY, uint gridZ,
        uint blockX, uint blockY, uint blockZ, uint sharedBytes, IntPtr stream, IntPtr arguments, IntPtr extra);
}
