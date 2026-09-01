using WarpCLR.IR;
using WarpCLR.Runtime.Device;

namespace WarpCLR.Tests.Features;

[TestClass]
public sealed class ScopedMemoryFeatureTests
{
    [TestMethod]
    [FourBackends]
    public void Sealed_objects_have_exact_layout_and_zero_initialization(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        WarpTypeLayout layout = CreateNodeLayout();
        using var region = new WarpScopedRegion(128);

        WarpManagedReference parent = region.AllocateObject(layout);
        WarpManagedReference child = region.AllocateObject(layout);

        Assert.IsTrue(layout.IsSealed);
        CollectionAssert.AreEqual(new[] { 4 }, layout.ReferenceFieldOffsets.ToArray());
        Assert.AreEqual(0u, region.ReadUInt32Field(parent, "value"));
        Assert.IsTrue(region.ReadReferenceField(parent, "next").IsNull);

        region.WriteUInt32Field(parent, "value", 0xDEADBEEFu);
        region.WriteReferenceField(parent, "next", child);
        Assert.AreEqual(0xDEADBEEFu, region.ReadUInt32Field(parent, "value"));
        Assert.AreEqual(child, region.ReadReferenceField(parent, "next"));
    }

    [TestMethod]
    [FourBackends]
    public void UInt32_arrays_are_zero_initialized_and_mutable(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        using var region = new WarpScopedRegion(128);
        WarpManagedReference array = region.AllocateUInt32Array(5);

        Assert.AreEqual(5, region.GetArrayLength(array));
        for (int index = 0; index < 5; index++)
        {
            Assert.AreEqual(0u, region.ReadUInt32ArrayElement(array, index));
            region.WriteUInt32ArrayElement(array, index, unchecked((uint)(index * 17)));
        }

        for (int index = 0; index < 5; index++)
        {
            Assert.AreEqual(unchecked((uint)(index * 17)), region.ReadUInt32ArrayElement(array, index));
        }
    }

    [TestMethod]
    [FourBackends]
    public void Disposal_reclaims_the_complete_activation(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        var region = new WarpScopedRegion(64);
        WarpManagedReference array = region.AllocateUInt32Array(4);
        region.WriteUInt32ArrayElement(array, 0, uint.MaxValue);

        region.Dispose();

        Assert.IsTrue(region.IsDisposed);
        Assert.AreEqual(0, region.UsedBytes);
        Assert.AreEqual(0, region.AllocationCount);
        WarpDeviceRuntimeException exception = Assert.ThrowsExactly<WarpDeviceRuntimeException>(
            () => region.ReadUInt32ArrayElement(array, 0));
        Assert.AreEqual("WRPDEV1004", exception.Code);
    }

    [TestMethod]
    [FourBackends]
    public void Activation_local_reference_flows_are_accepted(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        var verifier = new WarpScopedLifetimeVerifier();

        verifier.Verify(
        [
            new WarpScopedReferenceFlow("local", WarpScopedDestination.ActivationLocal),
            new WarpScopedReferenceFlow("field", WarpScopedDestination.SameActivationObject),
        ]);
    }

    private static WarpTypeLayout CreateNodeLayout() => new(
        "WarpCLR.Tests.Node",
        size: 8,
        alignment: 8,
        fields:
        [
            new WarpFieldLayout("value", WarpFieldKind.UInt32, 0),
            new WarpFieldLayout("next", WarpFieldKind.ManagedReference, 4),
        ]);
}
