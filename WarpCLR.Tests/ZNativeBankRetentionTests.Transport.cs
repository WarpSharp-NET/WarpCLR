using System.Collections;
using System.Runtime.InteropServices;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests.Production;

internal sealed partial class ZNativeBankRetentionTests
{
    private static WarpLogicalMachineLayout Layout(int inputs = 1) => new(new WarpControlFlowKernel(
        "native-bank-retention-cpu-memory-fixture", inputs, 1,
        [new WarpBasicBlock(0, [], [new WarpIrInstruction(0, WarpIrOpCode.LoadInput),
            new WarpIrInstruction(1, WarpIrOpCode.LoadScalar), new WarpIrInstruction(2, WarpIrOpCode.Add, 0, 1)],
            new WarpReturnTerminator(2))]));

    private static WarpNativeImage Image(WarpLogicalMachineLayout layout) =>
        new(new WarpNativeTarget(WarpBackendKind.NVPTX, "sm_80", "cpu-memory-ownership-fixture", "no-device-execution",
            256, int.MaxValue, 1UL << 32, 4096), WarpNativeImageFormat.Ptx, WarpPortableMachineEmitter.EntryPoint,
            [1], "cpu-memory-fixture-only", "no-native-toolchain-run", layout.Kernel.InputBufferCount, layout.Kernel.ScalarArgumentCount, layout);

    private static ulong ReadPointer(IntPtr arguments, int index) =>
        unchecked((ulong)Marshal.ReadInt64(Marshal.ReadIntPtr(arguments, index * IntPtr.Size)));

    private static uint ReadWord(IntPtr arguments, int index) =>
        unchecked((uint)Marshal.ReadInt32(Marshal.ReadIntPtr(arguments, index * IntPtr.Size)));

    private static void CompleteMachine(IntPtr arguments) => CompleteMachine(arguments, inputs: 1);

    private static void CompleteMachine(IntPtr arguments, int inputs)
    {
        IntPtr state = new(unchecked((long)ReadPointer(arguments, 0)));
        IntPtr input = new(unchecked((long)ReadPointer(arguments, 1)));
        uint first = unchecked((uint)Marshal.ReadInt32(input));
        uint scalar = ReadWord(arguments, inputs + 1);
        Marshal.WriteInt32(state, WarpLogicalMachineLayout.ResultOffset * sizeof(uint), unchecked((int)(first + scalar)));
        Marshal.WriteInt32(state, WarpLogicalMachineLayout.StatusOffset * sizeof(uint), (int)WarpLogicalMachineLayout.Completed);
    }

    private static void SumReduction(IntPtr arguments)
    {
        IntPtr output = new(unchecked((long)ReadPointer(arguments, 0))), input = new(unchecked((long)ReadPointer(arguments, 1)));
        uint count = ReadWord(arguments, 2);
        for (uint index = 0; index < (count + 1) / 2; index++)
        {
            uint left = unchecked((uint)Marshal.ReadInt32(input, checked((int)(2 * index * sizeof(uint)))));
            uint right = 2 * index + 1 < count ? unchecked((uint)Marshal.ReadInt32(input, checked((int)((2 * index + 1) * sizeof(uint))))) : 0;
            Marshal.WriteInt32(output, checked((int)(index * sizeof(uint))), unchecked((int)(left + right)));
        }
    }

    // Real host allocations and copies, exclusively for ordinary lifetime
    // accounting. No driver is loaded and no backend code or worker executes.
    private sealed class Transport : IDisposable
    {
        internal Transport() => Operations = new(Allocate, Upload, Readback,
            (arguments, _, _) => { Launches++; OnLaunch?.Invoke(arguments); }, static () => { }, Free,
            () => Quarantined = true, this);

        internal WarpMachineMemoryOperations Operations { get; }
        internal Dictionary<ulong, WarpNativeBlock> Blocks { get; } = [];
        internal Dictionary<int, Exception> FreeFailures { get; } = [];
        internal Action<int>? BeforeAllocation { get; set; }
        internal Action<int>? BeforeFree { get; set; }
        internal Action<IntPtr>? OnLaunch { get; set; }
        internal Action? AfterReadback { get; set; }
        internal Exception? AllocationFailure { get; init; }
        internal Exception? UploadFailure { get; init; }
        internal int UploadFailureAt { get; init; }
        internal bool FreesBeforeThrowing { get; init; }
        internal int AllocationAttempts { get; private set; }
        internal int FreeAttempts { get; private set; }
        internal int Uploads { get; private set; }
        internal int Readbacks { get; private set; }
        internal int Launches { get; private set; }
        internal bool Quarantined { get; private set; }

        private ulong Allocate(nuint bytes)
        {
            AllocationAttempts++;
            BeforeAllocation?.Invoke(AllocationAttempts);
            if (AllocationFailure is not null) { throw AllocationFailure; }
            var block = new WarpNativeBlock(checked((int)bytes));
            ulong pointer = unchecked((ulong)block.Pointer.ToInt64());
            Blocks.Add(pointer, block);
            return pointer;
        }

        private void Upload(ulong destination, IntPtr source, nuint bytes)
        {
            Uploads++;
            if (UploadFailure is not null && Uploads == UploadFailureAt) { throw UploadFailure; }
            Copy(new IntPtr(unchecked((long)destination)), source, bytes);
        }

        private void Readback(IntPtr destination, ulong source, nuint bytes)
        {
            Readbacks++;
            Copy(destination, new IntPtr(unchecked((long)source)), bytes);
            AfterReadback?.Invoke();
        }

        private static void Copy(IntPtr destination, IntPtr source, nuint bytes)
        {
            byte[] content = new byte[checked((int)bytes)];
            Marshal.Copy(source, content, 0, content.Length);
            Marshal.Copy(content, 0, destination, content.Length);
        }

        private void Free(ulong pointer)
        {
            FreeAttempts++;
            BeforeFree?.Invoke(FreeAttempts);
            bool failed = FreeFailures.TryGetValue(FreeAttempts, out Exception? failure);
            if (failed && !FreesBeforeThrowing) { throw failure!; }
            Blocks[pointer].Dispose();
            Blocks.Remove(pointer);
            if (failed) { throw failure!; }
        }

        public void Dispose()
        {
            foreach (WarpNativeBlock block in Blocks.Values) { block.Dispose(); }
            Blocks.Clear();
        }
    }

    private sealed class SingleCaptureInputs(uint[] original, uint[] replacement) : IReadOnlyList<uint[]>
    {
        internal int CountReads { get; private set; }
        internal int ReferenceReads { get; private set; }
        public int Count { get { CountReads++; return 1; } }
        public uint[] this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNotEqual(index, 0);
                ReferenceReads++;
                return ReferenceReads == 1 ? original : replacement;
            }
        }
        IEnumerator<uint[]> IEnumerable<uint[]>.GetEnumerator() => throw new InvalidOperationException("The captured outer references must not be re-enumerated.");
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<uint[]>)this).GetEnumerator();
    }

    private sealed class GetterScalars(uint[] values) : IReadOnlyList<uint>
    {
        internal int ValueReads { get; private set; }
        public int Count => values.Length;
        public uint this[int index] { get { ValueReads++; return values[index]; } }
        IEnumerator<uint> IEnumerable<uint>.GetEnumerator()
        {
            for (int index = 0; index < values.Length; index++) { yield return this[index]; }
        }
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<uint>)this).GetEnumerator();
    }
}
