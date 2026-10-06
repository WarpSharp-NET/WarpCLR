using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceOperand(int StackIndex, int PrivateWordOffset, WarpPortableTypedValue Value);
