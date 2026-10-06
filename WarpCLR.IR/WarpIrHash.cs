using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace WarpCLR.IR;

public static class WarpIrHash
{
    public static string Compute(WarpControlFlowKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "warp.ir-identity/raw-utf16-code-units/0.2");
        AppendString(hash, WarpProfileCatalog.ProfileId);
        AppendString(hash, kernel.Name);
        AppendInt32(hash, kernel.InputBufferCount);
        AppendInt32(hash, kernel.ScalarArgumentCount);
        AppendInt32(hash, kernel.Reduction.HasValue ? (int)kernel.Reduction.Value : -1);
        AppendBody(hash, kernel.Blocks);
        AppendInt32(hash, kernel.Functions.Count);
        foreach (WarpControlFlowFunction function in kernel.Functions)
        {
            AppendInt32(hash, function.Id);
            AppendString(hash, function.Name);
            AppendInt32(hash, function.ParameterCount);
            AppendBody(hash, function.Blocks);
        }

        if (kernel.Execution is WarpLogicalExecutionMetadata execution)
        {
            AppendString(hash, execution.IdentityVersion);
            AppendInt32(hash, execution.RecursiveCalls ? 1 : 0);
            AppendInt32(hash, execution.FrameOwners ? 1 : 0);
            AppendInt32(hash, execution.RuntimeStateAccess ? 1 : 0);
            AppendInt32(hash, execution.NonlocalStateDispatch ? 1 : 0);
            AppendInt32(hash, execution.ManagedExceptionTermination ? 1 : 0);
            AppendInt32(hash, execution.LogicalWorkerAccess ? 1 : 0);
            if (execution.LogicalWorkerAccess) { AppendString(hash, WarpManagedInvocationOpCode.Version); }
            if (execution.PrivateControllerProjection is { } projection)
            {
                AppendPrivateController(hash, projection);
            }
            foreach (WarpLogicalBodyMetadata body in execution.Bodies)
            {
                AppendInt32(hash, body.PrivateWordCount);
                AppendInt32(hash, body.RuntimeHelper ? 1 : 0);
                AppendInt32(hash, body.CountsSourceDepth ? 1 : 0);
                AppendInt32(hash, body.AliasOwnerFunction);
                AppendInt32(hash, body.AliasPrefixWords);
                AppendInt32(hash, body.SourceBlockCosts.Count);
                foreach (int cost in body.SourceBlockCosts) { AppendInt32(hash, cost); }
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendPrivateController(IncrementalHash hash, WarpPrivateControllerProjection projection)
    {
        if (projection.RequiresHelperReturnFences)
        {
            AppendString(hash, WarpPrivateControllerProjection.HelperReturnFenceSemantics);
            AppendString(hash, WarpLogicalMachineLayout.PrivateHelperScopeVersion);
        }
        if (projection.RequiresHelperBoundaries) { AppendString(hash, WarpPrivateControllerProjection.HelperBoundarySemantics); }
        AppendString(hash, WarpPrivateControllerOpCode.Version);
        AppendInt32(hash, projection.Uses.Count);
        foreach (WarpPrivateControllerUse use in projection.Uses)
        {
            AppendInt32(hash, use.Function); AppendInt32(hash, use.Block); AppendInt32(hash, use.Value);
            AppendInt32(hash, use.Callee); AppendInt32(hash, use.Argument); AppendInt32(hash, use.CallValue);
            AppendString(hash, use.ServiceIdentity);
        }
    }

    private static void AppendBody(
        IncrementalHash hash,
        ReadOnlyCollection<WarpBasicBlock> blocks)
    {
        AppendInt32(hash, blocks.Count);

        foreach (WarpBasicBlock block in blocks)
        {
            AppendInt32(hash, block.Id);
            AppendInt32(hash, block.Parameters.Count);
            foreach (WarpBlockParameter parameter in block.Parameters)
            {
                AppendInt32(hash, parameter.Value);
                AppendInt32(hash, (int)parameter.Type);
            }

            AppendInt32(hash, block.Instructions.Count);
            foreach (WarpIrInstruction instruction in block.Instructions)
            {
                AppendInt32(hash, instruction.Result);
                AppendInt32(hash, (int)instruction.ResultType);
                AppendInt32(hash, instruction.OpCode == WarpIrOpCode.Call && instruction.ResultWordCount != 1 ? 0x10014 : (int)instruction.OpCode);
                if (WarpManagedWideAtomicOpCode.IsAtomic(instruction.OpCode)) { AppendString(hash, WarpManagedWideAtomicOpCode.Semantics); }
                AppendInt32(hash, instruction.Left);
                AppendInt32(hash, instruction.Right);
                AppendUInt32(hash, instruction.Immediate);
                AppendInt32(hash, instruction.Third);
                AppendInt32(hash, instruction.Callee);
                AppendInt32(hash, instruction.Arguments.Count);
                foreach (int argument in instruction.Arguments)
                {
                    AppendInt32(hash, argument);
                }

                if (instruction.ResultWordCount != 1)
                {
                    AppendInt32(hash, instruction.ResultWordCount);
                }
            }

            AppendTerminator(hash, block.Terminator);
        }

    }

    private static void AppendTerminator(IncrementalHash hash, WarpBlockTerminator terminator)
    {
        switch (terminator)
        {
            case WarpManagedExceptionTerminator managed:
                AppendInt32(hash, 0x10005);
                AppendInt32(hash, managed.ResultWordCount);
                AppendInt32(hash, managed.Context);
                AppendInt32(hash, managed.ObjectId);
                AppendInt32(hash, managed.Generation);
                break;
            case WarpStateDispatchTerminator dispatch:
                AppendInt32(hash, 0x10004);
                AppendInt32(hash, dispatch.ResultWordCount);
                AppendInt32(hash, dispatch.Destinations.Count);
                foreach (WarpStateDispatchTarget target in dispatch.Destinations)
                {
                    AppendInt32(hash, target.Function);
                    AppendInt32(hash, target.Block);
                }
                break;
            case WarpBranchTerminator branch:
                AppendInt32(hash, (int)WarpControlFlowOperation.Branch);
                AppendTarget(hash, branch.Target);
                break;

            case WarpConditionalBranchTerminator conditional:
                AppendInt32(hash, (int)WarpControlFlowOperation.ConditionalBranch);
                AppendInt32(hash, conditional.Condition);
                AppendTarget(hash, conditional.WhenNonZero);
                AppendTarget(hash, conditional.WhenZero);
                break;

            case WarpReturnTerminator @return:
                AppendInt32(hash, (int)WarpControlFlowOperation.Return);
                AppendInt32(hash, @return.Value);
                break;

            case WarpTupleReturnTerminator tuple:
                AppendInt32(hash, 0x10003);
                AppendInt32(hash, tuple.Values.Count);
                foreach (int value in tuple.Values)
                {
                    AppendInt32(hash, value);
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(terminator));
        }
    }

    private static void AppendTarget(IncrementalHash hash, WarpBranchTarget target)
    {
        AppendInt32(hash, target.Block);
        AppendInt32(hash, target.Arguments.Count);
        foreach (int argument in target.Arguments)
        {
            AppendInt32(hash, argument);
        }
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        AppendInt32(hash, value.Length);
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        foreach (char unit in value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, unit);
            hash.AppendData(bytes);
        }
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendUInt32(IncrementalHash hash, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
