using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static class WarpFloatingPointMapLowerer
{
    public static WarpFloatingPointMapPlan Lower(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        WarpInitializationAdmission.RequireModule(method.Module);
        WarpInitializationAdmission.RequireMethod(method);
        MethodBody body = method.GetMethodBody() ?? throw Error("The method has no CIL body.", 0);
        ValidateSignature(method, body);
        byte[] cil = body.GetILAsByteArray() ?? throw Error("The method has no CIL bytes.", 0);
        WarpCompilationAdmission.Require(method.Name, WarpCompilationResourceKind.CilBytes,
            cil.Length, WarpCompilationAdmission.MaximumCilBytesPerBody);
        return new Lowering(method, body, cil).Lower();
    }

    private static void ValidateSignature(MethodInfo method, MethodBody body)
    {
        Type type = method.ReturnType;
        ParameterInfo[] parameters = method.GetParameters();
        if (!method.IsStatic || method.IsAbstract || method.ContainsGenericParameters ||
            method.CallingConvention != CallingConventions.Standard || method.DeclaringType is null ||
            method.DeclaringType.IsNested || method.DeclaringType.IsGenericType ||
            (type != typeof(float) && type != typeof(double)) || parameters.Length == 0 ||
            parameters.Any(parameter => parameter.ParameterType != type) ||
            body.LocalVariables.Any(local => local.LocalType != type || local.IsPinned) || body.ExceptionHandlingClauses.Count != 0)
        {
            throw Error("The internal floating-point map requires a concrete top-level static method with homogeneous Single or Double values and no exception regions.", 0);
        }

        WarpCompilationAdmission.Require(method.Name, WarpCompilationResourceKind.Parameters,
            parameters.Length * (type == typeof(double) ? 2L : 1L), WarpCompilationAdmission.MaximumParametersPerBody);
        WarpCompilationAdmission.Require(method.Name, WarpCompilationResourceKind.Locals,
            body.LocalVariables.Count, WarpCompilationAdmission.MaximumLocalsPerBody);
        WarpCompilationAdmission.Require(method.Name, WarpCompilationResourceKind.EvaluationStack,
            body.MaxStackSize, WarpCompilationAdmission.MaximumEvaluationStackPerBody);
    }

    private static WarpVerificationException Error(string message, int offset) =>
        new("WRPNUM1000", $"Floating-point CIL at IL_{offset:X4}: {message}", offset);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Value(int Low, int High);

    private sealed class Lowering
    {
        private readonly MethodInfo method;
        private readonly MethodBody body;
        private readonly Reader reader;
        private readonly bool wide;
        private readonly Value[] arguments;
        private readonly Value?[] locals;
        private readonly Stack<Value> stack = new();
        private readonly List<WarpIrInstruction> instructions = [];
        private readonly List<WarpControlFlowFunction> functions = [];
        private readonly Dictionary<int, (int Low, int High)> operators = [];
        private readonly Dictionary<string, int> importedHelpers = new(StringComparer.Ordinal);
        private IReadOnlyList<WarpLogicalMachineLayout>? numericLibrary;

        public Lowering(MethodInfo method, MethodBody body, byte[] cil)
        {
            this.method = method;
            this.body = body;
            reader = new Reader(cil);
            wide = method.ReturnType == typeof(double);
            arguments = new Value[method.GetParameters().Length];
            locals = new Value?[body.LocalVariables.Count];
            for (int index = 0; index < arguments.Length; index++)
            {
                int low = Append(WarpIrOpCode.LoadInput, immediate: checked((uint)(index * Words)));
                arguments[index] = new Value(low, wide ? Append(WarpIrOpCode.LoadInput, immediate: checked((uint)(index * Words + 1))) : -1);
            }

            if (body.InitLocals && locals.Length != 0)
            {
                int zero = Append(WarpIrOpCode.Constant);
                Array.Fill(locals, new Value(zero, wide ? zero : -1));
            }
        }

        private int Words => wide ? 2 : 1;

        public WarpFloatingPointMapPlan Lower()
        {
            while (!reader.Complete)
            {
                int offset = reader.Position;
                int operation = reader.ReadByte();
                if (operation == 0xFE)
                {
                    operation = 0xFE00 | reader.ReadByte();
                }

                if (operation == 0x2A)
                {
                    if (!reader.Complete || stack.Count != 1)
                    {
                        throw Error("A single final return value is required; control flow is not yet admitted.", offset);
                    }

                    return Finish(stack.Pop());
                }

                if (!LowerValueOperation(operation, offset) && !LowerStackOperation(operation, offset))
                {
                    throw Error($"Opcode 0x{operation:X4} is not yet admitted; no alternate precision or operation is substituted.", offset);
                }
            }

            throw Error("The method does not end in ret.", reader.Position);
        }

        private bool LowerValueOperation(int operation, int offset)
        {
            switch (operation)
            {
                case >= 0x02 and <= 0x05:
                    Push(GetArgument(operation - 0x02, offset), offset);
                    return true;
                case 0x0E:
                case 0xFE09:
                    Push(GetArgument(operation == 0x0E ? reader.ReadByte() : reader.ReadUInt16(), offset), offset);
                    return true;
                case 0x10:
                case 0xFE0B:
                    SetArgument(operation == 0x10 ? reader.ReadByte() : reader.ReadUInt16(), Pop(offset), offset);
                    return true;
                case >= 0x06 and <= 0x09:
                    Push(GetLocal(operation - 0x06, offset), offset);
                    return true;
                case 0x11:
                case 0xFE0C:
                    Push(GetLocal(operation == 0x11 ? reader.ReadByte() : reader.ReadUInt16(), offset), offset);
                    return true;
                case >= 0x0A and <= 0x0D:
                    SetLocal(operation - 0x0A, Pop(offset), offset);
                    return true;
                case 0x13:
                case 0xFE0E:
                    SetLocal(operation == 0x13 ? reader.ReadByte() : reader.ReadUInt16(), Pop(offset), offset);
                    return true;
                case 0x22 when !wide:
                case 0x23 when wide:
                    int low = Append(WarpIrOpCode.Constant, immediate: reader.ReadUInt32());
                    Push(new Value(low, wide ? Append(WarpIrOpCode.Constant, immediate: reader.ReadUInt32()) : -1), offset);
                    return true;
                default:
                    return false;
            }
        }

        private bool LowerStackOperation(int operation, int offset)
        {
            switch (operation)
            {
                case 0x00:
                    return true;
                case 0x25:
                    Value duplicate = Pop(offset);
                    Push(duplicate, offset);
                    Push(duplicate, offset);
                    return true;
                case 0x26:
                    _ = Pop(offset);
                    return true;
                case >= 0x58 and <= 0x5B:
                    Value right = Pop(offset);
                    Value left = Pop(offset);
                    (int low, int high) = GetOperator(operation - 0x58);
                    int[] values = wide ? [left.Low, left.High, right.Low, right.High] : [left.Low, right.Low];
                    int resultLow = Append(WarpIrOpCode.Call, callee: low, callArguments: values);
                    int resultHigh = wide ? Append(WarpIrOpCode.Call, callee: high, callArguments: values) : -1;
                    Push(new Value(resultLow, resultHigh), offset);
                    return true;
                default:
                    return false;
            }
        }

        private (int Low, int High) GetOperator(int operation)
        {
            if (operators.TryGetValue(operation, out (int Low, int High) result))
            {
                return result;
            }

            IReadOnlyList<WarpLogicalMachineLayout> primitives = numericLibrary ??= wide
                ? WarpPortableNumericKernels.CreateBinary64Arithmetic()
                : WarpPortableNumericKernels.CreateBinary32Arithmetic();
            int low = Import(primitives[operation * Words].Kernel);
            int high = wide ? Import(primitives[operation * Words + 1].Kernel) : -1;
            result = (low, high);
            operators.Add(operation, result);
            return result;
        }

        private int Import(WarpControlFlowKernel primitive)
        {
            int first = functions.Count;
            var added = new List<WarpControlFlowFunction>();
            foreach (WarpControlFlowFunction function in primitive.Functions)
            {
                if (importedHelpers.TryAdd(function.Name, first + 1 + added.Count))
                {
                    added.Add(function);
                }
            }

            int[] callees = primitive.Functions.Select(function => importedHelpers[function.Name]).ToArray();
            functions.Add(new WarpControlFlowFunction(first, primitive.Name, primitive.InputBufferCount,
                Relocate(primitive.Blocks, callees, primitive.InputBufferCount)));
            string semantics = wide ? WarpPortableBinary64.Semantics : WarpPortableBinary32.Semantics;
            foreach (ref readonly WarpControlFlowFunction function in CollectionsMarshal.AsSpan(added))
            {
                functions.Add(new WarpControlFlowFunction(importedHelpers[function.Name], semantics + "::" + function.Name, function.ParameterCount,
                    Relocate(function.Blocks, callees, inputBufferCount: 0)));
            }

            return first;
        }

        private static IEnumerable<WarpBasicBlock> Relocate(IEnumerable<WarpBasicBlock> blocks, int[] callees, int inputBufferCount)
        {
            foreach (WarpBasicBlock block in blocks)
            {
                WarpIrInstruction[] relocated = block.Instructions.Select(instruction => new WarpIrInstruction(
                    instruction.Result,
                    instruction.OpCode is WarpIrOpCode.LoadInput or WarpIrOpCode.LoadScalar ? WarpIrOpCode.LoadArgument : instruction.OpCode,
                    instruction.Left, instruction.Right,
                    instruction.OpCode == WarpIrOpCode.LoadScalar ? instruction.Immediate + checked((uint)inputBufferCount) : instruction.Immediate,
                    instruction.Third, instruction.ResultType,
                    instruction.OpCode == WarpIrOpCode.Call ? callees[instruction.Callee] : instruction.Callee,
                    instruction.Arguments)).ToArray();
                yield return new WarpBasicBlock(block.Id, block.Parameters, relocated, block.Terminator);
            }
        }

        private WarpFloatingPointMapPlan Finish(Value result)
        {
            string semantics = wide ? WarpPortableBinary64.Semantics : WarpPortableBinary32.Semantics;
            string name = $"{method.DeclaringType!.FullName}.{method.Name}/{semantics}";
            WarpBlockTerminator terminator = wide
                ? new WarpWideReturnTerminator(result.Low, result.High)
                : new WarpReturnTerminator(result.Low);
            var kernel = new WarpControlFlowKernel(name, arguments.Length * Words, 0,
                [new WarpBasicBlock(0, [], instructions, terminator)], functions: functions);
            return new WarpFloatingPointMapPlan(method.ReturnType, arguments.Length, new WarpLogicalMachineLayout(kernel));
        }

        private int Append(WarpIrOpCode operation, uint immediate = 0, int callee = -1, int[]? callArguments = null)
        {
            WarpCompilationAdmission.Require(method.Name, WarpCompilationResourceKind.Instructions,
                instructions.Count + 1L, WarpCompilationAdmission.MaximumInstructionsPerEntry);
            int result = instructions.Count;
            instructions.Add(new WarpIrInstruction(result, operation, immediate: immediate, callee: callee, arguments: callArguments));
            return result;
        }

        private void Push(Value value, int offset)
        {
            if (stack.Count >= body.MaxStackSize)
            {
                throw Error("The evaluation stack exceeds the declared maximum.", offset);
            }

            stack.Push(value);
        }

        private Value Pop(int offset) => stack.Count != 0 ? stack.Pop() : throw Error("The evaluation stack underflows.", offset);

        private Value GetArgument(int index, int offset) => index >= 0 && index < arguments.Length
            ? arguments[index] : throw Error("The argument index is out of range.", offset);

        private void SetArgument(int index, Value value, int offset)
        {
            _ = GetArgument(index, offset);
            arguments[index] = value;
        }

        private Value GetLocal(int index, int offset) => index >= 0 && index < locals.Length
            ? locals[index] ?? throw Error("An uninitialized local is read.", offset)
            : throw Error("The local index is out of range.", offset);

        private void SetLocal(int index, Value value, int offset)
        {
            if (index < 0 || index >= locals.Length)
            {
                throw Error("The local index is out of range.", offset);
            }

            locals[index] = value;
        }
    }

    private sealed class Reader(byte[] bytes)
    {
        public int Position { get; private set; }

        public bool Complete => Position == bytes.Length;

        public int ReadByte()
        {
            Require(1);
            return bytes[Position++];
        }

        public int ReadUInt16()
        {
            Require(2);
            int value = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(Position));
            Position += 2;
            return value;
        }

        public uint ReadUInt32()
        {
            Require(4);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(Position));
            Position += 4;
            return value;
        }

        private void Require(int count)
        {
            if (bytes.Length - Position < count)
            {
                throw Error("The instruction operand is truncated.", Position);
            }
        }
    }
}
