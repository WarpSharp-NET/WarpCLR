using System.Reflection;
using System.Text.Json;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host.Native;
using WarpCLR.Verifier;

namespace WarpCLR.NativeValidation;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || !Path.IsPathFullyQualified(args[0]) || !Path.IsPathFullyQualified(args[1]))
        {
            await Console.Error.WriteLineAsync("Usage: WarpCLR.NativeValidation <absolute LLVM19 bin directory> <absolute manifested assembly path>").ConfigureAwait(false);
            return 2;
        }

        string tools = args[0];
        var toolchain = new WarpNativeToolchain(new WarpNativeToolchainOptions
        {
            LlvmAssembler = Path.Combine(tools, "llvm-as-19"),
            LlvmCodeGenerator = Path.Combine(tools, "llc-19"),
            LlvmLinker = Path.Combine(tools, "ld.lld-19"),
            SpirVTranslator = Path.Combine(tools, "llvm-spirv-19"),
            SpirVValidator = Path.Combine(tools, "spirv-val"),
        });
        WarpVerifiedModule verified = new WarpModuleVerifier().Verify(await File.ReadAllBytesAsync(args[1]).ConfigureAwait(false));
        var reports = new List<ValidationRecord>();
        foreach (WarpVerifiedEntry entry in verified.Entries)
        {
            var layout = new WarpLogicalMachineLayout(new WarpIntegerMapLowerer().Lower(entry.Kernel));
            await ValidateAsync(layout, toolchain, reports).ConfigureAwait(false);
        }

        await ValidateAsync(CreateLoopAndCallLayout(), toolchain, reports).ConfigureAwait(false);
        await ValidateAsync(CreateInstructionSetLayout(), toolchain, reports).ConfigureAwait(false);
        foreach (WarpLogicalMachineLayout layout in WarpPortableNumericKernels.CreateBinary64Arithmetic())
        {
            await ValidateAsync(layout, toolchain, reports).ConfigureAwait(false);
        }
        foreach (WarpLogicalMachineLayout layout in WarpPortableNumericKernels.CreateBinary32Arithmetic())
        {
            await ValidateAsync(layout, toolchain, reports).ConfigureAwait(false);
        }

        foreach (MethodInfo method in typeof(WarpFloatingPointSourceKernels).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            await ValidateAsync(WarpFloatingPointMapLowerer.Lower(method).Layout, toolchain, reports).ConfigureAwait(false);
        }

        await ValidateAsync(WarpPortableResultKernels.CreateWideCall(), toolchain, reports).ConfigureAwait(false);
        await ValidateAsync(WarpPortableResultKernels.CreateVoidCall(), toolchain, reports).ConfigureAwait(false);
        await ValidateAsync(WarpPortableResultKernels.CreateTupleCall(), toolchain, reports).ConfigureAwait(false);
        foreach (WarpLogicalMachineLayout layout in WarpPortableIntegerKernels.Create32()
            .Concat(WarpPortableIntegerKernels.Create64())
            .Concat(WarpPortableSemanticKernels.CreateComparisons())
            .Concat(WarpPortableSemanticKernels.CreateConversions()))
        {
            await ValidateAsync(layout, toolchain, reports).ConfigureAwait(false);
        }

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(reports)).ConfigureAwait(false);
        return 0;
    }

    private static async Task ValidateAsync(WarpLogicalMachineLayout layout, WarpNativeToolchain toolchain, List<ValidationRecord> reports)
    {
        (WarpBackendKind Backend, string Architecture)[] targets =
        [
            (WarpBackendKind.NVPTX, "sm_80"),
            (WarpBackendKind.AMDGPU, "gfx942"),
            (WarpBackendKind.SPIRV, "opencl2.2-spirv1.2"),
        ];
        foreach ((WarpBackendKind backend, string architecture) in targets)
        {
            // This target is a compiler-validation recipe, never a discovered or executed GPU.
            var target = new WarpNativeTarget(backend, architecture, "offline-validation", "no-device-execution", 256, int.MaxValue, 1UL << 32);
            WarpNativeImage image = await toolchain.CompileMachineAsync(layout, target).ConfigureAwait(false);
            reports.Add(new ValidationRecord(layout.Kernel.Name, backend.ToString(), architecture,
                image.Format.ToString(), image.Content.Length, image.ContentHash, image.ToolchainIdentity));
        }
    }

    private static WarpLogicalMachineLayout CreateLoopAndCallLayout()
    {
        var helper = new WarpControlFlowFunction(0, "square", 1,
        [
            new WarpBasicBlock(0, [],
            [
                new WarpIrInstruction(0, WarpIrOpCode.LoadArgument, immediate: 0),
                new WarpIrInstruction(1, WarpIrOpCode.Multiply, 0, 0),
            ], new WarpReturnTerminator(1)),
        ]);
        var kernel = new WarpControlFlowKernel("offline.loop-and-call", 1, 0,
        [
            new WarpBasicBlock(0, [],
            [
                new WarpIrInstruction(0, WarpIrOpCode.LoadInput, immediate: 0),
                new WarpIrInstruction(1, WarpIrOpCode.Constant, immediate: 0),
            ], new WarpBranchTerminator(new WarpBranchTarget(1, [0, 1]))),
            new WarpBasicBlock(1, [new WarpBlockParameter(2), new WarpBlockParameter(3)],
            [
                new WarpIrInstruction(4, WarpIrOpCode.Constant, immediate: 0),
                new WarpIrInstruction(5, WarpIrOpCode.NotEqual, 2, 4),
            ], new WarpConditionalBranchTerminator(5, new WarpBranchTarget(2, [2, 3]), new WarpBranchTarget(3, [3]))),
            new WarpBasicBlock(2, [new WarpBlockParameter(6), new WarpBlockParameter(7)],
            [
                new WarpIrInstruction(8, WarpIrOpCode.Call, callee: 0, arguments: [6]),
                new WarpIrInstruction(9, WarpIrOpCode.Add, 7, 8),
                new WarpIrInstruction(10, WarpIrOpCode.Constant, immediate: 1),
                new WarpIrInstruction(11, WarpIrOpCode.Subtract, 6, 10),
            ], new WarpBranchTerminator(new WarpBranchTarget(1, [11, 9]))),
            new WarpBasicBlock(3, [new WarpBlockParameter(12)], [], new WarpReturnTerminator(12)),
        ], functions: [helper]);
        return new WarpLogicalMachineLayout(kernel);
    }

    private static WarpLogicalMachineLayout CreateInstructionSetLayout()
    {
        var kernel = new WarpControlFlowKernel("offline.instruction-set", 2, 2,
        [
            new WarpBasicBlock(0, [],
            [
                new WarpIrInstruction(0, WarpIrOpCode.LoadInput, immediate: 0),
                new WarpIrInstruction(1, WarpIrOpCode.LoadInput, immediate: 1),
                new WarpIrInstruction(2, WarpIrOpCode.LoadScalar, immediate: 0),
                new WarpIrInstruction(3, WarpIrOpCode.LoadScalar, immediate: 1),
                new WarpIrInstruction(4, WarpIrOpCode.Constant, immediate: 255),
                new WarpIrInstruction(5, WarpIrOpCode.BitwiseNot, 0),
                new WarpIrInstruction(6, WarpIrOpCode.Add, 0, 1),
                new WarpIrInstruction(7, WarpIrOpCode.Subtract, 0, 1),
                new WarpIrInstruction(8, WarpIrOpCode.Multiply, 6, 2),
                new WarpIrInstruction(9, WarpIrOpCode.BitwiseAnd, 8, 4),
                new WarpIrInstruction(10, WarpIrOpCode.BitwiseOr, 9, 5),
                new WarpIrInstruction(11, WarpIrOpCode.ExclusiveOr, 10, 1),
                new WarpIrInstruction(12, WarpIrOpCode.ShiftLeft, 11, 3),
                new WarpIrInstruction(13, WarpIrOpCode.ShiftRightLogical, 12, 3),
                new WarpIrInstruction(14, WarpIrOpCode.Equal, 0, 1),
                new WarpIrInstruction(15, WarpIrOpCode.NotEqual, 0, 1),
                new WarpIrInstruction(16, WarpIrOpCode.LessThanUnsigned, 0, 1),
                new WarpIrInstruction(17, WarpIrOpCode.LessThanOrEqualUnsigned, 0, 1),
                new WarpIrInstruction(18, WarpIrOpCode.GreaterThanUnsigned, 0, 1),
                new WarpIrInstruction(19, WarpIrOpCode.GreaterThanOrEqualUnsigned, 0, 1),
                new WarpIrInstruction(20, WarpIrOpCode.BitwiseOr, 14, 15),
                new WarpIrInstruction(21, WarpIrOpCode.BitwiseOr, 16, 17),
                new WarpIrInstruction(22, WarpIrOpCode.BitwiseOr, 18, 19),
                new WarpIrInstruction(23, WarpIrOpCode.ExclusiveOr, 20, 21),
                new WarpIrInstruction(24, WarpIrOpCode.ExclusiveOr, 23, 22),
                new WarpIrInstruction(25, WarpIrOpCode.Select, 24, 13, third: 7),
            ], new WarpReturnTerminator(25)),
        ]);
        return new WarpLogicalMachineLayout(kernel);
    }

    private sealed record ValidationRecord(string Entry, string Backend, string Target, string Format, int Bytes, string Hash, string Toolchain);
}
