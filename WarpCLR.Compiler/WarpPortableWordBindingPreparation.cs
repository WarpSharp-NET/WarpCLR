using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal abstract class WarpPortableWordBindingPreparation
{
    internal abstract WarpPortableMethodGraph Graph { get; }
    internal abstract WarpPortableTypedProgram Program { get; }
    internal abstract ImmutableArray<WarpPortableWordBody> SourceBodies { get; }
    internal abstract ImmutableArray<WarpControlFlowFunction> CompiledSourceFunctions { get; }
    internal abstract WarpLogicalBodyMetadata SourceMetadata(int function);
    internal abstract WarpPortableGeneratedServiceImporter Services { get; }
    internal abstract int ReserveFunction(string identity);
    internal abstract void InstallFunction(WarpControlFlowFunction function, WarpLogicalBodyMetadata metadata);
    internal abstract void InstallFilterAlias(WarpControlFlowFunction function, WarpLogicalBodyMetadata metadata, WarpPortableWordBody body);
    internal abstract void RequireService(string identity);

    internal WarpPortableWordBody SourceBody(string methodIdentity) => SourceBodies.First(body => string.Equals(body.MethodIdentity, methodIdentity, StringComparison.Ordinal));
    internal int SourceFunction(string methodIdentity) => SourceBody(methodIdentity).Function - 1;
    internal int SourceBlock(string methodIdentity, int sourceOffset) => SourceBody(methodIdentity).SourceBlocks.First(block => block.Instruction.Offset == sourceOffset).Block;
}
