using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace WarpCLR.IR;

public static class WarpIrHash
{
    public static string Compute(WarpLinearKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, WarpProfileCatalog.ProfileId);
        AppendString(hash, kernel.Name);
        AppendInt32(hash, kernel.InputBufferCount);
        AppendInt32(hash, kernel.ScalarArgumentCount);
        AppendInt32(hash, kernel.Result);
        AppendInt32(hash, kernel.Instructions.Count);

        foreach (WarpIrInstruction instruction in kernel.Instructions)
        {
            AppendInt32(hash, instruction.Result);
            AppendInt32(hash, (int)instruction.OpCode);
            AppendInt32(hash, instruction.Left);
            AppendInt32(hash, instruction.Right);
            AppendUInt32(hash, instruction.Immediate);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
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
