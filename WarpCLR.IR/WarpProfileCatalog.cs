using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public enum WarpProfileFeature
{
    VerifiedModuleIntake,
    UnsignedScalar,
    TypedUnsignedBuffers,
    OneDimensionalParallelMap,
    ScopedManagedMemory,
    DeterministicAotPackaging,
    ExplicitHostDispatch,
    ExactUnsignedReductions,
}

public enum WarpFeatureLayer
{
    WarpCil,
    WarpClr,
}

public readonly record struct WarpFeatureDescriptor(
    WarpProfileFeature Feature,
    WarpFeatureLayer Layer);

public static class WarpCapabilityCatalog
{
    public const string Scalar = "warp.core.scalar/0.1";

    public const string Parallel = "warp.core.parallel/0.1";

    public const string Buffers = "warp.core.buffers/0.1";

    public const string ScopedMemory = "warp.memory.scoped/0.1";
}

public static class WarpProfileCatalog
{
    public const string ProfileId = "warpclr.profile/0.1";

    private static readonly ReadOnlyCollection<WarpFeatureDescriptor> FeatureDescriptors =
        Array.AsReadOnly<WarpFeatureDescriptor>(
        [
            new(WarpProfileFeature.VerifiedModuleIntake, WarpFeatureLayer.WarpClr),
            new(WarpProfileFeature.UnsignedScalar, WarpFeatureLayer.WarpCil),
            new(WarpProfileFeature.TypedUnsignedBuffers, WarpFeatureLayer.WarpCil),
            new(WarpProfileFeature.OneDimensionalParallelMap, WarpFeatureLayer.WarpCil),
            new(WarpProfileFeature.ScopedManagedMemory, WarpFeatureLayer.WarpCil),
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
            WarpCapabilityCatalog.ScopedMemory,
        ]);

    private static readonly ReadOnlyCollection<WarpIrOpCode> IntegerMapInstructionSet =
        Array.AsReadOnly(
        [
            WarpIrOpCode.LoadInput,
            WarpIrOpCode.LoadScalar,
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
        ]);

    public static IReadOnlyList<WarpFeatureDescriptor> Features => FeatureDescriptors;

    public static IReadOnlyList<string> RequiredCapabilities => CapabilityIdentifiers;

    public static IReadOnlyList<WarpIrOpCode> IntegerMapInstructions => IntegerMapInstructionSet;
}
