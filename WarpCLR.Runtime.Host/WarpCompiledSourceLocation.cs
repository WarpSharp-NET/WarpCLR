using WarpCLR.Compiler;

namespace WarpCLR.Runtime.Host;

internal sealed record WarpCompiledSourceLocation(int Function, int ProgramCounter,
    WarpPortableWordBody? Body, WarpPortableWordSourceBlock? Block)
{
    internal uint CilOffset => checked((uint)(Block?.Instruction.Offset ?? 0));
    internal uint Instruction => checked((uint)(Block?.Block ?? 0));
}
