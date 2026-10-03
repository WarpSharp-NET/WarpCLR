using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

public static class WarpProfileCatalog
{
    public const string ProfileId = "warpclr.profile/0.3";

    private static readonly ReadOnlyCollection<WarpFeatureDescriptor> FeatureDescriptors =
        Array.AsReadOnly<WarpFeatureDescriptor>(
        [
            new(WarpProfileFeature.VerifiedModuleIntake, WarpFeatureLayer.WarpClr),
            new(WarpProfileFeature.UnsignedScalar, WarpFeatureLayer.WarpCil),
            new(WarpProfileFeature.TypedUnsignedBuffers, WarpFeatureLayer.WarpCil),
            new(WarpProfileFeature.OneDimensionalParallelMap, WarpFeatureLayer.WarpCil),
            new(WarpProfileFeature.ConditionalControlFlow, WarpFeatureLayer.WarpCil),
            new(WarpProfileFeature.ControlFlowGraph, WarpFeatureLayer.WarpClr),
            new(WarpProfileFeature.BackwardControlFlow, WarpFeatureLayer.WarpCil),
            new(WarpProfileFeature.ClosedWorldStaticCalls, WarpFeatureLayer.WarpCil),
            new(WarpProfileFeature.DeterministicAotPackaging, WarpFeatureLayer.WarpClr),
            new(WarpProfileFeature.ExplicitHostDispatch, WarpFeatureLayer.WarpClr),
            new(WarpProfileFeature.ExactUnsignedReductions, WarpFeatureLayer.WarpCil),
        ]);

    private static readonly ReadOnlyCollection<string> CapabilityIdentifiers =
        Array.AsReadOnly(
        [
            WarpCapabilityCatalog.Scalar,
            WarpCapabilityCatalog.Parallel,
            WarpCapabilityCatalog.Buffers,
            WarpCapabilityCatalog.ControlFlow,
            WarpCapabilityCatalog.Calls,
        ]);

    private static readonly ReadOnlyCollection<WarpIrOpCode> IntegerMapInstructionSet =
        Array.AsReadOnly(
        [
            WarpIrOpCode.LoadInput,
            WarpIrOpCode.LoadScalar,
            WarpIrOpCode.LoadArgument,
            WarpIrOpCode.Constant,
            WarpIrOpCode.BitwiseNot,
            WarpIrOpCode.Add,
            WarpIrOpCode.Subtract,
            WarpIrOpCode.Multiply,
            WarpIrOpCode.BitwiseAnd,
            WarpIrOpCode.BitwiseOr,
            WarpIrOpCode.ExclusiveOr,
            WarpIrOpCode.ShiftLeft,
            WarpIrOpCode.ShiftRightLogical,
            WarpIrOpCode.Equal,
            WarpIrOpCode.NotEqual,
            WarpIrOpCode.LessThanUnsigned,
            WarpIrOpCode.LessThanOrEqualUnsigned,
            WarpIrOpCode.GreaterThanUnsigned,
            WarpIrOpCode.GreaterThanOrEqualUnsigned,
            WarpIrOpCode.Select,
            WarpIrOpCode.Call,
        ]);

    private static readonly ReadOnlyCollection<WarpControlFlowOperation> ControlFlowOperations =
        Array.AsReadOnly(
        [
            WarpControlFlowOperation.BlockArguments,
            WarpControlFlowOperation.Branch,
            WarpControlFlowOperation.ConditionalBranch,
            WarpControlFlowOperation.Return,
        ]);

    private static readonly WarpBackendContract PortableContract = new(
        ProfileId,
        IntegerMapInstructionSet,
        ControlFlowOperations,
        WarpReductionContract.Operations.Select(descriptor => descriptor.Operation));

    public static IReadOnlyList<WarpFeatureDescriptor> Features => FeatureDescriptors;

    public static IReadOnlyList<string> RequiredCapabilities => CapabilityIdentifiers;

    public static IReadOnlyList<WarpIrOpCode> IntegerMapInstructions => IntegerMapInstructionSet;

    public static IReadOnlyList<WarpControlFlowOperation> ControlFlow => ControlFlowOperations;

    public static WarpBackendContract BackendContract => PortableContract;
}
