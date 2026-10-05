using WarpCLR.Compiler;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    private static WarpCoreCLRPreparedCleanup[] ControllerBindings(uint[] arena, uint scheduler, uint controller,
        WarpCoreCLRControllerOperation operation, WarpCoreCLRWorkerLease emergency, WarpCoreCLRWorkerLease publication, WarpCoreCLRWorkerLease stopped)
    {
        uint dispatch = arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration], epoch = arena[scheduler + WarpPortableSchedulerLayout.GCEpoch];
        var cleanup = new List<WarpCoreCLRPreparedCleanup>
        {
            new(stopped, WarpCoreCLRCleanupPurpose.StoppedController, 0, WordBanks(scheduler, controller, (uint)operation, dispatch, epoch, dispatch, epoch), []),
            new(emergency, WarpCoreCLRCleanupPurpose.ReleaseCapturedController, controller, WordBanks(scheduler + WarpPortableSchedulerLayout.ControllerOwner, controller, 0), []),
            new(publication, WarpCoreCLRCleanupPurpose.PublishControllerRelease, controller, WordBanks(scheduler + WarpPortableSchedulerLayout.ControllerOwner, controller, 0), []),
        };
        bool unchanged = operation == WarpCoreCLRControllerOperation.RequestCollection && arena[scheduler + WarpPortableSchedulerLayout.GCState] == WarpPortableSchedulerLayout.GCRequested;
        if (!unchanged)
        {
            uint nextDispatch = operation == WarpCoreCLRControllerOperation.BeginDispatch ? checked(dispatch + 1) : dispatch;
            uint nextEpoch = operation == WarpCoreCLRControllerOperation.RequestCollection ? checked(epoch + 1) : epoch;
            cleanup.Add(new(stopped, WarpCoreCLRCleanupPurpose.StoppedController, 0,
                WordBanks(scheduler, controller, (uint)operation, dispatch, epoch, nextDispatch, nextEpoch), []));
        }
        return cleanup.ToArray();
    }
}
