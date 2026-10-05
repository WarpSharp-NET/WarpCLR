using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableWordStorageSlot(int Index, int WordOffset, WarpPortableTypedType Type);
