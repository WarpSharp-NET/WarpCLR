using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed partial class CoreCLRResumableKernel
{
    private sealed partial class QuantumEmitter
    {
        private LocalBuilder? wideAtomicValue;
        private void EmitWideAtomicBoundsCheck(WarpIrInstruction instruction, WarpLogicalMachineNode node)
        {
            Label enough = il.DefineLabel(), valid = il.DefineLabel(), aligned = il.DefineLabel();
            LoadArena(); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_U4); Constant(2);
            il.Emit(OpCodes.Bge_Un, enough);
            EmitFault(node, WarpLogicalMachineLayout.ManagedMemoryBoundsFault);
            il.MarkLabel(enough);
            LoadValue(instruction.Left); LoadArena(); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_U4);
            Constant(1); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Blt_Un, valid);
            EmitFault(node, WarpLogicalMachineLayout.ManagedMemoryBoundsFault);
            il.MarkLabel(valid);
            LoadArena(); LoadValue(instruction.Left); il.Emit(OpCodes.Ldelema, typeof(uint));
            il.Emit(OpCodes.Conv_U); Constant(7); il.Emit(OpCodes.Conv_U); il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brfalse, aligned);
            EmitFault(node, WarpLogicalMachineLayout.AtomicAlignmentFault);
            il.MarkLabel(aligned);
        }

        private void EmitWideAtomic(WarpIrInstruction instruction)
        {
            // UInt64 appears only at the physical atomic boundary. Every source value remains two UInt32 SSA words.
            LocalBuilder value = wideAtomicValue ??= il.DeclareLocal(typeof(ulong));
            EmitWideAtomicReference(instruction.Left);
            if (instruction.OpCode is WarpManagedWideAtomicOpCode.LoadSequential or WarpManagedWideAtomicOpCode.LoadAcquire)
            {
                il.Emit(OpCodes.Call, WideAtomicMethod(instruction.OpCode == WarpManagedWideAtomicOpCode.LoadSequential
                    ? typeof(Interlocked) : typeof(Volatile), nameof(Volatile.Read), 1));
            }
            else { EmitWideAtomicMutation(instruction); }
            il.Emit(OpCodes.Stloc, value);
            StoreFrame(frame, WarpLogicalMachineLayout.FrameHeaderWords + instruction.Result, () =>
            {
                il.Emit(OpCodes.Ldloc, value); il.Emit(OpCodes.Conv_U4);
            });
            StoreFrame(frame, WarpLogicalMachineLayout.FrameHeaderWords + instruction.Result + 1, () =>
            {
                il.Emit(OpCodes.Ldloc, value); Constant(32); il.Emit(OpCodes.Shr_Un); il.Emit(OpCodes.Conv_U4);
            });
        }

        private void EmitWideAtomicMutation(WarpIrInstruction instruction)
        {
            if (instruction.OpCode is WarpManagedWideAtomicOpCode.Increment or WarpManagedWideAtomicOpCode.Decrement)
            {
                string operation = instruction.OpCode == WarpManagedWideAtomicOpCode.Increment ? nameof(Interlocked.Increment) : nameof(Interlocked.Decrement);
                il.Emit(OpCodes.Call, WideAtomicMethod(typeof(Interlocked), operation, 1));
                return;
            }
            LoadWordPair(instruction.Arguments, instruction.OpCode == WarpManagedWideAtomicOpCode.CompareExchange ? 2 : 0);
            if (instruction.OpCode == WarpManagedWideAtomicOpCode.StoreRelease)
            {
                il.Emit(OpCodes.Call, WideAtomicMethod(typeof(Volatile), nameof(Volatile.Write), 2));
                LoadWordPair(instruction.Arguments, 0);
                return;
            }
            string name = instruction.OpCode switch
            {
                WarpManagedWideAtomicOpCode.CompareExchange => nameof(Interlocked.CompareExchange),
                WarpManagedWideAtomicOpCode.Add => nameof(Interlocked.Add),
                WarpManagedWideAtomicOpCode.And => nameof(Interlocked.And),
                WarpManagedWideAtomicOpCode.Or => nameof(Interlocked.Or),
                _ => nameof(Interlocked.Exchange),
            };
            if (instruction.OpCode == WarpManagedWideAtomicOpCode.CompareExchange) { LoadWordPair(instruction.Arguments, 0); }
            il.Emit(OpCodes.Call, WideAtomicMethod(typeof(Interlocked), name, instruction.OpCode == WarpManagedWideAtomicOpCode.CompareExchange ? 3 : 2));
            if (instruction.OpCode == WarpManagedWideAtomicOpCode.StoreSequential)
            {
                il.Emit(OpCodes.Pop); LoadWordPair(instruction.Arguments, 0);
            }
        }

        private void EmitWideAtomicReference(int address)
        {
            LoadArena(); LoadValue(address); il.Emit(OpCodes.Ldelema, typeof(uint));
            MethodInfo cast = typeof(Unsafe).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(method => string.Equals(method.Name, nameof(Unsafe.As), StringComparison.Ordinal) &&
                    method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 2 && method.GetParameters().Length == 1);
            il.Emit(OpCodes.Call, cast.MakeGenericMethod(typeof(uint), typeof(ulong)));
        }

        private void LoadWordPair(IReadOnlyList<int> arguments, int offset)
        {
            LoadValue(arguments[offset]); il.Emit(OpCodes.Conv_U8);
            LoadValue(arguments[offset + 1]); il.Emit(OpCodes.Conv_U8); Constant(32); il.Emit(OpCodes.Shl); il.Emit(OpCodes.Or);
        }

        private static MethodInfo WideAtomicMethod(Type declaring, string name, int count) => declaring.GetMethod(name,
            count == 1 ? [typeof(ulong).MakeByRefType()] : count == 2 ? [typeof(ulong).MakeByRefType(), typeof(ulong)] :
                [typeof(ulong).MakeByRefType(), typeof(ulong), typeof(ulong)])
            ?? throw new InvalidOperationException("CoreCLR lacks the required full-width UInt64 atomic operation.");
    }
}
