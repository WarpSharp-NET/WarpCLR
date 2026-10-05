using System.Collections.ObjectModel;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCoreCLRStoppedCensus
{
    internal WarpCoreCLRStoppedCensus(WarpCoreCLRCommandAdmission[] admissions, WarpCoreCLRControllerAdmission? controller = null)
    { Admissions = Array.AsReadOnly(admissions); Controller = controller; }
    internal ReadOnlyCollection<WarpCoreCLRCommandAdmission> Admissions { get; }
    internal WarpCoreCLRControllerAdmission? Controller { get; }
    internal int Count => Admissions.Count + (Controller is null ? 0 : 1);
}
