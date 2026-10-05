using System.Collections.ObjectModel;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCoreCLRGenerationTransition
{
    internal WarpCoreCLRGenerationTransition(ulong ordinal, uint[] arena, string operation, uint scheduler,
        uint fromDispatch, uint toDispatch, uint fromCollection, uint toCollection,
        WarpCoreCLRCommandAdmission[] predecessors, WarpCoreCLRControllerAdmission? controller = null)
    {
        Ordinal = ordinal; Arena = arena; Operation = operation; Scheduler = scheduler;
        FromDispatch = fromDispatch; ToDispatch = toDispatch; FromCollection = fromCollection; ToCollection = toCollection;
        Predecessors = Array.AsReadOnly(predecessors);
        Controller = controller;
    }
    internal ulong Ordinal { get; }
    internal uint[] Arena { get; }
    internal string Operation { get; }
    internal uint Scheduler { get; }
    internal uint FromDispatch { get; }
    internal uint ToDispatch { get; }
    internal uint FromCollection { get; }
    internal uint ToCollection { get; }
    internal ReadOnlyCollection<WarpCoreCLRCommandAdmission> Predecessors { get; }
    internal bool Applied { get; private set; }
    internal WarpCoreCLRControllerAdmission? Controller { get; }
    internal void MarkApplied(object authority)
    { WarpCoreCLRWorkerProcess.ValidateRegistryAuthority(authority); Applied = true; }
}
