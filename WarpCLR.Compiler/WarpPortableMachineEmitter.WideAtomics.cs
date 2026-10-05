using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed partial class WarpPortableMachineEmitter
{
    private sealed partial class Emission
    {
        private void AppendWideAtomicRequirements()
        {
            if (backend != WarpBackendKind.SPIRV || !layout.RequiresWideAtomics) { return; }
            Line($"; required validation: {WarpManagedWideAtomicOpCode.OpenClValidation}");
            Line("!warpclr.required.opencl.extensions = !{!10000, !10001}");
            Line($"!10000 = !{{!\"{WarpManagedWideAtomicOpCode.OpenClBaseExtension}\"}}");
            Line($"!10001 = !{{!\"{WarpManagedWideAtomicOpCode.OpenClExtendedExtension}\"}}");
        }

        private void AppendWideAtomicBoundsCheck(WarpIrInstruction instruction, WarpLogicalMachineNode node)
        {
            string index = Assign($"zext i32 {LoadValue(instruction.Left)} to i64");
            string count = Assign("zext i32 %warp_heap_words to i64");
            string end = Assign($"add i64 {index}, 1");
            string invalid = Assign($"icmp uge i64 {end}, {count}");
            string suffix = N(temporary++);
            Line($"  br i1 {invalid}, label %wide_bounds_{suffix}, label %wide_check_{suffix}");
            Line($"wide_bounds_{suffix}:"); AppendFault(node, WarpLogicalMachineLayout.ManagedMemoryBoundsFault);
            Line($"wide_check_{suffix}:");
            string pointer = Assign($"getelementptr i32, ptr addrspace(1) %warp_heap, i64 {index}");
            string bytes = Assign($"ptrtoint ptr addrspace(1) {pointer} to i64");
            string residue = Assign($"and i64 {bytes}, 7");
            string misaligned = Assign($"icmp ne i64 {residue}, 0");
            Line($"  br i1 {misaligned}, label %wide_alignment_{suffix}, label %wide_valid_{suffix}");
            Line($"wide_alignment_{suffix}:"); AppendFault(node, WarpLogicalMachineLayout.AtomicAlignmentFault);
            Line($"wide_valid_{suffix}:");
        }

        private void AppendWideAtomic(WarpIrInstruction instruction)
        {
            string index = Assign($"zext i32 {LoadValue(instruction.Left)} to i64");
            string pointer = Assign($"getelementptr i32, ptr addrspace(1) %warp_heap, i64 {index}");
            string result = backend == WarpBackendKind.NVPTX ? EmitPtxWideAtomic(instruction, pointer) : EmitLlvmWideAtomic(instruction, pointer);
            StoreValue(instruction.Result, Assign($"trunc i64 {result} to i32"));
            string high = Assign($"lshr i64 {result}, 32");
            StoreValue(instruction.Result + 1, Assign($"trunc i64 {high} to i32"));
        }

        private string EmitLlvmWideAtomic(WarpIrInstruction instruction, string pointer)
        {
            if (instruction.OpCode is WarpManagedWideAtomicOpCode.LoadSequential or WarpManagedWideAtomicOpCode.LoadAcquire)
            {
                string order = instruction.OpCode == WarpManagedWideAtomicOpCode.LoadSequential ? "seq_cst" : "acquire";
                return Assign($"load atomic i64, ptr addrspace(1) {pointer} {order}, align 8");
            }
            string value = LoadWideAtomicOperand(instruction);
            if (instruction.OpCode == WarpManagedWideAtomicOpCode.StoreRelease)
            {
                Line($"  store atomic i64 {value}, ptr addrspace(1) {pointer} release, align 8");
                return value;
            }
            if (instruction.OpCode == WarpManagedWideAtomicOpCode.CompareExchange)
            {
                string pair = Assign($"cmpxchg ptr addrspace(1) {pointer}, i64 {value}, i64 {LoadWordPair(instruction.Arguments, 2)} seq_cst seq_cst, align 8");
                return Assign($"extractvalue {{ i64, i1 }} {pair}, 0");
            }
            string operation = WideRmwOperation(instruction.OpCode);
            string old = Assign($"atomicrmw {operation} ptr addrspace(1) {pointer}, i64 {value} seq_cst, align 8");
            return WideAtomicResult(instruction.OpCode, old, value);
        }

        private string EmitPtxWideAtomic(WarpIrInstruction instruction, string pointer)
        {
            if (instruction.OpCode is WarpManagedWideAtomicOpCode.LoadSequential or WarpManagedWideAtomicOpCode.LoadAcquire)
            {
                bool sequential = instruction.OpCode == WarpManagedWideAtomicOpCode.LoadSequential;
                if (sequential) { EmitPtxSequentialFence(); }
                string loaded = Assign($"call i64 asm sideeffect \"ld.acquire.sys.global.u64 $0, [$1];\", \"=l,l,~{{memory}}\"(ptr addrspace(1) {pointer})");
                if (sequential) { EmitPtxSequentialFence(); }
                return loaded;
            }
            string value = LoadWideAtomicOperand(instruction);
            if (instruction.OpCode == WarpManagedWideAtomicOpCode.StoreRelease)
            {
                Line($"  call void asm sideeffect \"st.release.sys.global.u64 [$0], $1;\", \"l,l,~{{memory}}\"(ptr addrspace(1) {pointer}, i64 {value})");
                return value;
            }
            EmitPtxSequentialFence();
            if (instruction.OpCode == WarpManagedWideAtomicOpCode.CompareExchange)
            {
                return Assign($"call i64 asm sideeffect \"atom.acquire.sys.global.cas.b64 $0, [$1], $2, $3;\", \"=l,l,l,l,~{{memory}}\"(ptr addrspace(1) {pointer}, i64 {value}, i64 {LoadWordPair(instruction.Arguments, 2)})");
            }
            string operation = WideRmwOperation(instruction.OpCode) switch { "add" => "add.u64", "and" => "and.b64", "or" => "or.b64", _ => "exch.b64" };
            string old = Assign($"call i64 asm sideeffect \"atom.acquire.sys.global.{operation} $0, [$1], $2;\", \"=l,l,l,~{{memory}}\"(ptr addrspace(1) {pointer}, i64 {value})");
            return WideAtomicResult(instruction.OpCode, old, value);
        }

        private string LoadWideAtomicOperand(WarpIrInstruction instruction) => instruction.OpCode switch
        {
            WarpManagedWideAtomicOpCode.Increment => "1",
            WarpManagedWideAtomicOpCode.Decrement => "-1",
            _ => LoadWordPair(instruction.Arguments, 0),
        };

        private static string WideRmwOperation(WarpIrOpCode opCode) => opCode switch
        {
            WarpManagedWideAtomicOpCode.Add or WarpManagedWideAtomicOpCode.Increment or WarpManagedWideAtomicOpCode.Decrement => "add",
            WarpManagedWideAtomicOpCode.And => "and",
            WarpManagedWideAtomicOpCode.Or => "or",
            _ => "xchg",
        };

        private string WideAtomicResult(WarpIrOpCode opCode, string old, string value) => opCode switch
        {
            WarpManagedWideAtomicOpCode.StoreSequential => value,
            WarpManagedWideAtomicOpCode.Add or WarpManagedWideAtomicOpCode.Increment or WarpManagedWideAtomicOpCode.Decrement => Assign($"add i64 {old}, {value}"),
            _ => old,
        };

        private string LoadWordPair(IReadOnlyList<int> operands, int offset)
        {
            string low = Assign($"zext i32 {LoadValue(operands[offset])} to i64");
            string high = Assign($"zext i32 {LoadValue(operands[offset + 1])} to i64");
            return Assign($"or i64 {low}, {Assign($"shl i64 {high}, 32")}");
        }
    }
}
