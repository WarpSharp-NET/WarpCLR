using System.Buffers.Binary;
using System.Text;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests;

internal sealed partial class WarpManagedWideAtomicTests
{
    [TestMethod]
    public void RequiredOpenClFeaturesAreExplicitAndConditionalOnActualWideOperations()
    {
        foreach (WarpLogicalMachineLayout layout in WarpManagedWideAtomicKernels.Create64())
        {
            string source = WarpPortableMachineEmitter.Emit(layout, WarpBackendKind.SPIRV);
            StringAssert.Contains(source, WarpManagedWideAtomicOpCode.OpenClBaseExtension, StringComparison.Ordinal);
            StringAssert.Contains(source, WarpManagedWideAtomicOpCode.OpenClExtendedExtension, StringComparison.Ordinal);
            StringAssert.Contains(source, WarpManagedWideAtomicOpCode.OpenClValidation, StringComparison.Ordinal);
        }
        foreach (WarpLogicalMachineLayout layout in new[] { WarpPortableAtomicWordKernels.Arena(), WarpPortableAtomicWordKernels.State(), WarpManagedAtomicKernels.Create32()[2] })
        {
            Assert.IsFalse(WarpPortableMachineEmitter.Emit(layout, WarpBackendKind.SPIRV).Contains(WarpManagedWideAtomicOpCode.OpenClBaseExtension, StringComparison.Ordinal));
        }
        byte[] original = AtomicModule(), declared = WarpSpirVAtomicRequirements.Declare(original, 4096);
        var retained = new List<byte>(); retained.AddRange(declared.AsSpan(0, 20).ToArray());
        var extensions = new List<string>();
        for (int offset = 20; offset < declared.Length;)
        {
            uint header = BinaryPrimitives.ReadUInt32LittleEndian(declared.AsSpan(offset));
            int bytes = (int)(header >> 16) * 4;
            if ((header & 0xFFFF) == 10)
            {
                extensions.Add(Encoding.UTF8.GetString(declared.AsSpan(offset + 4, bytes - 4)).TrimEnd('\0'));
            }
            else { retained.AddRange(declared.AsSpan(offset, bytes).ToArray()); }
            offset += bytes;
        }
        CollectionAssert.AreEqual(new[] { WarpManagedWideAtomicOpCode.OpenClBaseExtension, WarpManagedWideAtomicOpCode.OpenClExtendedExtension }, extensions.ToArray());
        CollectionAssert.AreEqual(original, retained.ToArray());
    }

    [TestMethod]
    public void MalformedOrOversizedModulesCannotAcquireAtomicExtensionDeclarations()
    {
        byte[] valid = AtomicModule();
        Assert.ThrowsExactly<InvalidDataException>(() => WarpSpirVAtomicRequirements.Declare(valid.AsSpan(0, valid.Length - 1), 4096));
        Assert.ThrowsExactly<InvalidDataException>(() => WarpSpirVAtomicRequirements.Declare(valid, valid.Length));
        foreach ((int offset, uint value) in new[] { (0, 0u), (20, 17u), (20, 0xFFFF0011u), (24, 11u) })
        {
            byte[] corrupt = (byte[])valid.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(offset), value);
            Assert.ThrowsExactly<InvalidDataException>(() => WarpSpirVAtomicRequirements.Declare(corrupt, 4096));
        }
    }

    private static byte[] AtomicModule()
    {
        uint[] words = [0x07230203, 0x00010000, 0, 200, 0, 0x00020011, 12, 0x0003000E, 2, 2];
        byte[] bytes = new byte[words.Length * 4];
        for (int index = 0; index < words.Length; index++) { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), words[index]); }
        return bytes;
    }
}
