using System.Buffers.Binary;
using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

internal static class WarpSpirVAtomicRequirements
{
    // LLVM19 has no forward metadata route for legacy OpenCL OpExtension strings.
    // Add only the two required declarations; every translated instruction and ID remains byte-for-byte unchanged.
    internal static byte[] Declare(ReadOnlySpan<byte> module, int maximumBytes)
    {
        if (module.Length < 20 || module.Length % 4 != 0 || module.Length > maximumBytes || Word(module, 0) != 0x07230203)
        {
            throw new InvalidDataException("The translated wide-atomic SPIR-V module is malformed or outside admission.");
        }
        int insertion = 20;
        bool capability = false;
        for (int offset = 20; offset < module.Length;)
        {
            uint header = Word(module, offset);
            int words = checked((int)(header >> 16));
            if (words == 0 || words > (module.Length - offset) / 4) { throw new InvalidDataException("Malformed SPIR-V instruction length."); }
            uint code = header & 0xFFFF;
            if (offset == insertion && code == 17) { insertion += words * 4; }
            if (code == 17 && words == 2 && Word(module, offset + 4) == 12) { capability = true; }
            offset += words * 4;
        }
        if (!capability) { throw new InvalidDataException("The wide-atomic module lost its required Int64Atomics capability."); }
        byte[] required = EncodeExtensions();
        if (module.Length > maximumBytes - required.Length) { throw new InvalidDataException("The declared atomic module exceeds image admission."); }
        byte[] declared = new byte[module.Length + required.Length];
        module[..insertion].CopyTo(declared); required.CopyTo(declared, insertion);
        module[insertion..].CopyTo(declared.AsSpan(insertion + required.Length));
        return declared;
    }

    private static byte[] EncodeExtensions()
    {
        using var stream = new MemoryStream();
        foreach (string name in new[] { WarpManagedWideAtomicOpCode.OpenClBaseExtension, WarpManagedWideAtomicOpCode.OpenClExtendedExtension })
        {
            byte[] text = Encoding.UTF8.GetBytes(name);
            int words = (text.Length + 4) / 4;
            byte[] instruction = new byte[(words + 1) * 4];
            BinaryPrimitives.WriteUInt32LittleEndian(instruction, checked(((uint)words + 1) << 16) | 10);
            text.CopyTo(instruction, 4); stream.Write(instruction);
        }
        return stream.ToArray();
    }

    private static uint Word(ReadOnlySpan<byte> module, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(module[offset..]);
}
