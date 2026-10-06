using System.Text.Json.Serialization;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableWordExecutionCapabilities(bool FrameOwners = false, bool RuntimeStateAccess = false,
    bool NonlocalStateDispatch = false, bool ManagedExceptionTermination = false, bool LogicalWorkerAccess = false,
    bool PrivateController = false, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool PrivateHelperBoundaries = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool PrivateHelperReturnFences = false);
