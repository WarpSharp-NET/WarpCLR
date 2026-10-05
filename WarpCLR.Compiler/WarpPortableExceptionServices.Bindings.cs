using System.Collections.Immutable;
using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    internal static ImmutableDictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>> BankBindings()
    {
        string[] mixed = [nameof(CaptureFrames), nameof(RefreshContinuation), nameof(ReleaseCaughtForReturn), nameof(ReturnFrame), nameof(PruneTransferScopes),
            nameof(ValidateCapture), nameof(ValidateCapturedAlias), nameof(CopyFrames), nameof(PruneCaught),
            nameof(ApplyAction), nameof(WriteCatchReference), nameof(ValidateTransfer), nameof(AcknowledgeTransfer), nameof(PrepareFilterTransfer), nameof(PrepareFilterEnd), nameof(ValidateFilterOwner), nameof(SetFilterTransfer)];
        mixed = [.. mixed, nameof(PrepareEscapedOwners), nameof(ValidateTemporaryCleanup), nameof(ValidateTemporaryFrame),
            nameof(TemporaryFrame), nameof(TemporaryReference), nameof(ClearTemporaryCleanup), nameof(PrepareActionTransfer), nameof(FinishFilterPreparation)];
        mixed = [.. mixed, nameof(ValidateFaultCensus)];
        string[] pure = [nameof(Begin), nameof(Worker), nameof(Record), nameof(Frame), nameof(Report), nameof(Site), nameof(Clause), nameof(SameAdmission),
            nameof(SetAction), nameof(Active), nameof(Range), nameof(Fits), nameof(Descriptor), nameof(ValidateTables), nameof(Table), nameof(Region),
            nameof(ValidateHeapTables), nameof(ValidateRootAssignments), nameof(RequireObject), nameof(RequireException), nameof(Assignable),
            nameof(LookupSite), nameof(LookupPc), nameof(LookupBody), nameof(ValidList), nameof(Root), nameof(OwnedRoot), nameof(PublishRoot),
            nameof(ClearRecord), nameof(CopyOwner), nameof(ValidMachinePc), nameof(BindTraceReport), nameof(SealReports), nameof(UniqueTrace),
            nameof(AvailableReport), nameof(WriteTrace), nameof(WriteLogicalFrames), nameof(LogicalFrameSite), nameof(ProjectManagedTrace), nameof(CopyPropagationFrames), nameof(TracePayload),
            nameof(RaiseReference), nameof(PreparedAtSite), nameof(StartRaise), nameof(RaisePreparedImplicit),
            nameof(InitializeRaisedReference), nameof(RestoreExceptionTrace),
            nameof(AdvanceSearch), nameof(SearchStep), nameof(EligibleClause), nameof(CompleteFilter), nameof(AdvanceUnwind), nameof(UnwindStep), nameof(AtSelectedGroup), nameof(EndCleanup), nameof(EndCleanupAt),
            nameof(Rethrow), nameof(FindCaught), nameof(BeginLeave), nameof(RaiseResource), nameof(PrepareTransfer), nameof(DiscardPrepared),
            nameof(ResetForDispatch), nameof(DisposeRecords), nameof(ValidLifetimeController), nameof(ClearWorkerRecords),
            nameof(PrepareEscaped), nameof(ValidateEscapedReference), nameof(ReferenceRootMatches), nameof(PublishEscapedFault), nameof(BeginPublication)];
        pure = [.. pure, nameof(ValidateTemporarySlice), nameof(ValidTemporaryOwner)];
        pure = [.. pure, nameof(ValidateFaultEnvironment), nameof(FaultPcMatches), nameof(FaultAliasIsLive), nameof(RaiseFaultOperation),
            nameof(FaultOperationExists), nameof(ValidateFaultTraceReport), nameof(FaultRootMatches), nameof(FaultRecordRootsMatch),
            nameof(ValidateFaultOperationTable), nameof(ValidateFaultPublication), nameof(ValidateFaultTraceFrames)];
        var bindings = ImmutableDictionary.CreateBuilder<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>>();
        foreach (string name in mixed)
        {
            MethodInfo method = Method(name); ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length < 2 || parameters[0].ParameterType != typeof(uint[]) || parameters[1].ParameterType != typeof(uint[]))
            {
                throw new InvalidOperationException("The explicit mixed EH bank signature has changed.");
            }
            bindings.Add(method, Array.AsReadOnly(parameters.Select((parameter, index) => index == 0 ? WarpRuntimeWordBank.State :
                index == 1 ? WarpRuntimeWordBank.Arena : parameter.ParameterType == typeof(uint[]) ?
                    throw new InvalidOperationException("A third EH bank is not admitted.") : WarpRuntimeWordBank.Word).ToArray()));
        }
        foreach (string name in pure)
        {
            MethodInfo method = Method(name);
            bindings.Add(method, Array.AsReadOnly(method.GetParameters().Select(parameter => parameter.ParameterType == typeof(uint[])
                ? WarpRuntimeWordBank.Arena : WarpRuntimeWordBank.Word).ToArray()));
        }
        return bindings.ToImmutable();
    }

    internal static MethodInfo Method(string name) => typeof(WarpPortableExceptionServices).GetMethod(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) ?? throw new ArgumentException("Unknown closed EH service.", nameof(name));
}
