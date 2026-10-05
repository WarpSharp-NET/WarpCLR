using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class MethodLowerer
    {
        private WarpPortableSourceOperationMetadata SourceOperation(WarpPortableTypedInstruction instruction) =>
            WarpPortableSourceOperationCatalog.Describe(method.Identity,
                source.Instructions.First(item => item.Offset == instruction.Offset).OpCode.Name!, instruction, owner.Types);
    }
}
