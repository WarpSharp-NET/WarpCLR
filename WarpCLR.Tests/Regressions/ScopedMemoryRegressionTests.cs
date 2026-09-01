using WarpCLR.IR;
using WarpCLR.Runtime.Device;

namespace WarpCLR.Tests.Regressions;

[TestClass]
public sealed class ScopedMemoryRegressionTests
{
    [TestMethod]
    [FourBackends]
    public void Allocation_exhaustion_is_deterministic_and_has_no_partial_allocation(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        var layout = new WarpTypeLayout(
            "WarpCLR.Tests.Value",
            size: 8,
            alignment: 8,
            [new WarpFieldLayout("value", WarpFieldKind.UInt32, 0)]);
        using var region = new WarpScopedRegion(24);
        _ = region.AllocateObject(layout);
        int usedBytes = region.UsedBytes;

        WarpDeviceRuntimeException exception = Assert.ThrowsExactly<WarpDeviceRuntimeException>(
            () => region.AllocateObject(layout));

        Assert.AreEqual("WRPDEV1000", exception.Code);
        Assert.AreEqual(1, region.AllocationCount);
        Assert.AreEqual(usedBytes, region.UsedBytes);
    }

    [TestMethod]
    [FourBackends]
    public void Array_bounds_are_checked(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        using var region = new WarpScopedRegion(64);
        WarpManagedReference array = region.AllocateUInt32Array(2);

        WarpDeviceRuntimeException exception = Assert.ThrowsExactly<WarpDeviceRuntimeException>(
            () => region.ReadUInt32ArrayElement(array, 2));

        Assert.AreEqual("WRPDEV1003", exception.Code);
    }

    [TestMethod]
    [FourBackends]
    public void References_cannot_cross_activation_regions(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        var layout = new WarpTypeLayout(
            "WarpCLR.Tests.Link",
            size: 4,
            alignment: 4,
            [new WarpFieldLayout("target", WarpFieldKind.ManagedReference, 0)]);
        using var first = new WarpScopedRegion(64);
        using var second = new WarpScopedRegion(64);
        WarpManagedReference owner = first.AllocateObject(layout);
        WarpManagedReference foreign = second.AllocateObject(layout);

        WarpDeviceRuntimeException exception = Assert.ThrowsExactly<WarpDeviceRuntimeException>(
            () => first.WriteReferenceField(owner, "target", foreign));

        Assert.AreEqual("WRPDEV1004", exception.Code);
    }

    [TestMethod]
    [FourBackends]
    public void Every_scoped_reference_escape_is_rejected(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        var verifier = new WarpScopedLifetimeVerifier();
        WarpScopedDestination[] escapes =
        [
            WarpScopedDestination.ReturnValue,
            WarpScopedDestination.StaticField,
            WarpScopedDestination.OutputBuffer,
            WarpScopedDestination.HostBoundary,
            WarpScopedDestination.LongerLivedObject,
        ];

        foreach (WarpScopedDestination destination in escapes)
        {
            WarpDeviceRuntimeException exception = Assert.ThrowsExactly<WarpDeviceRuntimeException>(
                () => verifier.Verify([new WarpScopedReferenceFlow("allocation", destination)]));
            Assert.AreEqual("WRPDEV1005", exception.Code);
            StringAssert.Contains(exception.Message, destination.ToString());
        }
    }
}
