using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableCollectiveTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CheckedFaultsSelectTheFirstOutputThenItsLeftRightParentPostorder(bool minimumQuantum)
    {
        foreach (uint type in new uint[] { 1, 2, 3, 4 })
        {
            ulong maximum = type == 1 ? uint.MaxValue : type == 2 ? int.MaxValue : type == 3 ? ulong.MaxValue : long.MaxValue;
            ulong minimum = type == 2 ? 0x80000000UL : type == 4 ? 0x8000000000000000UL : 0;
            ulong negativeOne = type <= 2 ? uint.MaxValue : ulong.MaxValue;
            ulong[] inputs = type == 1 || type == 3 ? [maximum, 1, maximum, 1, 0, 0] : [maximum, 1, minimum, negativeOne, 0, 0];
            foreach (uint kind in new uint[] { 1, 2, 3 })
            {
                foreach (int residents in new[] { 1, 3 })
                {
                    Check(type, 1, 1, kind, inputs, generated: true, minimum: minimumQuantum, residents);
                }
            }
            Check(type, 4, 1, 1, [maximum, maximum, 0], generated: true, minimum: minimumQuantum, 3);
        }
    }

    [TestMethod]
    public void OwnershipVersionTwoAndTheParentTypeRootSchemaEnterAdmissionAndIdentity()
    {
        WarpPortableCollectivePlan plan = Plan(1, 1, 0, 1, 1);
        WarpPortableCollectivePlan changed = WarpPortableCollectivePlan.Create(1, 1, 0, 1, 1,
            Enumerable.Range(0, 131).Select(static member => (uint)(member * 3 + 7)), new string('1', 64));
        Assert.AreNotEqual(plan.Identity, changed.Identity, StringComparer.Ordinal);
        foreach (bool minimum in new[] { false, true })
        {
            var driver = new WarpPortableCollectiveDriver(plan, [7], generated: true, minimum, 1);
            driver.Arena[1] = 1;
            uint[] before = (uint[])driver.Arena.Clone();
            Assert.AreEqual(2u, driver.Control(nameof(WarpPortableCollectiveServices.BeginRun), 1));
            CollectionAssert.AreEqual(before, driver.Arena);
            driver.Arena[1] = 2;
            driver.Arena[driver.Descriptor + 46] = 1;
            before = (uint[])driver.Arena.Clone();
            Assert.AreEqual(2u, driver.Control(nameof(WarpPortableCollectiveServices.BeginRun), 1));
            CollectionAssert.AreEqual(before, driver.Arena);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SuspendedJobsKeepTheirOwnGenerationAndCancellationWaitsForQuiescence(bool minimumQuantum)
    {
        var driver = new WarpPortableCollectiveDriver(Plan(6, 1, 0, 2, 17), Inputs(6, 17, edges: false), generated: true, minimumQuantum, 3);
        Assert.AreEqual(0u, driver.Control(nameof(WarpPortableCollectiveServices.BeginRun), 1));
        uint descriptor = driver.Descriptor;
        Assert.AreEqual(0u, driver.Control(nameof(WarpPortableCollectiveServices.TryAcquireNode), 0, 7, 1));
        uint entry = driver.Arena[descriptor + 18];
        uint token = driver.Arena[entry + 3];
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, driver.PauseNode(0));
        Assert.AreEqual(0u, driver.Control(nameof(WarpPortableCollectiveServices.RebindWorkerRun), 0, driver.Generation, token, 2));
        Assert.AreEqual(0u, driver.Control(nameof(WarpPortableCollectiveServices.RequestCancellation)));
        Assert.AreEqual(1u, driver.Control(nameof(WarpPortableCollectiveServices.TryAcquireNode), 1, 10, 1));
        Assert.AreEqual(1u, driver.Control(nameof(WarpPortableCollectiveServices.CancelRun)));
        Assert.AreEqual(1u, driver.Control(nameof(WarpPortableCollectiveServices.FinishRun)));
        Assert.AreEqual(0u, driver.ResumePausedNode());
        Assert.AreEqual(2u, driver.Control(nameof(WarpPortableCollectiveServices.CompleteNode), 0, driver.Generation, token, 1));
        Assert.AreEqual(0u, driver.Control(nameof(WarpPortableCollectiveServices.CompleteNode), 0, driver.Generation, token, 2));
        Assert.AreEqual(5u, driver.Control(nameof(WarpPortableCollectiveServices.CancelRun)));
        Assert.AreEqual(0u, driver.Arena[descriptor + 41]);
        Assert.AreEqual(0u, driver.Control(nameof(WarpPortableCollectiveServices.BeginRun), 1));
        Assert.AreEqual(2u, driver.Execute(nameof(WarpPortableCollectiveServices.ComputeNode), [descriptor, 0, driver.Generation - 1, token]));
        Assert.AreEqual(0u, driver.Control(nameof(WarpPortableCollectiveServices.TryAcquireNode), 0, 7, 3));
        token = driver.Arena[entry + 3];
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, driver.PauseNode(0));
        Assert.AreEqual(0u, driver.Control(nameof(WarpPortableCollectiveServices.RequestCancellation)));
        driver.StopPausedNode();
        Assert.AreEqual(0u, driver.Control(nameof(WarpPortableCollectiveServices.AbandonStoppedNode), 0, driver.Generation, token));
        Assert.AreEqual(5u, driver.Control(nameof(WarpPortableCollectiveServices.AdvancePhase)));
        Assert.AreEqual(0u, driver.Arena[descriptor + 27]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PublicationAcknowledgmentAndNonwrappingIdentitiesRejectStaleRuns(bool minimumQuantum)
    {
        var driver = new WarpPortableCollectiveDriver(Plan(1, 1, 0, 1, 1), [19], generated: true, minimumQuantum, 1);
        Assert.AreEqual(3u, driver.Run());
        uint generation = driver.Generation;
        Assert.AreEqual(1u, driver.Control(nameof(WarpPortableCollectiveServices.BeginRun), 1));
        Assert.AreEqual(2u, driver.Control(nameof(WarpPortableCollectiveServices.AcknowledgeResults), generation + 1));
        Assert.AreEqual(0u, driver.Control(nameof(WarpPortableCollectiveServices.AcknowledgeResults), generation));
        driver.Arena[60] = 2;
        Assert.AreEqual(2u, driver.Control(nameof(WarpPortableCollectiveServices.AcknowledgeResults), generation));
        Assert.AreEqual(0u, driver.Control(nameof(WarpPortableCollectiveServices.BeginRun), 2));
        Assert.AreEqual(2u, driver.Control(nameof(WarpPortableCollectiveServices.TryAcquireNode), 0, 8, 1));
        uint worker = driver.Arena[driver.Descriptor + 18];
        driver.Arena[worker + 3] = uint.MaxValue;
        uint[] before = (uint[])driver.Arena.Clone();
        Assert.AreEqual(6u, driver.Control(nameof(WarpPortableCollectiveServices.TryAcquireNode), 0, 7, 1));
        CollectionAssert.AreEqual(before, driver.Arena);
        Assert.AreEqual(0u, driver.Control(nameof(WarpPortableCollectiveServices.RequestCancellation)));
        Assert.AreEqual(5u, driver.Control(nameof(WarpPortableCollectiveServices.CancelRun)));
        driver.Arena[driver.Descriptor + 23] = uint.MaxValue;
        before = (uint[])driver.Arena.Clone();
        Assert.AreEqual(6u, driver.Control(nameof(WarpPortableCollectiveServices.BeginRun), 2));
        CollectionAssert.AreEqual(before, driver.Arena);
        Assert.AreEqual(1u, driver.Execute(nameof(WarpPortableCollectiveServices.BeginRun), [driver.Descriptor, 7, 2]));
    }

    [TestMethod]
    public void RawSingletonsArithmeticNaNsSignedZerosAndPrefixAssociationHaveExactWitnesses()
    {
        foreach (uint type in new uint[] { 5, 6 })
        {
            ulong[] transport = type == 5 ? [0x7F812345, 0xFFC34567, 0x80000000, 1] :
                [0x7FF0000000001234, 0xFFF8456789ABCDEF, 0x8000000000000000, 1];
            foreach (ulong raw in transport)
            {
                foreach (uint operation in new uint[] { 1, 2, 3, 4 })
                {
                    Check(type, operation, 0, 1, [raw], generated: true, minimum: true, 1);
                    Check(type, operation, 0, 1, [raw, 0], generated: true, minimum: true, 2);
                }
            }
            ulong big = type == 5 ? 0x4B800000UL : 0x4340000000000000UL;
            ulong sign = type == 5 ? 0x80000000UL : 0x8000000000000000UL;
            ulong one = type == 5 ? 0x3F800000UL : 0x3FF0000000000000UL;
            ulong[] witness = [big | sign, 0, 0, 0, big, one];
            Assert.AreEqual(0UL, WarpPortableCollectiveOracle.Evaluate(type, 1, 0, 1, witness)[0].Bits);
            Check(type, 1, 0, 1, witness, generated: true, minimum: true, 3);
            Check(type, 1, 0, 2, witness, generated: true, minimum: true, 3);
        }
    }
}
