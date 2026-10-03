using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

public enum WarpProfileFeature
{
    VerifiedModuleIntake,
    UnsignedScalar,
    TypedUnsignedBuffers,
    OneDimensionalParallelMap,
    ConditionalControlFlow,
    ControlFlowGraph,
    BackwardControlFlow,
    ClosedWorldStaticCalls,
    DeterministicAotPackaging,
    ExplicitHostDispatch,
    ExactUnsignedReductions,
}
