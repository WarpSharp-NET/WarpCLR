using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace WarpCLR.IR;

public static class WarpIrHash
{
    public static string Compute(WarpControlFlowKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, WarpProfileCatalog.ProfileId);
        AppendString(hash, kernel.Name);
        AppendInt32(hash, kernel.InputBufferCount);
        AppendInt32(hash, kernel.ScalarArgumentCount);
        AppendInt32(hash, kernel.Reduction.HasValue ? (int)kernel.Reduction.Value : -1);
        AppendInt32(hash, kernel.Blocks.Count);

        foreach (WarpBasicBlock block in kernel.Blocks)
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
                AppendInt32(hash, (int)instruction.OpCode);
                AppendInt32(hash, instruction.Left);
                AppendInt32(hash, instruction.Right);
                AppendUInt32(hash, instruction.Immediate);
                AppendInt32(hash, instruction.Third);
            }

            AppendTerminator(hash, block.Terminator);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendTerminator(IncrementalHash hash, WarpBlockTerminator terminator)
    {
        switch (terminator)
        {
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
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
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
