using System.Runtime.InteropServices;
using System.Text;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpCudaNativeDriver : IWarpNativeDriver
{
    private readonly Lock gate = new();
    private readonly WarpNativeLibrary library;
    private readonly CudaApi api;
    private readonly HashSet<Module> modules = [];
    private readonly HashSet<WarpNativeManagedArena> arenas = [];
    private IntPtr context;
    private bool disposed;
    private bool faulted;

    private WarpCudaNativeDriver(int deviceOrdinal, string? libraryPath)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deviceOrdinal);
        if (IntPtr.Size != 8)
        {
            throw new PlatformNotSupportedException("The portable PTX64 ABI requires a 64-bit host process.");
        }
        library = WarpNativeLibrary.Open(libraryPath, OperatingSystem.IsWindows() ? "nvcuda.dll" : "libcuda.so.1");
        try
        {
            api = new CudaApi(library);
            Check(api.Init(0), "cuInit");
            Check(api.DeviceGetCount(out int count), "cuDeviceGetCount");
            if (deviceOrdinal >= count)
            {
                throw new WarpHostException("WRPNATIVE1001", "The selected CUDA device is absent.");
            }

            Check(api.DeviceGet(out int device, deviceOrdinal), "cuDeviceGet");
            Check(api.DeviceGetAttribute(out int major, 75, device), "compute capability major");
            Check(api.DeviceGetAttribute(out int minor, 76, device), "compute capability minor");
            Check(api.DeviceGetAttribute(out int maxThreads, 1, device), "maximum block size");
            Check(api.DeviceGetAttribute(out int maxGrid, 5, device), "maximum grid size");
            Check(api.DeviceTotalMem(out nuint memory, device), "cuDeviceTotalMem");
            Check(api.DriverGetVersion(out int version), "cuDriverGetVersion");
            var name = new StringBuilder(256);
            var bus = new StringBuilder(32);
            Check(api.DeviceGetName(name, name.Capacity, device), "cuDeviceGetName");
            Check(api.DeviceGetPciBusId(bus, bus.Capacity, device), "cuDeviceGetPCIBusId");
            Target = new WarpNativeTarget(WarpBackendKind.NVPTX, $"sm_{major}{minor}",
                bus + "/" + name, "cuda-driver/" + version, checked((uint)maxThreads),
                checked((uint)maxGrid), (ulong)memory, supportsInt64Atomics: major >= 7);
            // An owned context avoids resetting or destroying another client's primary context.
            Check(api.ContextCreate(out context, 0, device), "cuCtxCreate");
            Check(api.ContextPopCurrent(out _), "cuCtxPopCurrent");
        }
        catch
        {
            if (context != IntPtr.Zero) { api?.ContextDestroy(context); }
            library.Dispose();
            throw;
        }
    }

    public WarpNativeTarget Target { get; }

    public static WarpCudaNativeDriver Open(int deviceOrdinal = 0, string? libraryPath = null) =>
        new(deviceOrdinal, libraryPath);

    private void WithArenaContext(Action action, bool checkFault)
    {
        lock (gate)
        {
            if (checkFault) { EnsureUsable(); }
            if (disposed) { action(); return; }
            using var scope = EnterContext();
            action();
        }
    }

    public IWarpNativeModule Load(WarpNativeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        lock (gate)
        {
            EnsureUsable();
            if (image.Target != Target || image.Format != WarpNativeImageFormat.Ptx)
            {
                throw new WarpHostException("WRPNATIVE1003", "The module was compiled for a different concrete CUDA target.");
            }

            using var scope = EnterContext();
            byte[] ptx = new byte[checked(image.Content.Length + 1)];
            image.Content.Span.CopyTo(ptx);
            using var pinned = new PinnedBytes(ptx);
            using var errorLog = new WarpNativeBlock(16384);
            using var options = new WarpNativeBlock(2 * sizeof(int));
            using var values = new WarpNativeBlock(2 * IntPtr.Size);
            // CU_JIT_ERROR_LOG_BUFFER and CU_JIT_ERROR_LOG_BUFFER_SIZE_BYTES.
            Marshal.WriteInt32(options.Pointer, 0, 5);
            Marshal.WriteInt32(options.Pointer, sizeof(int), 6);
            Marshal.WriteIntPtr(values.Pointer, 0, errorLog.Pointer);
            Marshal.WriteIntPtr(values.Pointer, IntPtr.Size, new IntPtr(errorLog.Size));
            int result = api.ModuleLoadDataEx(out IntPtr module, pinned.Pointer, 2, options.Pointer, values.Pointer);
            if (result != 0)
            {
                throw new WarpHostException("WRPNATIVE2005",
                    $"CUDA module validation/JIT failed ({result}): {Marshal.PtrToStringUTF8(errorLog.Pointer)}");
            }

            try
            {
                Check(api.ModuleGetFunction(out IntPtr function, module, image.EntryPoint), "cuModuleGetFunction");
                Check(api.FunctionGetAttribute(out int maximumThreads, 0, function), "cuFuncGetAttribute");
                if (maximumThreads < WarpDeviceAbi.IntegerMapWorkgroupSize)
                {
                    throw new WarpHostException("WRPNATIVE1005", "The loaded CUDA kernel cannot admit the portable workgroup size.");
                }

                IntPtr reductionFunction = IntPtr.Zero;
                if (image.SupportsScalableReduction)
                {
                    Check(api.ModuleGetFunction(out reductionFunction, module, WarpPortableMachineEmitter.ReductionEntryPoint),
                        "cuModuleGetFunction(reduction)");
                    Check(api.FunctionGetAttribute(out int reductionMaximumThreads, 0, reductionFunction), "cuFuncGetAttribute(reduction)");
                    if (reductionMaximumThreads < WarpDeviceAbi.IntegerMapWorkgroupSize)
                    {
                        throw new WarpHostException("WRPNATIVE1005", "The CUDA reduction kernel cannot admit the portable workgroup size.");
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
                using var scope = EnterContext();
                foreach (Module module in modules.ToArray()) { cleanup.Attempt(module.Release); }
                foreach (WarpNativeManagedArena arena in arenas.ToArray()) { cleanup.Attempt(arena.CloseContext); }
            });
            disposed = true;
            cleanup.Attempt(() => Check(api.ContextDestroy(context), "cuCtxDestroy"));
            context = IntPtr.Zero;
            try { cleanup.Complete(); }
            finally { library.Dispose(); }
        }
    }

    private ContextScope EnterContext()
    {
        Check(api.ContextPushCurrent(context), "cuCtxPushCurrent");
        return new ContextScope(api);
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (faulted) { throw new WarpHostException("WRPNATIVE1006", "The CUDA context is faulted and must be disposed."); }
    }

    private static void Check(int error, string operation)
    {
        if (error != 0)
        {
            throw new WarpHostException(error == 2 ? "WRPNATIVE1005" : "WRPNATIVE1007",
                $"CUDA operation '{operation}' failed with driver result {error}.");
        }
    }

    private sealed class Module(WarpCudaNativeDriver owner, WarpNativeImage image, IntPtr module, IntPtr function, IntPtr reductionFunction)
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
                using var scope = owner.EnterContext();
                var arena = new WarpNativeManagedArena(Image.Target, CreateOperations(function), initial,
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
                using var scope = owner.EnterContext();
                var bound = new WarpBoundNativeMachineExecution(
                    () => new WarpNativeMachineExecution(Image, states, inputs, scalars, itemCount, inputBase,
                        maximumCallDepth, CreateOperations(function), managedArena),
                    action => WithContext(action, checkFault: true), action => WithContext(action, checkFault: false),
                    item => executions.Remove(item));
                try { executions.Add(bound); return bound; }
                catch { bound.Dispose(); throw; }
            }
        }

        private void WithContext(Action action, bool checkFault)
        {
            lock (owner.gate)
            {
                if (checkFault) { owner.EnsureUsable(); ObjectDisposedException.ThrowIf(released, this); }
                using var scope = owner.EnterContext();
                action();
            }
        }

        private WarpMachineMemoryOperations CreateOperations(IntPtr entry)
        {
            return new WarpMachineMemoryOperations(
                bytes => { Check(owner.api.MemoryAllocate(out ulong pointer, bytes), "cuMemAlloc(state)"); return pointer; },
                (destination, source, bytes) => Check(owner.api.CopyHostToDevice(destination, source, bytes), "cuMemcpyHtoD(state)"),
                (destination, source, bytes) => Check(owner.api.CopyDeviceToHost(destination, source, bytes), "cuMemcpyDtoH(state)"),
                (arguments, grid, block) => Check(owner.api.LaunchKernel(entry, grid, 1, 1, block, 1, 1, 0,
                    IntPtr.Zero, arguments, IntPtr.Zero), "cuLaunchKernel(resume)"),
                () => Check(owner.api.ContextSynchronize(), "cuCtxSynchronize(resume)"),
                pointer => Check(owner.api.MemoryFree(pointer), "cuMemFree(logical)"),
                () => owner.faulted = true, owner);
        }

        public uint ReduceUInt32(uint[] values, WarpReductionOperation operation, CancellationToken cancellationToken = default)
        {
            lock (owner.gate)
            {
                owner.EnsureUsable();
                ObjectDisposedException.ThrowIf(released, this);
                using var scope = owner.EnterContext();
                var operations = CreateOperations(reductionFunction);
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
                using var scope = owner.EnterContext();
                var operations = CreateOperations(function);
                return WarpNativeMachineDispatch.Resume(Image, states, inputs, scalars, itemCount, inputBase,
                    maximumCallDepth, quantum, operations, cancellationToken);
            }
        }

        public uint[] DispatchUInt32(IReadOnlyList<uint[]> inputs, IReadOnlyList<uint> scalars,
            int itemCount, bool reduction, CancellationToken cancellationToken = default)
        {
            using var bankUse = WarpNativeBankRetention.Acquire(inputs, scalars, []);
            try
            {
                Image.ValidateArguments(bankUse.Inputs, bankUse.Scalars, reduction);
                WarpNativeLaunch launch = WarpNativeLaunch.Admit(owner.Target, bankUse.Inputs, itemCount, reduction, bankUse.Scalars.Count);
                lock (owner.gate)
                {
                    owner.EnsureUsable();
                    ObjectDisposedException.ThrowIf(released, this);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (launch.GridX == 0) { bankUse.PreparePublication(); return []; }
                    using var scope = owner.EnterContext();
                    return DispatchAdmittedUInt32(bankUse, launch, itemCount, cancellationToken);
                }
            }
            catch (Exception failure) { bankUse.RecordFailure(failure); throw; }
            finally
            {
                bankUse.ReleaseIfNoAllocationAttempt();
                bankUse.ThrowFirstFailure();
            }
        }

        private uint[] DispatchAdmittedUInt32(WarpNativeBankRetention bankUse, WarpNativeLaunch launch, int itemCount,
            CancellationToken cancellationToken)
        {
            bool launched = false;
            try
            {
                List<ulong> inputPointers = UploadInputs(bankUse, itemCount, cancellationToken);
                nuint outputBytes = checked((nuint)Math.Max(launch.OutputCount, 1) * sizeof(uint));
                ulong output = bankUse.Allocate(outputBytes, bytes =>
                {
                    Check(owner.api.MemoryAllocate(out ulong pointer, bytes), "cuMemAlloc(output)");
                    return pointer;
                });
                using var arguments = new WarpNativeKernelArguments(inputPointers, output, checked((uint)itemCount), bankUse.Scalars);
                cancellationToken.ThrowIfCancellationRequested();
                Check(owner.api.LaunchKernel(function, launch.GridX, 1, 1, launch.WorkgroupSize, 1, 1,
                    0, IntPtr.Zero, arguments.Pointer, IntPtr.Zero), "cuLaunchKernel");
                launched = true;
                Check(owner.api.ContextSynchronize(), "cuCtxSynchronize");
                var result = new uint[launch.OutputCount];
                using var resultPin = new WarpPinnedUInt32(result);
                Check(owner.api.CopyDeviceToHost(resultPin.Pointer, output, outputBytes), "cuMemcpyDtoH");
                cancellationToken.ThrowIfCancellationRequested();
                bankUse.PreparePublication();
                return result;
            }
            catch (Exception failure)
            {
                bankUse.RecordFailure(failure);
                if (launched && failure is not OperationCanceledException) { owner.faulted = true; }
                throw;
            }
            finally
            {
                bankUse.Retire(pointer => Check(owner.api.MemoryFree(pointer), "cuMemFree(dispatch)"), () => owner.faulted = true);
            }
        }

        private List<ulong> UploadInputs(WarpNativeBankRetention bankUse, int itemCount, CancellationToken cancellationToken)
        {
            var inputPointers = new List<ulong>(bankUse.Inputs.Count);
            foreach (uint[] input in bankUse.Inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                nuint bytes = checked((nuint)Math.Max(itemCount, 1) * sizeof(uint));
                ulong pointer = bankUse.Allocate(bytes, requested =>
                {
                    Check(owner.api.MemoryAllocate(out ulong allocation, requested), "cuMemAlloc");
                    return allocation;
                });
                inputPointers.Add(pointer);
                using var pin = new WarpPinnedUInt32((uint[])input.Clone());
                if (itemCount > 0) { Check(owner.api.CopyHostToDevice(pointer, pin.Pointer, bytes), "cuMemcpyHtoD"); }
            }
            return inputPointers;
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                if (released) { return; }
                ObjectDisposedException.ThrowIf(owner.disposed, owner);
                using var scope = owner.EnterContext();
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

    private sealed class ContextScope(CudaApi api) : IDisposable
    {
        public void Dispose() => Check(api.ContextPopCurrent(out _), "cuCtxPopCurrent");
    }

    private sealed class PinnedBytes : IDisposable
    {
        private GCHandle pin;
        public PinnedBytes(byte[] bytes) => pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        public IntPtr Pointer => pin.AddrOfPinnedObject();
        public void Dispose() { if (pin.IsAllocated) { pin.Free(); } }
    }

    private sealed class CudaApi
    {
        public CudaApi(WarpNativeLibrary library)
        {
            Init = library.Export<InitDelegate>("cuInit");
            DeviceGetCount = library.Export<GetIntegerDelegate>("cuDeviceGetCount");
            DeviceGet = library.Export<DeviceGetDelegate>("cuDeviceGet");
            DeviceGetAttribute = library.Export<DeviceAttributeDelegate>("cuDeviceGetAttribute");
            DeviceTotalMem = library.Export<DeviceMemoryDelegate>("cuDeviceTotalMem_v2");
            DeviceGetName = library.Export<DeviceStringDelegate>("cuDeviceGetName");
            DeviceGetPciBusId = library.Export<DeviceStringDelegate>("cuDeviceGetPCIBusId");
            DriverGetVersion = library.Export<GetIntegerDelegate>("cuDriverGetVersion");
            ContextCreate = library.Export<ContextCreateDelegate>("cuCtxCreate_v2");
            ContextDestroy = library.Export<HandleDelegate>("cuCtxDestroy_v2");
            ContextPushCurrent = library.Export<HandleDelegate>("cuCtxPushCurrent_v2");
            ContextPopCurrent = library.Export<GetHandleDelegate>("cuCtxPopCurrent_v2");
            ContextSynchronize = library.Export<NoArgumentsDelegate>("cuCtxSynchronize");
            ModuleLoadDataEx = library.Export<ModuleLoadDelegate>("cuModuleLoadDataEx");
            ModuleGetFunction = library.Export<GetFunctionDelegate>("cuModuleGetFunction");
            ModuleUnload = library.Export<HandleDelegate>("cuModuleUnload");
            FunctionGetAttribute = library.Export<FunctionAttributeDelegate>("cuFuncGetAttribute");
            MemoryAllocate = library.Export<MemoryAllocateDelegate>("cuMemAlloc_v2");
            MemoryFree = library.Export<MemoryFreeDelegate>("cuMemFree_v2");
            CopyHostToDevice = library.Export<HostToDeviceDelegate>("cuMemcpyHtoD_v2");
            CopyDeviceToHost = library.Export<DeviceToHostDelegate>("cuMemcpyDtoH_v2");
            LaunchKernel = library.Export<LaunchDelegate>("cuLaunchKernel");
        }

        public InitDelegate Init { get; }
        public GetIntegerDelegate DeviceGetCount { get; }
        public DeviceGetDelegate DeviceGet { get; }
        public DeviceAttributeDelegate DeviceGetAttribute { get; }
        public DeviceMemoryDelegate DeviceTotalMem { get; }
        public DeviceStringDelegate DeviceGetName { get; }
        public DeviceStringDelegate DeviceGetPciBusId { get; }
        public GetIntegerDelegate DriverGetVersion { get; }
        public ContextCreateDelegate ContextCreate { get; }
        public HandleDelegate ContextDestroy { get; }
        public HandleDelegate ContextPushCurrent { get; }
        public GetHandleDelegate ContextPopCurrent { get; }
        public NoArgumentsDelegate ContextSynchronize { get; }
        public ModuleLoadDelegate ModuleLoadDataEx { get; }
        public GetFunctionDelegate ModuleGetFunction { get; }
        public HandleDelegate ModuleUnload { get; }
        public FunctionAttributeDelegate FunctionGetAttribute { get; }
        public MemoryAllocateDelegate MemoryAllocate { get; }
        public MemoryFreeDelegate MemoryFree { get; }
        public HostToDeviceDelegate CopyHostToDevice { get; }
        public DeviceToHostDelegate CopyDeviceToHost { get; }
        public LaunchDelegate LaunchKernel { get; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int InitDelegate(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetIntegerDelegate(out int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeviceGetDelegate(out int device, int ordinal);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeviceAttributeDelegate(out int value, int attribute, int device);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeviceMemoryDelegate(out nuint bytes, int device);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeviceStringDelegate([Out] StringBuilder value, int length, int device);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ContextCreateDelegate(out IntPtr context, uint flags, int device);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int HandleDelegate(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetHandleDelegate(out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int NoArgumentsDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ModuleLoadDelegate(out IntPtr module, IntPtr image, uint count, IntPtr options, IntPtr values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFunctionDelegate(out IntPtr function, IntPtr module, [MarshalAs(UnmanagedType.LPStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int FunctionAttributeDelegate(out int value, int attribute, IntPtr function);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MemoryAllocateDelegate(out ulong pointer, nuint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MemoryFreeDelegate(ulong pointer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int HostToDeviceDelegate(ulong destination, IntPtr source, nuint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeviceToHostDelegate(IntPtr destination, ulong source, nuint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int LaunchDelegate(IntPtr function, uint gridX, uint gridY, uint gridZ,
        uint blockX, uint blockY, uint blockZ, uint sharedBytes, IntPtr stream, IntPtr arguments, IntPtr extra);
}
